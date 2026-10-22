// csrt garbage collector: conservative, non-moving, stop-the-world mark-sweep.
//
// Design (docs/adr/0004-memory-management.md):
//   * Small objects (<= 2048 bytes) live in 256 KiB chunks, aligned to their size, each serving a
//     single size class. A chunk starts with a header holding the per-slot "allocated" and "mark"
//     bitmaps, so any candidate pointer maps to (chunk, slot) with a mask, a hash lookup and a
//     division — interior pointers included.
//   * Allocation works directly on the allocated bitmap: each size class caches one 64-slot
//     bitmap word's free mask, and the fast path is ctz + clear-lowest-bit + multiply (no free
//     lists to thread or rebuild). A whole word is claimed at once; before marking, the unused
//     part of each claimed word is handed back. Sweeping is just `allocated &= marked`.
//   * Large objects get their own calloc'd block and are tracked in an array that is sorted at the
//     start of each collection, so candidate pointers are resolved by binary search.
//   * Roots are scanned conservatively: the main thread's stack (with callee-saved registers
//     spilled by setjmp) and the writable segments (.data/.bss) of the main executable, which is
//     where the compiler puts static fields. Pointers outside the heap — e.g. string literals in
//     .rodata — are ignored. Every word of a reachable object is scanned conservatively, too.
//   * A collection runs when the bytes allocated since the last one exceed max(32 MiB, 2 x live bytes)
//     (CSRT_GC_TRIGGER_MB overrides the minimum).
//
// Threads (docs/11-threading.md): every managed thread registers itself (stack bounds, TLS block,
// allocation cache). Allocation caches are thread-local, so the fast path stays lock-free; slow
// paths take one global lock. A collection stops the world with a signal handshake (as Boehm GC
// does): each mutator spills its registers in a signal handler, records its stack pointer, acks and
// sleeps in sigsuspend until the collector resumes it. While the world is stopped the collector
// never calls malloc/free, stdio or the dynamic loader (a stopped thread may hold their locks).
//
// Limitations: conservative scanning can retain garbage that happens to look like a pointer; no
// finalizers; empty chunks are kept (resident) for reuse by any size class.
//
// Environment: CSRT_GC=0 disables collection; CSRT_GC_STATS=1 prints statistics at exit.
#define _GNU_SOURCE
#include "gc.h"
#include "csrt.h"

#include <errno.h>
#include <link.h>
#include <pthread.h>
#include <semaphore.h>
#include <setjmp.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>

#define CHUNK_SIZE   ((size_t)256 * 1024)
#define CHUNK_HEADER ((size_t)4096)
#define MAX_SLOTS    ((CHUNK_SIZE - CHUNK_HEADER) / 16)
#define LARGE_LIMIT  ((size_t)2048)
#define MIN_TRIGGER  ((size_t)32 * 1024 * 1024)

static const uint32_t k_size_classes[17] = {16, 32, 48, 64, 80, 96, 128, 160, 192, 256, 320, 384, 512, 768, 1024, 1536, 2048};
#define NUM_CLASSES (sizeof k_size_classes / sizeof k_size_classes[0])

typedef struct Chunk Chunk;
struct Chunk {
    uint32_t object_size;  // 0 = empty chunk, available for any size class
    uint32_t slots;
    uint32_t live;         // live slots after the last sweep
    uint32_t size_class;
    Chunk* next;           // all chunks
    Chunk* next_free;      // per-class list of chunks with free slots / list of empty chunks
    uint64_t allocated[MAX_SLOTS / 64];
    uint64_t marked[MAX_SLOTS / 64];
};
_Static_assert(sizeof(Chunk) <= CHUNK_HEADER, "chunk header too large");

/// Per-thread allocation cache: one claimed bitmap word per size class. Only its owner writes it.
typedef struct {
    uint64_t free_bits;   // unallocated slots of the claimed word: bit i -> word_base + i * size
    uint8_t* word_base;
} AllocCache;

static _Thread_local AllocCache tl_cache[17];

/// Shared per-class state (guarded by g.lock).
typedef struct {
    Chunk* chunk;         // chunk currently being claimed from
    size_t word;          // next bitmap word to look at in `chunk`
    Chunk* partial;       // other chunks of this class that have free slots
} SizeClass;

enum { THREAD_STARTING, THREAD_RUNNING };

static _Thread_local csrt_GcThread* tl_self;

#define SIG_SUSPEND SIGPWR
#define SIG_RESUME  SIGXCPU
#define MAX_SEGMENTS 16

typedef struct {
    uint8_t* start;
    size_t size;
    bool marked;
} LargeObject;

static struct {
    bool enabled, stats, initialized, collecting, multithreaded;
    pthread_mutex_t lock;  // allocation slow paths, collections, thread registry

    csrt_GcThread main_thread;
    csrt_GcThread* threads;           // registry (includes main_thread)
    atomic_int stop_epoch, world_stopped;
    sem_t ack;

    // Roots cached at init: dl_iterate_phdr takes the loader lock, which a stopped thread may hold.
    struct { uintptr_t lo, hi; } segments[MAX_SEGMENTS];
    size_t segment_count;
    size_t tls_size, tls_align;

    // Large blocks freed by a sweep, released after the world restarts (malloc locks!).
    void** deferred_free;
    size_t deferred_count, deferred_capacity;

    Chunk* chunks;
    Chunk* empty;         // chunks with no live objects, reusable by any size class
    SizeClass classes[NUM_CLASSES];

    // Open-addressing hash set of chunk base addresses (chunks are never unmapped, so no deletes).
    uintptr_t* chunk_set;
    size_t chunk_set_capacity, chunk_count;

    LargeObject* large;
    size_t large_count, large_capacity;

    uintptr_t heap_min, heap_max;  // quick range filter over everything we own

    void* stack_bottom;

    // mark stack: object start + size pairs
    struct { uint8_t* start; size_t size; }* mark_stack;
    size_t mark_count, mark_capacity;

    size_t allocated_since_gc, live_bytes, trigger, min_trigger;
    size_t collections, peak_heap, total_allocated;
    double total_gc_seconds;
} g;

/// Size -> class index. Written as comparisons (not a table) so it constant-folds when the
/// allocation size is a compile-time constant, which it is after LTO inlines csrt_alloc.
static inline uint32_t size_class_of(size_t size) {
    if (size <= 96) return size <= 16 ? 0 : (uint32_t)((size + 15) / 16) - 1;
    if (size <= 128) return 6;
    if (size <= 160) return 7;
    if (size <= 192) return 8;
    if (size <= 256) return 9;
    if (size <= 320) return 10;
    if (size <= 384) return 11;
    if (size <= 512) return 12;
    if (size <= 768) return 13;
    if (size <= 1024) return 14;
    if (size <= 1536) return 15;
    return 16;
}

// ---------------------------------------------------------------------------------------------
// Bitmaps / chunk lookup
// ---------------------------------------------------------------------------------------------
static inline bool bit_get(const uint64_t* bits, size_t i) { return (bits[i >> 6] >> (i & 63)) & 1; }
static inline void bit_set(uint64_t* bits, size_t i) { bits[i >> 6] |= (uint64_t)1 << (i & 63); }

static inline size_t hash_ptr(uintptr_t p) { return (size_t)((p >> 18) * 0x9E3779B97F4A7C15ull); }

static void chunk_set_insert_raw(uintptr_t* table, size_t capacity, uintptr_t base) {
    size_t i = hash_ptr(base) & (capacity - 1);
    while (table[i]) i = (i + 1) & (capacity - 1);
    table[i] = base;
}

static void chunk_set_insert(uintptr_t base) {
    if ((g.chunk_count + 1) * 2 > g.chunk_set_capacity) {
        size_t capacity = g.chunk_set_capacity ? g.chunk_set_capacity * 2 : 64;
        uintptr_t* table = calloc(capacity, sizeof *table);
        if (!table) { fputs("csrt: out of memory (gc metadata)\n", stderr); abort(); }
        for (size_t i = 0; i < g.chunk_set_capacity; i++)
            if (g.chunk_set[i]) chunk_set_insert_raw(table, capacity, g.chunk_set[i]);
        free(g.chunk_set);
        g.chunk_set = table;
        g.chunk_set_capacity = capacity;
    }
    chunk_set_insert_raw(g.chunk_set, g.chunk_set_capacity, base);
    g.chunk_count++;
}

static inline bool chunk_set_contains(uintptr_t base) {
    if (!g.chunk_set_capacity) return false;
    size_t i = hash_ptr(base) & (g.chunk_set_capacity - 1);
    for (;;) {
        uintptr_t v = g.chunk_set[i];
        if (v == base) return true;
        if (!v) return false;
        i = (i + 1) & (g.chunk_set_capacity - 1);
    }
}

static void note_range(uintptr_t lo, uintptr_t hi) {
    if (!g.heap_min || lo < g.heap_min) g.heap_min = lo;
    if (hi > g.heap_max) g.heap_max = hi;
}

// ---------------------------------------------------------------------------------------------
// Chunks
// ---------------------------------------------------------------------------------------------
static Chunk* chunk_map(void) {
    // Over-allocate and trim so the chunk is CHUNK_SIZE-aligned.
    size_t span = CHUNK_SIZE * 2;
    uint8_t* raw = mmap(NULL, span, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (raw == MAP_FAILED) return NULL;
    uintptr_t aligned = ((uintptr_t)raw + CHUNK_SIZE - 1) & ~(CHUNK_SIZE - 1);
    size_t head = aligned - (uintptr_t)raw;
    if (head) munmap(raw, head);
    size_t tail = span - head - CHUNK_SIZE;
    if (tail) munmap((uint8_t*)aligned + CHUNK_SIZE, tail);
    Chunk* c = (Chunk*)aligned;
    c->next = g.chunks;
    g.chunks = c;
    chunk_set_insert(aligned);
    note_range(aligned, aligned + CHUNK_SIZE);
    return c;
}

/// Gives an empty (or fresh) chunk a size class. Slots are zeroed at allocation time.
static void chunk_format(Chunk* c, uint32_t cls) {
    uint32_t size = k_size_classes[cls];
    c->object_size = size;
    c->size_class = cls;
    c->slots = (uint32_t)((CHUNK_SIZE - CHUNK_HEADER) / size);
    c->live = 0;
    memset(c->allocated, 0, sizeof c->allocated);
    memset(c->marked, 0, sizeof c->marked);
}

static inline uint64_t valid_mask(const Chunk* c, size_t w) {
    size_t last = c->slots / 64;
    if (w < last) return ~(uint64_t)0;
    return w == last ? (((uint64_t)1 << (c->slots % 64)) - 1) : 0;
}

/// Claims the next bitmap word with free slots for `sc`, moving to other chunks as needed.
static bool claim_word(SizeClass* sc, AllocCache* cache, uint32_t cls) {
    for (;;) {
        Chunk* c = sc->chunk;
        if (!c) {
            if (sc->partial) {
                c = sc->partial;
                sc->partial = c->next_free;
            } else if (g.empty) {
                c = g.empty;
                g.empty = c->next_free;
                chunk_format(c, cls);
            } else {
                c = chunk_map();
                if (!c) return false;
                chunk_format(c, cls);
            }
            sc->chunk = c;
            sc->word = 0;
        }
        size_t words = (c->slots + 63) / 64;
        for (size_t w = sc->word; w < words; w++) {
            uint64_t free_bits = ~c->allocated[w] & valid_mask(c, w);
            if (!free_bits) continue;
            c->allocated[w] |= free_bits;  // claim the whole word for this thread's cache
            cache->free_bits = free_bits;
            cache->word_base = (uint8_t*)c + CHUNK_HEADER + w * 64 * (size_t)c->object_size;
            sc->word = w + 1;
            size_t bytes = (size_t)__builtin_popcountll(free_bits) * c->object_size;
            g.allocated_since_gc += bytes;
            g.total_allocated += bytes;
            return true;
        }
        sc->chunk = NULL;
    }
}

/// Chunk + bitmap word index of a cache's claimed word.
static inline Chunk* cache_word(const AllocCache* cache, size_t* word) {
    Chunk* c = (Chunk*)((uintptr_t)cache->word_base & ~(CHUNK_SIZE - 1));
    *word = (size_t)(cache->word_base - (uint8_t*)c - CHUNK_HEADER) / c->object_size / 64;
    return c;
}

/// Hands a thread's unused claimed slots back to their chunks (thread exit). Caller holds g.lock.
static void release_cache(AllocCache* cache) {
    for (size_t cls = 0; cls < NUM_CLASSES; cls++) {
        AllocCache* ac = &cache[cls];
        if (ac->word_base && ac->free_bits) {
            size_t w;
            Chunk* c = cache_word(ac, &w);
            c->allocated[w] &= ~ac->free_bits;
        }
        ac->free_bits = 0;
        ac->word_base = NULL;
    }
}

/// After marking: keep every slot of each thread's current word allocated. A stopped thread may be
/// halfway through the (non-atomic) fast path, holding a slot of that word only in a register, so
/// those slots must survive this sweep. Dead objects there are reclaimed by a later collection.
static void protect_caches(void) {
    for (csrt_GcThread* t = g.threads; t; t = t->next) {
        if (t->state != THREAD_RUNNING || !t->cache) continue;
        for (size_t cls = 0; cls < NUM_CLASSES; cls++) {
            AllocCache* ac = &((AllocCache*)t->cache)[cls];
            if (!ac->word_base) continue;
            size_t w;
            Chunk* c = cache_word(ac, &w);
            c->marked[w] |= c->allocated[w];
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Allocation
// ---------------------------------------------------------------------------------------------
static void collect(void);

static int cache_segments(struct dl_phdr_info* info, size_t size, void* data) {
    (void)size; (void)data;
    // The first entry is the main executable; static fields live in its writable PT_LOAD segments.
    for (int i = 0; i < info->dlpi_phnum; i++) {
        const ElfW(Phdr)* ph = &info->dlpi_phdr[i];
        if (ph->p_type == PT_TLS) { g.tls_size = ph->p_memsz; g.tls_align = ph->p_align ? ph->p_align : 1; }
        if (ph->p_type != PT_LOAD || !(ph->p_flags & PF_W) || g.segment_count == MAX_SEGMENTS) continue;
        uintptr_t start = (uintptr_t)(info->dlpi_addr + ph->p_vaddr);
        g.segments[g.segment_count].lo = start;
        g.segments[g.segment_count].hi = start + ph->p_memsz;
        g.segment_count++;
    }
    return 1;  // stop after the main executable
}

static int tls_of_main(struct dl_phdr_info* info, size_t size, void* data) {
    (void)size;
    *(void**)data = info->dlpi_tls_data;  // the calling thread's copy of the main executable's TLS
    return 1;                             // first entry = main executable
}

/// The calling thread's static TLS block for the main executable ([ThreadStatic] fields, csrt TLS).
/// Called at registration, never while the world is stopped (dl_iterate_phdr takes the loader lock).
static void tls_bounds(csrt_GcThread* t) {
    if (!g.tls_size) return;
    void* block = NULL;
    dl_iterate_phdr(tls_of_main, &block);
    if (!block) return;
    t->tls_lo = (uintptr_t)block;
    t->tls_hi = (uintptr_t)block + g.tls_size;
}

void csrt_gc_init(void) {
    if (g.initialized) return;
    g.initialized = true;
    const char* env = getenv("CSRT_GC");
    g.enabled = !(env && env[0] == '0');
    env = getenv("CSRT_GC_STATS");
    g.stats = env && env[0] == '1';
    env = getenv("CSRT_GC_TRIGGER_MB");
    g.min_trigger = env && atol(env) > 0 ? (size_t)atol(env) * 1024 * 1024 : MIN_TRIGGER;
    g.trigger = g.min_trigger;
#ifdef __GLIBC__
    extern void* __libc_stack_end;
    g.stack_bottom = __libc_stack_end;
#else
    g.stack_bottom = __builtin_frame_address(0);
#endif
    pthread_mutex_init(&g.lock, NULL);
    sem_init(&g.ack, 0, 0);
    dl_iterate_phdr(cache_segments, NULL);
    g.main_thread.thread = pthread_self();
    g.main_thread.stack_bottom = g.stack_bottom;
    g.main_thread.cache = tl_cache;
    tls_bounds(&g.main_thread);
    g.main_thread.state = THREAD_RUNNING;
    g.threads = &g.main_thread;
    tl_self = &g.main_thread;
}

#define CALLOC_THRESHOLD ((size_t)128 * 1024)

static void* alloc_large_locked(size_t size, bool clear);

static void* alloc_large(size_t size, bool clear) {
    size = (size + 15) & ~(size_t)15;
    void* p = NULL;
    if (size >= CALLOC_THRESHOLD) {
        // glibc serves these with fresh mmap'd pages: zeroed for free, no memset touching every page.
        p = calloc(1, size);
        if (!p) return NULL;
    } else {
        if (posix_memalign(&p, 16, size) != 0) return NULL;
        if (clear) memset(p, 0, size);
    }
    if (g.large_count == g.large_capacity) {
        size_t capacity = g.large_capacity ? g.large_capacity * 2 : 64;
        LargeObject* grown = realloc(g.large, capacity * sizeof *grown);
        if (!grown) { free(p); return NULL; }
        g.large = grown;
        g.large_capacity = capacity;
    }
    g.large[g.large_count++] = (LargeObject){ p, size, false };
    note_range((uintptr_t)p, (uintptr_t)p + size);
    return p;
}

__attribute__((noinline, cold)) static void* alloc_small_slow(uint32_t cls) {
    if (!g.initialized) csrt_gc_init();
    pthread_mutex_lock(&g.lock);
    SizeClass* sc = &g.classes[cls];
    AllocCache* cache = &tl_cache[cls];
    bool collected = false;
    void* result = NULL;
    for (;;) {
        if (g.enabled && g.allocated_since_gc >= g.trigger && !g.collecting) { collect(); collected = true; }
        if (cache->free_bits || claim_word(sc, cache, cls)) break;
        if (!g.enabled || collected) goto out;
        collect();
        collected = true;
    }
    size_t heap = g.live_bytes + g.allocated_since_gc;
    if (heap > g.peak_heap) g.peak_heap = heap;
    uint64_t bits = cache->free_bits;
    cache->free_bits = bits & (bits - 1);
    result = cache->word_base + (size_t)__builtin_ctzll(bits) * k_size_classes[cls];
out:
    pthread_mutex_unlock(&g.lock);
    return result;
}

__attribute__((noinline)) static void* alloc_large_slow(size_t size, bool clear) {
    if (!g.initialized) csrt_gc_init();
    pthread_mutex_lock(&g.lock);
    void* result = alloc_large_locked(size, clear);
    pthread_mutex_unlock(&g.lock);
    return result;
}

static void* alloc_large_locked(size_t size, bool clear) {
    if (g.enabled && g.allocated_since_gc >= g.trigger) collect();
    void* result = alloc_large(size, clear);
    if (!result && g.enabled) { collect(); result = alloc_large(size, clear); }
    if (result) {
        size_t charged = (size + 15) & ~(size_t)15;
        g.allocated_since_gc += charged;
        g.total_allocated += charged;
        size_t heap = g.live_bytes + g.allocated_since_gc;
        if (heap > g.peak_heap) g.peak_heap = heap;
    }
    return result;
}

static inline void* gc_alloc(size_t size, bool clear) {
    if (__builtin_expect(size > LARGE_LIMIT, 0)) return alloc_large_slow(size, clear);
    uint32_t cls = size_class_of(size);
    AllocCache* cache = &tl_cache[cls];
    uint64_t bits = cache->free_bits;
    void* slot;
    if (__builtin_expect(bits != 0, 1)) {
        cache->free_bits = bits & (bits - 1);
        slot = cache->word_base + (size_t)__builtin_ctzll(bits) * k_size_classes[cls];
    } else {
        slot = alloc_small_slow(cls);
        if (!slot) return NULL;
    }
    if (clear) memset(slot, 0, k_size_classes[cls]);  // C# semantics: fresh objects are zeroed
    return slot;
}

void* csrt_gc_alloc(size_t size) { return gc_alloc(size, true); }

void* csrt_gc_alloc_noclear(size_t size) { return gc_alloc(size, false); }

int64_t csrt_gc_total_memory(void) {
    pthread_mutex_lock(&g.lock);
    int64_t total = (int64_t)(g.live_bytes + g.allocated_since_gc);
    pthread_mutex_unlock(&g.lock);
    return total;
}

// ---------------------------------------------------------------------------------------------
// Marking
// ---------------------------------------------------------------------------------------------
static void* mmap_grow(void* old, size_t old_bytes, size_t new_bytes) {
    void* grown = mmap(NULL, new_bytes, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (grown == MAP_FAILED) { static const char m[] = "csrt: out of memory (gc metadata)\n"; (void)!write(2, m, sizeof m - 1); abort(); }
    if (old) { memcpy(grown, old, old_bytes); munmap(old, old_bytes); }
    return grown;
}

static void mark_push(uint8_t* start, size_t size) {
    if (g.mark_count == g.mark_capacity) {
        // mmap, not realloc: this runs with the world stopped.
        size_t capacity = g.mark_capacity ? g.mark_capacity * 2 : 4096;
        void* grown = mmap_grow(g.mark_stack, g.mark_capacity * sizeof *g.mark_stack, capacity * sizeof *g.mark_stack);
        g.mark_stack = grown;
        g.mark_capacity = capacity;
    }
    g.mark_stack[g.mark_count].start = start;
    g.mark_stack[g.mark_count].size = size;
    g.mark_count++;
}

/// Heapsort by start address (in place, no allocation: glibc's qsort may malloc).
static void sort_large(LargeObject* a, size_t n) {
    if (n < 2) return;
    for (size_t start = n / 2; start-- > 0;)
        for (size_t root = start, child; (child = 2 * root + 1) < n; root = child) {
            if (child + 1 < n && a[child].start < a[child + 1].start) child++;
            if (a[root].start >= a[child].start) break;
            LargeObject t = a[root]; a[root] = a[child]; a[child] = t;
        }
    for (size_t end = n - 1; end > 0; end--) {
        LargeObject t = a[0]; a[0] = a[end]; a[end] = t;
        for (size_t root = 0, child; (child = 2 * root + 1) < end; root = child) {
            if (child + 1 < end && a[child].start < a[child + 1].start) child++;
            if (a[root].start >= a[child].start) break;
            t = a[root]; a[root] = a[child]; a[child] = t;
        }
    }
}

/// If `word` points into a live, unmarked heap object, marks it and queues it for scanning.
static inline void mark_candidate(uintptr_t word) {
    if (word < g.heap_min || word >= g.heap_max) return;

    uintptr_t base = word & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains(base)) {
        Chunk* c = (Chunk*)base;
        size_t offset = word - base;
        if (c->object_size == 0 || offset < CHUNK_HEADER) return;
        size_t index = (offset - CHUNK_HEADER) / c->object_size;
        if (index >= c->slots || !bit_get(c->allocated, index) || bit_get(c->marked, index)) return;
        bit_set(c->marked, index);
        mark_push((uint8_t*)base + CHUNK_HEADER + index * c->object_size, c->object_size);
        return;
    }

    // Large objects: binary search the (sorted) table; interior pointers count.
    size_t lo = 0, hi = g.large_count;
    while (lo < hi) {
        size_t mid = (lo + hi) / 2;
        LargeObject* obj = &g.large[mid];
        if (word < (uintptr_t)obj->start) hi = mid;
        else if (word >= (uintptr_t)obj->start + obj->size) lo = mid + 1;
        else {
            if (!obj->marked) {
                obj->marked = true;
                mark_push(obj->start, obj->size);
            }
            return;
        }
    }
}

static void scan_range(const void* from, const void* to) {
    uintptr_t lo = ((uintptr_t)from + sizeof(void*) - 1) & ~(uintptr_t)(sizeof(void*) - 1);
    for (const uintptr_t* p = (const uintptr_t*)lo; (const void*)(p + 1) <= to; p++) mark_candidate(*p);
}

static void drain(void) {
    while (g.mark_count) {
        g.mark_count--;
        uint8_t* start = g.mark_stack[g.mark_count].start;
        size_t size = g.mark_stack[g.mark_count].size;
        // Word 0 is the method-table pointer (static data, never a heap reference); strings and
        // arrays of primitives hold no references, so they have nothing to scan at all.
        const void* mt = *(const void* const*)start;
        if (mt == (const void*)&csrt_String_MT.header || mt == (const void*)&csrt_PrimitiveArray_MT.header) continue;
        scan_range(start + sizeof(void*), start + size);
    }
}

/// noinline so the caller's frame (and every frame above it) lies inside the scanned range.
__attribute__((noinline)) static void scan_own_stack(const void* bottom) {
    jmp_buf registers;  // spills callee-saved registers onto this frame
    setjmp(registers);
    __builtin_unwind_init();
    volatile uintptr_t marker = 0;
    const void* top = (const void*)&marker;
    if ((const void*)registers < top) top = (const void*)registers;
    scan_range(top, bottom);
}

static void scan_roots(void) {
    for (size_t i = 0; i < g.segment_count; i++) scan_range((const void*)g.segments[i].lo, (const void*)g.segments[i].hi);
    for (csrt_GcThread* t = g.threads; t; t = t->next) {
        mark_candidate((uintptr_t)t->managed);  // e.g. the Thread object of a starting thread
        if (t->state != THREAD_RUNNING) continue;
        if (t->tls_hi) scan_range((const void*)t->tls_lo, (const void*)t->tls_hi);
        if (t == tl_self) scan_own_stack(t->stack_bottom);
        else scan_range((const void*)t->stack_top, t->stack_bottom);
    }
}

// ---------------------------------------------------------------------------------------------
// Stop the world (signal handshake)
// ---------------------------------------------------------------------------------------------
static void suspend_handler(int sig) {
    (void)sig;
    int saved_errno = errno;
    csrt_GcThread* me = tl_self;
    int epoch = atomic_load(&g.stop_epoch);
    jmp_buf registers;
    setjmp(registers);       // callee-saved registers -> this frame; the kernel's signal frame above
    __builtin_unwind_init(); // holds the rest of the interrupted register state
    volatile uintptr_t marker = 0;
    const void* top = (const void*)&marker;
    if ((const void*)registers < top) top = (const void*)registers;
    if (me) me->stack_top = (void*)top;
    sem_post(&g.ack);
    sigset_t mask;
    sigfillset(&mask);
    sigdelset(&mask, SIG_RESUME);
    while (atomic_load(&g.world_stopped) && atomic_load(&g.stop_epoch) == epoch) sigsuspend(&mask);
    errno = saved_errno;
}

static void resume_handler(int sig) { (void)sig; }

static void install_signal_handlers(void) {
    struct sigaction sa;
    memset(&sa, 0, sizeof sa);
    sa.sa_flags = SA_RESTART;
    sigfillset(&sa.sa_mask);
    sa.sa_handler = suspend_handler;
    sigaction(SIG_SUSPEND, &sa, NULL);
    sa.sa_handler = resume_handler;
    sigaction(SIG_RESUME, &sa, NULL);
}

static int stop_world(void) {
    if (!g.multithreaded) return 0;
    atomic_fetch_add(&g.stop_epoch, 1);
    atomic_store(&g.world_stopped, 1);
    int signalled = 0;
    for (csrt_GcThread* t = g.threads; t; t = t->next)
        if (t->state == THREAD_RUNNING && t != tl_self && pthread_kill(t->thread, SIG_SUSPEND) == 0) signalled++;
    for (int i = 0; i < signalled; i++)
        while (sem_wait(&g.ack) != 0 && errno == EINTR) {}
    return signalled;
}

static void resume_world(void) {
    if (!g.multithreaded) return;
    atomic_store(&g.world_stopped, 0);
    for (csrt_GcThread* t = g.threads; t; t = t->next)
        if (t->state == THREAD_RUNNING && t != tl_self) pthread_kill(t->thread, SIG_RESUME);
}

// ---------------------------------------------------------------------------------------------
// Thread registry
// ---------------------------------------------------------------------------------------------
csrt_GcThread* csrt_gc_thread_prepare(void* managed) {
    if (!g.initialized) csrt_gc_init();
    csrt_GcThread* t = calloc(1, sizeof *t);
    if (!t) return NULL;
    t->managed = managed;
    t->state = THREAD_STARTING;
    pthread_mutex_lock(&g.lock);
    if (!g.multithreaded) { install_signal_handlers(); g.multithreaded = true; }
    t->next = g.threads;
    g.threads = t;
    pthread_mutex_unlock(&g.lock);
    return t;
}

static void unlink_thread(csrt_GcThread* t) {
    for (csrt_GcThread** p = &g.threads; *p; p = &(*p)->next)
        if (*p == t) { *p = t->next; break; }
}

void csrt_gc_thread_attach(csrt_GcThread* t) {
    pthread_attr_t attr;
    void* stack_addr = NULL;
    size_t stack_size = 0;
    if (pthread_getattr_np(pthread_self(), &attr) == 0) {
        pthread_attr_getstack(&attr, &stack_addr, &stack_size);
        pthread_attr_destroy(&attr);
    }
    pthread_mutex_lock(&g.lock);  // never attach in the middle of a collection
    t->thread = pthread_self();
    t->stack_bottom = stack_addr ? (uint8_t*)stack_addr + stack_size : __builtin_frame_address(0);
    t->cache = tl_cache;
    tls_bounds(t);
    tl_self = t;
    t->state = THREAD_RUNNING;
    pthread_mutex_unlock(&g.lock);
}

void csrt_gc_thread_detach(void) {
    csrt_GcThread* t = tl_self;
    if (!t || t == &g.main_thread) return;
    pthread_mutex_lock(&g.lock);
    release_cache(tl_cache);
    unlink_thread(t);
    tl_self = NULL;
    pthread_mutex_unlock(&g.lock);
    free(t);
}

void csrt_gc_thread_abandon(csrt_GcThread* t) {
    pthread_mutex_lock(&g.lock);
    unlink_thread(t);
    pthread_mutex_unlock(&g.lock);
    free(t);
}

bool csrt_gc_is_multithreaded(void) { return g.multithreaded; }

// ---------------------------------------------------------------------------------------------
// Sweeping
// ---------------------------------------------------------------------------------------------
static size_t sweep(void) {
    size_t live = 0;
    for (size_t cls = 0; cls < NUM_CLASSES; cls++) {
        g.classes[cls].chunk = NULL;
        g.classes[cls].partial = NULL;
    }
    g.empty = NULL;

    for (Chunk* c = g.chunks; c; c = c->next) {
        if (c->object_size == 0) { c->next_free = g.empty; g.empty = c; continue; }
        uint32_t still_live = 0;
        size_t words = (c->slots + 63) / 64;
        for (size_t w = 0; w < words; w++) {
            c->allocated[w] &= c->marked[w];
            still_live += (uint32_t)__builtin_popcountll(c->allocated[w]);
            c->marked[w] = 0;
        }
        c->live = still_live;
        if (still_live == 0) {
            c->object_size = 0;  // reusable by any size class (pages stay resident: refaulting is costly)
            c->next_free = g.empty;
            g.empty = c;
            continue;
        }
        if (still_live < c->slots) {
            SizeClass* sc = &g.classes[c->size_class];
            c->next_free = sc->partial;
            sc->partial = c;
        }
        live += (size_t)still_live * c->object_size;
    }

    size_t kept = 0;
    for (size_t i = 0; i < g.large_count; i++) {
        LargeObject obj = g.large[i];
        if (obj.marked) {
            obj.marked = false;
            g.large[kept++] = obj;
            live += obj.size;
        } else {
            if (g.deferred_count == g.deferred_capacity) {
                size_t capacity = g.deferred_capacity ? g.deferred_capacity * 2 : 1024;
                g.deferred_free = mmap_grow(g.deferred_free, g.deferred_capacity * sizeof(void*), capacity * sizeof(void*));
                g.deferred_capacity = capacity;
            }
            g.deferred_free[g.deferred_count++] = obj.start;
        }
    }
    g.large_count = kept;
    return live;
}

static void collect(void) {
    if (g.collecting) return;
    g.collecting = true;
    struct timespec t0, t1;
    if (g.stats) clock_gettime(CLOCK_MONOTONIC, &t0);

    stop_world();
    sort_large(g.large, g.large_count);
    scan_roots();
    drain();
    protect_caches();
    g.live_bytes = sweep();
    resume_world();
    for (size_t i = 0; i < g.deferred_count; i++) free(g.deferred_free[i]);
    g.deferred_count = 0;
    g.allocated_since_gc = 0;
    g.trigger = 2 * g.live_bytes > g.min_trigger ? 2 * g.live_bytes : g.min_trigger;
    g.collections++;

    if (g.stats) {
        clock_gettime(CLOCK_MONOTONIC, &t1);
        g.total_gc_seconds += (double)(t1.tv_sec - t0.tv_sec) + (double)(t1.tv_nsec - t0.tv_nsec) / 1e9;
    }
    g.collecting = false;
}

void csrt_gc_collect(void) {
    if (!g.initialized) csrt_gc_init();
    if (!g.enabled) return;
    pthread_mutex_lock(&g.lock);
    collect();
    pthread_mutex_unlock(&g.lock);
}

void csrt_gc_shutdown(void) {
    if (!g.stats) return;
    fprintf(stderr,
            "csrt gc: %zu collections, %.1f ms total, allocated %.1f MiB, peak heap %.1f MiB, live %.1f MiB, %zu chunks, %zu large objects\n",
            g.collections, g.total_gc_seconds * 1000.0, g.total_allocated / 1048576.0, g.peak_heap / 1048576.0,
            (g.live_bytes + g.allocated_since_gc) / 1048576.0, g.chunk_count, g.large_count);
}
