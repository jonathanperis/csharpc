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
// Limitations: single-threaded (no thread registration or safepoints); conservative scanning can
// retain garbage that happens to look like a pointer; no finalizers; empty chunks are kept
// (resident) for reuse by any size class.
//
// Environment: CSRT_GC=0 disables collection; CSRT_GC_STATS=1 prints statistics at exit.
#define _GNU_SOURCE
#include "gc.h"
#include "csrt.h"

#include <link.h>
#include <setjmp.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>

#define CHUNK_SIZE   ((size_t)256 * 1024)
#define CHUNK_HEADER ((size_t)4096)
#define MAX_SLOTS    ((CHUNK_SIZE - CHUNK_HEADER) / 16)
#define LARGE_LIMIT  ((size_t)2048)
#define MIN_TRIGGER  ((size_t)32 * 1024 * 1024)

static const uint32_t k_size_classes[] = {16, 32, 48, 64, 80, 96, 128, 160, 192, 256, 320, 384, 512, 768, 1024, 1536, 2048};
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

typedef struct {
    uint64_t free_bits;   // unallocated slots of the claimed word: bit i -> word_base + i * size
    uint8_t* word_base;
    Chunk* chunk;         // chunk currently being allocated from
    size_t word;          // next bitmap word to look at in `chunk`
    Chunk* partial;       // other chunks of this class that have free slots
} SizeClass;

typedef struct {
    uint8_t* start;
    size_t size;
    bool marked;
} LargeObject;

static struct {
    bool enabled, stats, initialized, collecting;
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
static bool claim_word(SizeClass* sc, uint32_t cls) {
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
            c->allocated[w] |= free_bits;  // claim the whole word; unused slots are returned before marking
            sc->free_bits = free_bits;
            sc->word_base = (uint8_t*)c + CHUNK_HEADER + w * 64 * (size_t)c->object_size;
            sc->word = w + 1;
            size_t bytes = (size_t)__builtin_popcountll(free_bits) * c->object_size;
            g.allocated_since_gc += bytes;
            g.total_allocated += bytes;
            return true;
        }
        sc->chunk = NULL;
    }
}

/// Returns the unallocated part of every claimed word to its chunk (before marking).
static void release_claims(void) {
    for (size_t cls = 0; cls < NUM_CLASSES; cls++) {
        SizeClass* sc = &g.classes[cls];
        if (sc->free_bits && sc->chunk) {
            sc->chunk->allocated[sc->word - 1] &= ~sc->free_bits;
            size_t unused = (size_t)__builtin_popcountll(sc->free_bits) * sc->chunk->object_size;
            g.allocated_since_gc -= unused;
            g.total_allocated -= unused;
        }
        sc->free_bits = 0;
    }
}

// ---------------------------------------------------------------------------------------------
// Allocation
// ---------------------------------------------------------------------------------------------
static void collect(void);

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
}

static void* alloc_large(size_t size) {
    size = (size + 15) & ~(size_t)15;
    void* p = NULL;
    if (posix_memalign(&p, 16, size) != 0) return NULL;
    memset(p, 0, size);
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
    SizeClass* sc = &g.classes[cls];
    bool collected = false;
    for (;;) {
        if (g.enabled && g.allocated_since_gc >= g.trigger && !g.collecting) { collect(); collected = true; }
        if (claim_word(sc, cls)) break;
        if (!g.enabled || collected) return NULL;
        collect();
        collected = true;
    }
    size_t heap = g.live_bytes + g.allocated_since_gc;
    if (heap > g.peak_heap) g.peak_heap = heap;
    uint64_t bits = sc->free_bits;
    sc->free_bits = bits & (bits - 1);
    return sc->word_base + (size_t)__builtin_ctzll(bits) * k_size_classes[cls];
}

__attribute__((noinline)) static void* alloc_large_slow(size_t size) {
    if (!g.initialized) csrt_gc_init();
    if (g.enabled && g.allocated_since_gc >= g.trigger) collect();
    void* result = alloc_large(size);
    if (!result && g.enabled) { collect(); result = alloc_large(size); }
    if (result) {
        size_t charged = (size + 15) & ~(size_t)15;
        g.allocated_since_gc += charged;
        g.total_allocated += charged;
        size_t heap = g.live_bytes + g.allocated_since_gc;
        if (heap > g.peak_heap) g.peak_heap = heap;
    }
    return result;
}

void* csrt_gc_alloc(size_t size) {
    if (__builtin_expect(size > LARGE_LIMIT, 0)) return alloc_large_slow(size);
    uint32_t cls = size_class_of(size);
    SizeClass* sc = &g.classes[cls];
    uint64_t bits = sc->free_bits;
    void* slot;
    if (__builtin_expect(bits != 0, 1)) {
        sc->free_bits = bits & (bits - 1);
        slot = sc->word_base + (size_t)__builtin_ctzll(bits) * k_size_classes[cls];
    } else {
        slot = alloc_small_slow(cls);
        if (!slot) return NULL;
    }
    memset(slot, 0, k_size_classes[cls]);  // C# semantics: fresh objects are zeroed
    return slot;
}

int64_t csrt_gc_total_memory(void) { return (int64_t)(g.live_bytes + g.allocated_since_gc); }

// ---------------------------------------------------------------------------------------------
// Marking
// ---------------------------------------------------------------------------------------------
static void mark_push(uint8_t* start, size_t size) {
    if (g.mark_count == g.mark_capacity) {
        size_t capacity = g.mark_capacity ? g.mark_capacity * 2 : 4096;
        void* grown = realloc(g.mark_stack, capacity * sizeof *g.mark_stack);
        if (!grown) { fputs("csrt: out of memory (gc mark stack)\n", stderr); abort(); }
        g.mark_stack = grown;
        g.mark_capacity = capacity;
    }
    g.mark_stack[g.mark_count].start = start;
    g.mark_stack[g.mark_count].size = size;
    g.mark_count++;
}

static int compare_large(const void* a, const void* b) {
    uintptr_t x = (uintptr_t)((const LargeObject*)a)->start, y = (uintptr_t)((const LargeObject*)b)->start;
    return x < y ? -1 : x > y;
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
        // Word 0 is the method-table pointer (static data, never a heap reference); strings hold
        // only UTF-16 data, so they have nothing to scan at all.
        if (*(const void* const*)start == (const void*)&csrt_String_MT.header) continue;
        scan_range(start + sizeof(void*), start + size);
    }
}

static int scan_segments(struct dl_phdr_info* info, size_t size, void* data) {
    (void)size; (void)data;
    // The first entry is the main executable; static fields live in its writable PT_LOAD segments.
    for (int i = 0; i < info->dlpi_phnum; i++) {
        const ElfW(Phdr)* ph = &info->dlpi_phdr[i];
        if (ph->p_type != PT_LOAD || !(ph->p_flags & PF_W)) continue;
        const uint8_t* start = (const uint8_t*)(info->dlpi_addr + ph->p_vaddr);
        scan_range(start, start + ph->p_memsz);
    }
    return 1;  // stop after the main executable
}

/// noinline so the caller's frame (and every frame above it) lies inside the scanned range.
__attribute__((noinline)) static void scan_stack(void) {
    jmp_buf registers;  // spills callee-saved registers onto this frame
    setjmp(registers);
    __builtin_unwind_init();
    volatile uintptr_t marker = 0;
    const void* top = (const void*)&marker;
    if ((const void*)registers < top) top = (const void*)registers;
    scan_range(top, g.stack_bottom);
}

// ---------------------------------------------------------------------------------------------
// Sweeping
// ---------------------------------------------------------------------------------------------
static size_t sweep(void) {
    size_t live = 0;
    for (size_t cls = 0; cls < NUM_CLASSES; cls++) {
        g.classes[cls].chunk = NULL;
        g.classes[cls].partial = NULL;
        g.classes[cls].free_bits = 0;
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
            free(obj.start);
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

    release_claims();
    qsort(g.large, g.large_count, sizeof *g.large, compare_large);
    scan_stack();
    dl_iterate_phdr(scan_segments, NULL);
    drain();
    g.live_bytes = sweep();
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
    if (g.enabled) collect();
}

void csrt_gc_shutdown(void) {
    if (!g.stats) return;
    fprintf(stderr,
            "csrt gc: %zu collections, %.1f ms total, allocated %.1f MiB, peak heap %.1f MiB, live %.1f MiB, %zu chunks, %zu large objects\n",
            g.collections, g.total_gc_seconds * 1000.0, g.total_allocated / 1048576.0, g.peak_heap / 1048576.0,
            (g.live_bytes + g.allocated_since_gc) / 1048576.0, g.chunk_count, g.large_count);
}
