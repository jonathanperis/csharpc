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
// Threads (docs/13-threading.md): every managed thread registers itself (stack bounds, TLS block,
// allocation cache). Allocation caches are thread-local, so the fast path stays lock-free; slow
// paths take one global lock. A collection stops the world with a signal handshake (as Boehm GC
// does): each mutator spills its registers in a signal handler, records its stack pointer, acks and
// sleeps in sigsuspend until the collector resumes it. While the world is stopped the collector
// never calls malloc/free, stdio or the dynamic loader (a stopped thread may hold their locks).
//
// Limitations: conservative scanning can retain garbage that happens to look like a pointer; no
// finalizers; empty chunks are kept (resident) for reuse by any size class. Marking is serial.
// An optional parallel marker (one owner per chunk, plain mark-bit stores, cross-chunk pointers
// batched to the owner) is compiled in with -DCSRT_GC_PARALLEL_MARK; it was slower than one thread
// on a 4-core machine, and an atomic mark bit was slower still, so the default binary does not
// carry it. wasm32 stays serial either way. Slot indexing uses a per-class multiplicative inverse
// instead of a division.
//
// Environment: CSRT_GC=0 disables collection; CSRT_GC_STATS=1 prints statistics at exit;
// CSRT_GC_STRESS=N collects on every N-th allocation (every allocation takes the slow path), a test
// mode for root discovery. With the runtime built -DCSRT_GC_PARALLEL_MARK, CSRT_GC_PARALLEL=1
// marks with one worker per CPU (0 is serial; -1 marks in parallel only after a mark has taken
// at least 3 ms) and CSRT_GC_MARK_THREADS caps that count, including the collecting thread.
//
// Platforms: root discovery is per object format. Linux (ELF) finds the data segments and the
// static TLS block with dl_iterate_phdr; macOS (Mach-O) walks the main image's load commands and
// finds each thread's thread-local block through a TLV descriptor (see the __APPLE__ blocks).
// Windows (PE/COFF) has no signals to stop threads with: the collector suspends each one with
// SuspendThread and reads its registers with GetThreadContext instead (see the _WIN32 blocks).
// WASI (wasm32) has one thread and no registers in memory: values the program keeps in wasm locals
// are invisible to a scan of linear memory, so csharpc spills every reference that is live across
// a call that may collect into a stack slot (src/Csharpc/Wasm/RootSpilling.cs; docs/adr/0011-wasm32.md),
// and the collector scans the linear-memory stack and the data segment (see the __wasi__ blocks).
#define _GNU_SOURCE
#define _WIN32_WINNT 0x0A00  // Windows 10 (GetCurrentThreadStackLimits)
#include "gc.h"
#include "csrt.h"

#include <errno.h>
#include "csrt_pthread.h"
#if defined(_WIN32)
#include <windows.h>
#include <io.h>
#elif defined(__APPLE__)
#include <mach/mach.h>
#include <mach-o/dyld.h>
#include <mach-o/loader.h>
#elif defined(__wasi__)
#include <unistd.h>
#else
#include <link.h>
#include <semaphore.h>
#endif
#ifndef __wasi__
#include <setjmp.h>
#include <signal.h>
#endif
#include <stdatomic.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#if !defined(_WIN32) && !defined(__wasi__)
#include <sys/mman.h>
#include <unistd.h>
#endif
#include <time.h>
#define CHUNK_SIZE   ((size_t)256 * 1024)
#define CHUNK_HEADER ((size_t)4096)
#define MAX_SLOTS    ((CHUNK_SIZE - CHUNK_HEADER) / 16)
#define LARGE_LIMIT  ((size_t)2048)
#define MIN_TRIGGER  ((size_t)32 * 1024 * 1024)

static const uint32_t k_size_classes[17] = {16, 32, 48, 64, 80, 96, 128, 160, 192, 256, 320, 384, 512, 768, 1024, 1536, 2048};
// ceil(2^32 / size). (n * mul) >> 32 == n / size for every byte offset in a chunk, so the marker
// can replace the size-class division. Checked for every numerator below CHUNK_SIZE - CHUNK_HEADER.
static const uint32_t k_div_mul[17] = {
    268435456u, 134217728u, 89478486u, 67108864u, 53687092u, 44739243u, 33554432u, 26843546u,
    22369622u, 16777216u, 13421773u, 11184811u, 8388608u, 5592406u, 4194304u, 2796203u, 2097152u,
};
#define NUM_CLASSES (sizeof k_size_classes / sizeof k_size_classes[0])

typedef struct Chunk Chunk;
struct Chunk {
    uint32_t object_size;  // 0 = empty chunk, available for any size class
    uint32_t slots;
    uint32_t live;         // live slots after the last sweep
    uint32_t size_class;
    uint32_t div_mul;      // slot index = ((offset - CHUNK_HEADER) * div_mul) >> 32
#if defined(CSRT_GC_PARALLEL_MARK)
    uint8_t owner;         // which parallel marker writes this chunk's mark bits
#endif
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

// local-exec: csrt is always linked into the executable, so TLS is a direct %fs-relative access.
#define CSRT_TLS _Thread_local __attribute__((tls_model("local-exec")))
static CSRT_TLS AllocCache tl_cache[17];

/// Shared per-class state (guarded by g.lock).
typedef struct {
    Chunk* chunk;         // chunk currently being claimed from
    size_t word;          // next bitmap word to look at in `chunk`
    Chunk* partial;       // other chunks of this class that have free slots
} SizeClass;

enum { THREAD_STARTING, THREAD_RUNNING };

static CSRT_TLS csrt_GcThread* tl_self;

#if defined(__APPLE__)
#define SIG_SUSPEND SIGUSR1  // Darwin has no SIGPWR; .NET programs can't handle SIGUSR1 themselves
#define SIG_RESUME  SIGXCPU
#elif !defined(_WIN32) && !defined(__wasi__)
#define SIG_SUSPEND SIGPWR
#define SIG_RESUME  SIGXCPU
#endif
#define MAX_SEGMENTS 16

typedef struct { uint8_t* start; size_t size; } MarkItem;

typedef struct {
    uint8_t* start;
    size_t size;
    unsigned char marked;  // atomic byte while a collection marks in parallel
} LargeObject;

static struct {
    bool enabled, stats, initialized, collecting, multithreaded;
    pthread_mutex_t lock;  // allocation slow paths, collections, thread registry

    csrt_GcThread main_thread;
    csrt_GcThread* threads;           // registry (includes main_thread)
    atomic_int stop_epoch, world_stopped;
#if defined(__APPLE__)
    semaphore_t ack;  // macOS has no unnamed POSIX semaphores (sem_init fails with ENOSYS)
#elif !defined(_WIN32) && !defined(__wasi__)
    sem_t ack;
#endif

    // Roots cached at init: dl_iterate_phdr takes the loader lock, which a stopped thread may hold.
    struct { uintptr_t lo, hi; } segments[MAX_SEGMENTS];
    size_t segment_count;
    size_t tls_size, tls_align;
#ifdef __APPLE__
    struct TlvDescriptor* tlv;  // any thread-local variable of the executable (NULL: none)
    uintptr_t tlv_template;     // start of the TLV template (__thread_data / __thread_bss)
#endif

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
    size_t large_sorted;  // large[0, large_sorted) is sorted by address; later allocations are appended after it

    uintptr_t heap_min, heap_max;  // quick range filter over everything we own

    void* stack_bottom;

    // mark stack: object start + size pairs
    MarkItem* mark_stack;
    size_t mark_count, mark_capacity;

    size_t allocated_since_gc, live_bytes, trigger, min_trigger;
    size_t stress, stress_count;  // CSRT_GC_STRESS
    size_t collections, parallel_collections, peak_heap, total_allocated;
    double total_gc_seconds, mark_seconds, sweep_seconds;
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

/// Byte offset from the chunk base (past the header) to a slot. `div_mul` is ceil(2^32 / object_size).
static inline size_t slot_index(const Chunk* c, size_t offset) {
    return (size_t)(((uint64_t)(offset - CHUNK_HEADER) * c->div_mul) >> 32);
}

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
#ifdef _WIN32
    // VirtualFree can't trim part of a reservation: reserve twice the size to find an aligned
    // address, release it, and map exactly there (retrying if another thread took the range).
    uintptr_t aligned;
    for (;;) {
        uint8_t* raw = VirtualAlloc(NULL, CHUNK_SIZE * 2, MEM_RESERVE, PAGE_NOACCESS);
        if (!raw) return NULL;
        aligned = ((uintptr_t)raw + CHUNK_SIZE - 1) & ~(CHUNK_SIZE - 1);
        VirtualFree(raw, 0, MEM_RELEASE);
        if (VirtualAlloc((void*)aligned, CHUNK_SIZE, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE)) break;
    }
#elif defined(__wasi__)
    // Linear memory only grows: take aligned blocks from wasi-libc's allocator (memory.grow).
    uintptr_t aligned = (uintptr_t)aligned_alloc(CHUNK_SIZE, CHUNK_SIZE);
    if (!aligned) return NULL;
#else
    // Over-allocate and trim so the chunk is CHUNK_SIZE-aligned.
    size_t span = CHUNK_SIZE * 2;
    uint8_t* raw = mmap(NULL, span, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (raw == MAP_FAILED) return NULL;
    uintptr_t aligned = ((uintptr_t)raw + CHUNK_SIZE - 1) & ~(CHUNK_SIZE - 1);
    size_t head = aligned - (uintptr_t)raw;
    if (head) munmap(raw, head);
    size_t tail = span - head - CHUNK_SIZE;
    if (tail) munmap((uint8_t*)aligned + CHUNK_SIZE, tail);
#endif
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
    c->div_mul = k_div_mul[cls];
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
    *word = slot_index(c, (size_t)(cache->word_base - (uint8_t*)c)) / 64;
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
/// Named and never inlined: csharpc's wasm root spiller (RootSpilling.cs) finds the calls that may
/// collect by looking for the callers of this function.
__attribute__((noinline)) void csrt_gc_collect_now(void);
#define collect csrt_gc_collect_now

// The lock only matters once a second thread exists. g.multithreaded flips once, set by the only
// running thread (in csrt_gc_thread_prepare, before the new thread starts), and never resets.
static inline void gc_lock(void) { if (g.multithreaded) pthread_mutex_lock(&g.lock); }
static inline void gc_unlock(void) { if (g.multithreaded) pthread_mutex_unlock(&g.lock); }

#if defined(_WIN32)
/// The executable's PE image (GetModuleHandle(NULL)).
static const IMAGE_NT_HEADERS* image_headers(const uint8_t** base) {
    *base = (const uint8_t*)GetModuleHandleW(NULL);
    return (const IMAGE_NT_HEADERS*)(*base + ((const IMAGE_DOS_HEADER*)*base)->e_lfanew);
}

/// The executable's writable sections (.data, .bss: static fields) and the size of its TLS block.
static void cache_segments(void) {
    const uint8_t* base;
    const IMAGE_NT_HEADERS* nt = image_headers(&base);
    const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections && g.segment_count < MAX_SEGMENTS; i++, section++) {
        if (!(section->Characteristics & IMAGE_SCN_MEM_WRITE)) continue;
        g.segments[g.segment_count].lo = (uintptr_t)(base + section->VirtualAddress);
        g.segments[g.segment_count].hi = (uintptr_t)(base + section->VirtualAddress + section->Misc.VirtualSize);
        g.segment_count++;
    }
    const IMAGE_DATA_DIRECTORY* dir = &nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS];
    if (dir->VirtualAddress && dir->Size) {
        const IMAGE_TLS_DIRECTORY* tls = (const IMAGE_TLS_DIRECTORY*)(base + dir->VirtualAddress);
        g.tls_size = (size_t)(tls->EndAddressOfRawData - tls->StartAddressOfRawData) + tls->SizeOfZeroFill;
    }
}

/// The calling thread's copy of the executable's TLS block: TEB.ThreadLocalStoragePointer[_tls_index].
static void tls_bounds(csrt_GcThread* t) {
    extern ULONG _tls_index;  // the executable's TLS slot (mingw-w64 tlssup.c)
    if (!g.tls_size) return;
    void** blocks = (void**)__readgsqword(0x58);
    if (!blocks || !blocks[_tls_index]) return;
    t->tls_lo = (uintptr_t)blocks[_tls_index];
    t->tls_hi = t->tls_lo + g.tls_size;
}

/// Highest address of the calling thread's stack.
static void* own_stack_bottom(void) {
    ULONG_PTR low = 0, high = 0;
    GetCurrentThreadStackLimits(&low, &high);
    return (void*)high;
}

/// A handle that can suspend the calling thread and read its registers, plus a buffer for them.
static void thread_handles(csrt_GcThread* t) {
    HANDLE self;
    if (!DuplicateHandle(GetCurrentProcess(), GetCurrentThread(), GetCurrentProcess(), &self,
                         THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION, FALSE, 0)) {
        fputs("csrt: cannot register a thread with the garbage collector\n", stderr);
        abort();
    }
    t->handle = self;
    if (!t->context) t->context = _aligned_malloc(sizeof(CONTEXT), 16);
    if (!t->context) { fputs("csrt: out of memory (gc metadata)\n", stderr); abort(); }
}
#elif defined(__APPLE__)
// Mach-O thread-locals: each one has a TLV descriptor { thunk, key, offset } in a
// S_THREAD_LOCAL_VARIABLES section. dyld gives every thread one block holding all of the image's
// thread-locals, laid out like the template sections (__thread_data, __thread_bss); a descriptor's
// thunk returns the variable's address in the calling thread's block (allocating it on first use).
struct TlvDescriptor {
    void* (*thunk)(struct TlvDescriptor*);
    unsigned long key;
    unsigned long offset;  // (dyld4 rewrites key/offset at load time; see tls_bounds)
};

/// The executable's writable segments (static fields) and its TLV template, from its load commands.
static void cache_segments(void) {
    const struct mach_header_64* header = (const struct mach_header_64*)_dyld_get_image_header(0);  // image 0 = executable
    intptr_t slide = _dyld_get_image_vmaddr_slide(0);
    uintptr_t tls_lo = UINTPTR_MAX, tls_hi = 0;
    const uint8_t* cmd = (const uint8_t*)(header + 1);
    for (uint32_t i = 0; i < header->ncmds; i++, cmd += ((const struct load_command*)cmd)->cmdsize) {
        const struct segment_command_64* seg = (const struct segment_command_64*)cmd;
        if (seg->cmd != LC_SEGMENT_64) continue;
        if ((seg->initprot & VM_PROT_WRITE) && g.segment_count < MAX_SEGMENTS) {  // __DATA, __DATA_CONST
            g.segments[g.segment_count].lo = (uintptr_t)(seg->vmaddr + slide);
            g.segments[g.segment_count].hi = (uintptr_t)(seg->vmaddr + slide + seg->vmsize);
            g.segment_count++;
        }
        const struct section_64* sect = (const struct section_64*)(seg + 1);
        for (uint32_t s = 0; s < seg->nsects; s++, sect++) {
            uint32_t type = sect->flags & SECTION_TYPE;
            uintptr_t addr = (uintptr_t)(sect->addr + slide);
            if (type == S_THREAD_LOCAL_VARIABLES && sect->size >= sizeof(struct TlvDescriptor)) {
                g.tlv = (struct TlvDescriptor*)addr;
            } else if (type == S_THREAD_LOCAL_REGULAR || type == S_THREAD_LOCAL_ZEROFILL) {
                if (addr < tls_lo) tls_lo = addr;
                if (addr + sect->size > tls_hi) tls_hi = addr + sect->size;
            }
        }
    }
    if (g.tlv && tls_hi > tls_lo) { g.tlv_template = tls_lo; g.tls_size = tls_hi - tls_lo; }
    else g.tlv = NULL;
}

/// The calling thread's thread-local block ([ThreadStatic] fields, csrt TLS). Called at
/// registration, never while the world is stopped (the thunk may allocate the block).
static void tls_bounds(csrt_GcThread* t) {
    if (!g.tlv) return;
    // The thunk allocates the block on first use. dyld keeps the block's start under a pthread key
    // held in the low 32 bits of the descriptor's second word (dyld4 rewrites the rest of the
    // descriptor into its own format, so `offset` can't be used to get back to the start).
    uintptr_t variable = (uintptr_t)g.tlv->thunk(g.tlv);
    uintptr_t block = (uintptr_t)pthread_getspecific((pthread_key_t)(g.tlv->key & 0xFFFFFFFFu));
    if (!block || variable < block || variable >= block + g.tls_size) {
        static const char m[] = "csrt: unsupported thread-local storage layout (cannot find this thread's TLV block)\n";
        (void)!write(2, m, sizeof m - 1);
        abort();
    }
    t->tls_lo = block;
    t->tls_hi = block + g.tls_size;
}

/// Highest address of the calling thread's stack.
static void* own_stack_bottom(void) { return pthread_get_stackaddr_np(pthread_self()); }

static void ack_init(void) { semaphore_create(mach_task_self(), &g.ack, SYNC_POLICY_FIFO, 0); }
static void ack_post(void) { semaphore_signal(g.ack); }  // a Mach trap: async-signal-safe
static void ack_wait(void) { while (semaphore_wait(g.ack) == KERN_ABORTED) {} }
#elif defined(__wasi__)
// wasm-ld's layout with --stack-first: [stack | data (.data, .bss) | heap], the stack growing down
// from __stack_high. Static fields live in the data segment; there is no thread-local storage
// without the threads proposal (the compiler turns _Thread_local into ordinary globals).
extern char __stack_high, __global_base, __data_end;

static void cache_segments(void) {
    g.segments[0].lo = (uintptr_t)&__global_base;
    g.segments[0].hi = (uintptr_t)&__data_end;
    g.segment_count = 1;
}

static void tls_bounds(csrt_GcThread* t) { (void)t; }

static void* own_stack_bottom(void) { return &__stack_high; }
#else
static void ack_init(void) { sem_init(&g.ack, 0, 0); }
static void ack_post(void) { sem_post(&g.ack); }
static void ack_wait(void) { while (sem_wait(&g.ack) != 0 && errno == EINTR) {} }

static int cache_segments(struct dl_phdr_info* info, size_t size, void* data) {
    (void)size; (void)data;
    // The first entry is the main executable; static fields live in its writable PT_LOAD segments.
    // The RELRO range (method tables, vtables, interface maps, the GOT) is read-only once the loader has
    // relocated it, so it can't point into the heap: it is cut from the segment it starts.
    // In a large program it is most of the writable image (megabytes for an ASP.NET Core app).
    uintptr_t relro_lo = 0, relro_hi = 0;
    for (int i = 0; i < info->dlpi_phnum; i++) {
        const ElfW(Phdr)* ph = &info->dlpi_phdr[i];
        uintptr_t start = (uintptr_t)(info->dlpi_addr + ph->p_vaddr);
        if (ph->p_type == PT_TLS) { g.tls_size = ph->p_memsz; g.tls_align = ph->p_align ? ph->p_align : 1; }
        if (ph->p_type == PT_GNU_RELRO) { relro_lo = start; relro_hi = start + ph->p_memsz; }
        if (ph->p_type != PT_LOAD || !(ph->p_flags & PF_W) || g.segment_count == MAX_SEGMENTS) continue;
        g.segments[g.segment_count].lo = start;
        g.segments[g.segment_count].hi = start + ph->p_memsz;
        g.segment_count++;
    }
    // PT_GNU_RELRO starts a writable segment (and may cover all of it).
    for (size_t i = 0; i < g.segment_count; i++)
        if (relro_lo <= g.segments[i].lo && g.segments[i].lo < relro_hi) g.segments[i].lo = relro_hi < g.segments[i].hi ? relro_hi : g.segments[i].hi;
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

/// Highest address of the calling thread's stack (NULL if unknown).
static void* own_stack_bottom(void) {
    pthread_attr_t attr;
    void* stack_addr = NULL;
    size_t stack_size = 0;
    if (pthread_getattr_np(pthread_self(), &attr) == 0) {
        pthread_attr_getstack(&attr, &stack_addr, &stack_size);
        pthread_attr_destroy(&attr);
    }
    return stack_addr ? (uint8_t*)stack_addr + stack_size : NULL;
}
#endif

/// Lowest usable address of the calling thread's stack (0 if unknown).
static uintptr_t own_stack_limit(void) {
#if defined(_WIN32)
    ULONG_PTR low = 0, high = 0;
    GetCurrentThreadStackLimits(&low, &high);
    return (uintptr_t)low;
#elif defined(__APPLE__)
    pthread_t self = pthread_self();
    return (uintptr_t)pthread_get_stackaddr_np(self) - pthread_get_stacksize_np(self);
#elif defined(__wasi__)
    return 0;  // one fixed stack the engine bounds itself
#else
    pthread_attr_t attr;
    void* stack_addr = NULL;
    size_t stack_size = 0;
    if (pthread_getattr_np(pthread_self(), &attr) == 0) {
        pthread_attr_getstack(&attr, &stack_addr, &stack_size);
        pthread_attr_destroy(&attr);
    }
    return (uintptr_t)stack_addr;
#endif
}

/// Bytes of stack left below the caller (SIZE_MAX if the limit is unknown): RuntimeHelpers.
/// TryEnsureSufficientExecutionStack compares it with .NET's minimum (128 KiB on 64-bit targets).
size_t csrt_stack_remaining(void) {
    static _Thread_local uintptr_t limit;
    static _Thread_local bool known;
    if (!known) { limit = own_stack_limit(); known = true; }
    uintptr_t here = (uintptr_t)__builtin_frame_address(0);
    if (!limit) return SIZE_MAX;
    return here > limit ? here - limit : 0;
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
    env = getenv("CSRT_GC_STRESS");
    g.stress = env && atol(env) > 0 ? (size_t)atol(env) : 0;
#if defined(__GLIBC__)
    extern void* __libc_stack_end;
    g.stack_bottom = __libc_stack_end;
#elif defined(__APPLE__) || defined(_WIN32) || defined(__wasi__)
    g.stack_bottom = own_stack_bottom();
#else
    g.stack_bottom = __builtin_frame_address(0);
#endif
    pthread_mutex_init(&g.lock, NULL);
#if defined(_WIN32)
    cache_segments();
    thread_handles(&g.main_thread);
#elif defined(__APPLE__)
    ack_init();
    cache_segments();
#elif defined(__wasi__)
    cache_segments();
#else
    ack_init();
    dl_iterate_phdr(cache_segments, NULL);
#endif
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
#ifdef _WIN32
        p = malloc(size);  // 16-byte aligned on Win64 (MEMORY_ALLOCATION_ALIGNMENT)
        if (!p) return NULL;
#else
        if (posix_memalign(&p, 16, size) != 0) return NULL;
#endif
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
    gc_lock();
    SizeClass* sc = &g.classes[cls];
    AllocCache* cache = &tl_cache[cls];
    bool collected = false;
    void* result = NULL;
    if (g.stress && g.enabled && ++g.stress_count % g.stress == 0 && !g.collecting) { collect(); collected = true; }
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
    if (g.stress) {
        // Hand back all but one slot, so that the next allocation of this class is slow too.
        size_t w;
        Chunk* c = cache_word(cache, &w);
        c->allocated[w] &= ~(bits & (bits - 1));
        bits &= -bits;
    }
    cache->free_bits = bits & (bits - 1);
    result = cache->word_base + (size_t)__builtin_ctzll(bits) * k_size_classes[cls];
out:
    gc_unlock();
    return result;
}

__attribute__((noinline)) static void* alloc_large_slow(size_t size, bool clear) {
    if (!g.initialized) csrt_gc_init();
    gc_lock();
    void* result = alloc_large_locked(size, clear);
    gc_unlock();
    return result;
}

static void* alloc_large_locked(size_t size, bool clear) {
    if (g.enabled && (g.allocated_since_gc >= g.trigger || (g.stress && ++g.stress_count % g.stress == 0))) collect();
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

/// GC.CollectionCount: the collections so far.
int64_t csrt_gc_collection_count(void) { gc_lock(); int64_t n = (int64_t)g.collections; gc_unlock(); return n; }

/// GC.GetTotalAllocatedBytes: every byte handed out so far (allocation caches count when they claim slots).
int64_t csrt_gc_total_allocated(void) { return (int64_t)__atomic_load_n(&g.total_allocated, __ATOMIC_RELAXED); }

/// GC.GetTotalPauseDuration in 100-ns ticks: the collections' stop-the-world time.
int64_t csrt_gc_total_pause_ticks(void) {
    gc_lock();
    int64_t ticks = (int64_t)(g.total_gc_seconds * 1e7);
    gc_unlock();
    return ticks;
}

int64_t csrt_gc_total_memory(void) {
    gc_lock();
    int64_t total = (int64_t)(g.live_bytes + g.allocated_since_gc);
    gc_unlock();
    return total;
}

// ---------------------------------------------------------------------------------------------
// Marking
// ---------------------------------------------------------------------------------------------
static void* mmap_grow(void* old, size_t old_bytes, size_t new_bytes) {
#if defined(__wasi__)
    // One thread: nothing can hold the allocator's lock while the collector runs.
    (void)old_bytes;
    void* grown = realloc(old, new_bytes);
    if (!grown) { static const char m[] = "csrt: out of memory (gc metadata)\n"; (void)!write(2, m, sizeof m - 1); abort(); }
#elif defined(_WIN32)
    void* grown = VirtualAlloc(NULL, new_bytes, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!grown) { static const char m[] = "csrt: out of memory (gc metadata)\n"; (void)!_write(2, m, sizeof m - 1); abort(); }
    if (old) { memcpy(grown, old, old_bytes); VirtualFree(old, 0, MEM_RELEASE); }
#else
    void* grown = mmap(NULL, new_bytes, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (grown == MAP_FAILED) { static const char m[] = "csrt: out of memory (gc metadata)\n"; (void)!write(2, m, sizeof m - 1); abort(); }
    if (old) { memcpy(grown, old, old_bytes); munmap(old, old_bytes); }
#endif
    return grown;
}

__attribute__((noinline, cold)) static void mark_stack_grow(void) {
    // mmap, not realloc: this runs with the world stopped.
    size_t capacity = g.mark_capacity ? g.mark_capacity * 2 : 4096;
    g.mark_stack = mmap_grow(g.mark_stack, g.mark_capacity * sizeof *g.mark_stack, capacity * sizeof *g.mark_stack);
    g.mark_capacity = capacity;
}

static inline __attribute__((always_inline)) void mark_push(uint8_t* start, size_t size) {
    if (__builtin_expect(g.mark_count == g.mark_capacity, 0)) mark_stack_grow();
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
static inline __attribute__((always_inline)) void mark_candidate(uintptr_t word) {
    if (word < g.heap_min || word >= g.heap_max) return;

    uintptr_t base = word & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains(base)) {
        Chunk* c = (Chunk*)base;
        size_t offset = word - base;
        if (c->object_size == 0 || offset < CHUNK_HEADER) return;
        size_t index = slot_index(c, offset);
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

/// The start of the live heap object `p` points into, or NULL (MemoryMarshal.TryGetArray: Memory<T> is a
/// bare { pointer, length }, and the collector knows which array an interior pointer belongs to).
/// The large object containing `word`, outside a collection: the table is sorted at each collection, and
/// objects allocated since then are appended unsorted (a short tail, scanned; sorted once it grows).
static LargeObject* find_large_between_collections(uintptr_t word) {
    if (g.large_count - g.large_sorted > 64) {
        sort_large(g.large, g.large_count);
        g.large_sorted = g.large_count;
    }
    size_t lo = 0, hi = g.large_sorted;
    while (lo < hi) {
        size_t mid = (lo + hi) / 2;
        LargeObject* obj = &g.large[mid];
        if (word < (uintptr_t)obj->start) hi = mid;
        else if (word >= (uintptr_t)obj->start + obj->size) lo = mid + 1;
        else return obj;
    }
    for (size_t i = g.large_sorted; i < g.large_count; i++)
        if (word >= (uintptr_t)g.large[i].start && word < (uintptr_t)g.large[i].start + g.large[i].size) return &g.large[i];
    return NULL;
}

void* csrt_gc_object_base(const void* p) {
    uintptr_t word = (uintptr_t)p;
    void* result = NULL;
    gc_lock();
    if (word >= g.heap_min && word < g.heap_max) {
        uintptr_t base = word & ~(CHUNK_SIZE - 1);
        if (chunk_set_contains(base)) {
            Chunk* c = (Chunk*)base;
            size_t offset = word - base;
            if (c->object_size != 0 && offset >= CHUNK_HEADER) {
                size_t index = slot_index(c, offset);
                if (index < c->slots && bit_get(c->allocated, index)) result = (uint8_t*)base + CHUNK_HEADER + index * c->object_size;
            }
        } else {
            LargeObject* obj = find_large_between_collections(word);
            if (obj) result = obj->start;
        }
    }
    gc_unlock();
    return result;
}

// Conservative scanning reads every word of stacks, TLS and data segments on purpose; under
// AddressSanitizer (csharpc --sanitize=address) those reads must not be checked.
#if defined(__SANITIZE_ADDRESS__) || (defined(__has_feature) && __has_feature(address_sanitizer))
#define CSRT_GC_NO_SANITIZE __attribute__((no_sanitize("address")))
// The fake stack (use-after-return detection) moves locals off the real stack, where a conservative
// collector can't see them; leak detection doesn't understand GC-managed memory.
const char* __asan_default_options(void) { return "detect_stack_use_after_return=0:detect_leaks=0"; }
#else
#define CSRT_GC_NO_SANITIZE
#endif

CSRT_GC_NO_SANITIZE static void scan_range(const void* from, const void* to) {
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
        // (The compiler also emits element-typed array method tables, e.g. "System.Int32[]", with the flag.)
        const csrt_MethodTable* mt = *(const csrt_MethodTable* const*)start;
        if (mt && (mt->flags & CSRT_MT_NO_REFERENCES)) continue;
        scan_range(start + sizeof(void*), start + size);
    }
}

// Parallel marking (native targets only). The serial marker above is unchanged and is what runs for
// a small live set, under CSRT_GC_STRESS, and on wasm32. Helpers are not managed threads: they are
// not registered, not stopped, and they never allocate, so a collection can mark with every CPU
// while the mutators sit in the suspend handler.
//
// Mark bits stay plain stores. Each chunk, and each large object (by its index in the sorted
// table), has one owner for this collection and only that owner sets its bits. A pointer into
// another worker's object is batched across to that owner. Workers go idle under the same lock
// that publishes an inbox, and the last idle worker rechecks every inbox before the mark is done.
// A fetch_or on the shared mark bitmap was slower than the serial marker: every object in a chunk
// hits the same few cache lines. Not in the default runtime: on a 4-core machine it was slower
// than the serial marker and it costs several KiB in every binary. Build the runtime with
// -DCSRT_GC_PARALLEL_MARK (CSHARPC_RUNTIME_CFLAGS) to enable it; CSRT_GC_PARALLEL then selects
// the mode (1 always, -1 after a 3 ms mark, 0 serial). wasm32 has no threads.
#if defined(CSRT_GC_PARALLEL_MARK) && !defined(__wasi__)
#define MAX_HELPERS 7
#define MAX_WORKERS (MAX_HELPERS + 1)
#define OUT_BATCH 64
#define ABSORB_MAX 256
// A mark-stack entry whose size has this bit set is a raw pointer range (a slice of a large
// array), not an object. Object sizes never use the bit.
#define RAW_SCAN ((size_t)1 << (sizeof(size_t) * 8 - 1))

extern int32_t csrt_processor_count(void);

typedef struct {
    MarkItem* items;
    size_t count, capacity;
    MarkItem out[MAX_WORKERS][OUT_BATCH];
    uint16_t out_n[MAX_WORKERS];
    int id;
} MarkBuf;

typedef struct {
    MarkItem* items;
    size_t count, capacity;
} Inbox;

static int g_mark_heavy;  // set once a serial mark exceeds a few milliseconds
static int g_nworkers;
static int nwait;          // idle workers, guarded by work_mu
static atomic_int mark_done;
static pthread_mutex_t work_mu = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t work_cv = PTHREAD_COND_INITIALIZER;
static Inbox inboxes[MAX_WORKERS];

static struct {
    int n, idle, epoch, exit_flag, ready;
    atomic_int finished, exited;
    pthread_mutex_t mu;
    pthread_cond_t cv;
    pthread_t threads[MAX_HELPERS];
    MarkBuf bufs[MAX_HELPERS];
    MarkBuf self;
} pool = { .mu = PTHREAD_MUTEX_INITIALIZER, .cv = PTHREAD_COND_INITIALIZER };

static void cpu_relax(void) {
#if defined(__x86_64__) || defined(__i386__)
    __builtin_ia32_pause();
#elif defined(__aarch64__)
    __asm__ __volatile__("yield");
#else
    ;
#endif
}

static int parallel_setting(void) {
    static int cached = -2;
    if (cached != -2) return cached;
    const char* e = getenv("CSRT_GC_PARALLEL");
    // Serial unless asked. On a 4-core machine the ownership marker was slower than one thread:
    // the serial scan is a handful of instructions per word, and copying an object to its owner
    // costs more than scanning it. =1 marks with every CPU (a larger machine can still win),
    // =-1 is the old auto policy (parallel after a mark has taken 3 ms).
    cached = (e && *e) ? atoi(e) : 0;
    return cached;
}

static bool want_parallel(void) {
    if (g.stress) return false;
    int mode = parallel_setting();
    if (mode == 0 || csrt_processor_count() < 2) return false;
    // Auto mode waits until a mark has already cost a few milliseconds. Waking helpers for a
    // short list walk is slower than doing it alone, and the previous mark predicts the next one
    // while the live set stays large.
    if (mode < 0 && !g_mark_heavy) return false;
    return true;
}

static LargeObject* find_large(uintptr_t word, size_t* index_out) {
    size_t lo = 0, hi = g.large_count;
    while (lo < hi) {
        size_t mid = (lo + hi) / 2;
        LargeObject* obj = &g.large[mid];
        if (word < (uintptr_t)obj->start) hi = mid;
        else if (word >= (uintptr_t)obj->start + obj->size) lo = mid + 1;
        else {
            if (index_out) *index_out = mid;
            return obj;
        }
    }
    return NULL;
}

static void local_push(MarkBuf* b, uint8_t* start, size_t size) {
    if (__builtin_expect(b->count == b->capacity, 0)) {
        size_t cap = b->capacity ? b->capacity * 2 : 4096;
        b->items = mmap_grow(b->items, b->capacity * sizeof(MarkItem), cap * sizeof(MarkItem));
        b->capacity = cap;
    }
    b->items[b->count].start = start;
    b->items[b->count].size = size;
    b->count++;
}

static void inbox_grow(Inbox* in, size_t need) {
    size_t cap = in->capacity ? in->capacity * 2 : 1024;
    while (cap < need) cap *= 2;
    in->items = mmap_grow(in->items, in->capacity * sizeof(MarkItem), cap * sizeof(MarkItem));
    in->capacity = cap;
}

static void flush_dest(MarkBuf* b, int dest) {
    uint16_t n = b->out_n[dest];
    if (!n) return;
    pthread_mutex_lock(&work_mu);
    Inbox* in = &inboxes[dest];
    if (in->count + n > in->capacity) inbox_grow(in, in->count + n);
    memcpy(in->items + in->count, b->out[dest], n * sizeof(MarkItem));
    in->count += n;
    pthread_cond_broadcast(&work_cv);
    pthread_mutex_unlock(&work_mu);
    b->out_n[dest] = 0;
}

static void flush_all(MarkBuf* b) {
    for (int i = 0; i < g_nworkers; i++)
        if (b->out_n[i]) flush_dest(b, i);
}

static void queue_foreign(MarkBuf* b, int owner, uint8_t* start, size_t size) {
    uint16_t n = b->out_n[owner];
    b->out[owner][n].start = start;
    b->out[owner][n].size = size;
    b->out_n[owner] = (uint16_t)(n + 1);
    if (n + 1 == OUT_BATCH) flush_dest(b, owner);
}

/// Resolves `word` the same way the serial marker does, then either marks it (when this worker
/// owns the chunk) or copies the object start to the owner. The bit is set by the owner only.
static inline void consider(MarkBuf* b, uintptr_t word) {
    if (word < g.heap_min || word >= g.heap_max) return;
    uintptr_t base = word & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains(base)) {
        Chunk* c = (Chunk*)base;
        size_t offset = word - base;
        if (c->object_size == 0 || offset < CHUNK_HEADER) return;
        size_t index = slot_index(c, offset);
        if (index >= c->slots || !bit_get(c->allocated, index)) return;
        uint8_t* start = (uint8_t*)base + CHUNK_HEADER + index * c->object_size;
        if (c->owner == (uint8_t)b->id) {
            if (bit_get(c->marked, index)) return;
            bit_set(c->marked, index);
            local_push(b, start, c->object_size);
        } else {
            queue_foreign(b, c->owner, start, c->object_size);
        }
        return;
    }
    size_t idx = 0;
    LargeObject* obj = find_large(word, &idx);
    if (!obj) return;
    int owner = (int)(idx % (size_t)g_nworkers);
    if (owner == b->id) {
        if (obj->marked) return;
        obj->marked = 1;
        local_push(b, obj->start, obj->size);
    } else {
        queue_foreign(b, owner, obj->start, obj->size);
    }
}

CSRT_GC_NO_SANITIZE static void scan_range_owned(MarkBuf* b, const void* from, const void* to) {
    uintptr_t lo = ((uintptr_t)from + sizeof(void*) - 1) & ~(uintptr_t)(sizeof(void*) - 1);
    for (const uintptr_t* p = (const uintptr_t*)lo; (const void*)(p + 1) <= to; p++) consider(b, *p);
}

CSRT_GC_NO_SANITIZE static void scan_object_owned(MarkBuf* b, uint8_t* start, size_t size) {
    if (size & RAW_SCAN) {
        scan_range_owned(b, start, start + (size & ~RAW_SCAN));
        return;
    }
    const csrt_MethodTable* mt = *(const csrt_MethodTable* const*)start;
    if (mt && (mt->flags & CSRT_MT_NO_REFERENCES)) return;
    // One worker walking a multi-megabyte reference array is the serial bottleneck (every element
    // is a pointer into some other chunk). Hand each worker an aligned slice of the word range.
    if (size >= 8192 && g_nworkers > 1) {
        uintptr_t from = ((uintptr_t)(start + sizeof(void*)) + sizeof(void*) - 1) & ~(uintptr_t)(sizeof(void*) - 1);
        uintptr_t to = (uintptr_t)start + size;
        size_t bytes = to > from ? to - from : 0;
        size_t slice = (bytes / (size_t)g_nworkers) & ~(sizeof(void*) - 1);
        if (slice >= 4096) {
            for (int w = 0; w < g_nworkers; w++) {
                uintptr_t s = from + (size_t)w * slice;
                uintptr_t e = w == g_nworkers - 1 ? to : s + slice;
                size_t n = e - s;
                if (!n) continue;
                if (w == b->id) local_push(b, (uint8_t*)s, n | RAW_SCAN);
                else queue_foreign(b, w, (uint8_t*)s, n | RAW_SCAN);
            }
            return;
        }
    }
    scan_range_owned(b, start + sizeof(void*), start + size);
}

/// An inbox entry is an object the sender did not mark. The owner sets the bit, once. A raw slice
/// is not an object; it is scanned as words. Already-marked roots were pushed onto the owner's
/// stack before the helpers started and do not come through here.
static void claim_item(MarkBuf* b, MarkItem it) {
    if (it.size & RAW_SCAN) {
        local_push(b, it.start, it.size);
        return;
    }
    uintptr_t start = (uintptr_t)it.start;
    uintptr_t base = start & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains(base)) {
        Chunk* c = (Chunk*)base;
        if (c->object_size == 0 || start < base + CHUNK_HEADER) return;
        size_t index = slot_index(c, start - base);
        if (index >= c->slots || bit_get(c->marked, index)) return;
        bit_set(c->marked, index);
        local_push(b, it.start, c->object_size);
        return;
    }
    size_t idx = 0;
    LargeObject* obj = find_large(start, &idx);
    if (!obj || obj->marked) return;
    obj->marked = 1;
    local_push(b, obj->start, obj->size);
}

static bool absorb(MarkBuf* b) {
    pthread_mutex_lock(&work_mu);
    Inbox* in = &inboxes[b->id];
    size_t n = in->count;
    if (n == 0) { pthread_mutex_unlock(&work_mu); return false; }
    if (n > ABSORB_MAX) n = ABSORB_MAX;
    MarkItem tmp[ABSORB_MAX];
    memcpy(tmp, in->items + in->count - n, n * sizeof(MarkItem));
    in->count -= n;
    pthread_mutex_unlock(&work_mu);
    for (size_t i = 0; i < n; i++) claim_item(b, tmp[i]);
    return true;
}

static void worker_loop(MarkBuf* b) {
    for (;;) {
        if (atomic_load_explicit(&mark_done, memory_order_acquire)) return;
        if (b->count == 0) {
            flush_all(b);
            if (absorb(b)) continue;
            pthread_mutex_lock(&work_mu);
            if (inboxes[b->id].count) { pthread_mutex_unlock(&work_mu); continue; }
            nwait++;
            if (nwait == g_nworkers) {
                bool any = false;
                for (int i = 0; i < g_nworkers; i++)
                    if (inboxes[i].count) any = true;
                if (any) {
                    nwait--;
                    pthread_cond_broadcast(&work_cv);
                    pthread_mutex_unlock(&work_mu);
                    continue;
                }
                atomic_store_explicit(&mark_done, 1, memory_order_release);
                pthread_cond_broadcast(&work_cv);
                pthread_mutex_unlock(&work_mu);
                return;
            }
            while (!atomic_load_explicit(&mark_done, memory_order_acquire) && inboxes[b->id].count == 0)
                pthread_cond_wait(&work_cv, &work_mu);
            int done = atomic_load_explicit(&mark_done, memory_order_acquire);
            if (!done) nwait--;
            pthread_mutex_unlock(&work_mu);
            if (done) return;
            continue;
        }
        MarkItem it = b->items[--b->count];
        scan_object_owned(b, it.start, it.size);
    }
}

static void block_gc_signals(void) {
#if !defined(_WIN32)
    sigset_t set;
    sigemptyset(&set);
    sigaddset(&set, SIG_SUSPEND);
    sigaddset(&set, SIG_RESUME);
    pthread_sigmask(SIG_BLOCK, &set, NULL);
#else
    ;
#endif
}

static void* helper_main(void* arg) {
    MarkBuf* b = arg;
    block_gc_signals();
    int seen = 0;
    pthread_mutex_lock(&pool.mu);
    pool.idle++;
    pthread_cond_broadcast(&pool.cv);
    while (!pool.exit_flag) {
        while (pool.epoch == seen && !pool.exit_flag) pthread_cond_wait(&pool.cv, &pool.mu);
        if (pool.exit_flag) break;
        seen = pool.epoch;
        pool.idle--;
        pthread_mutex_unlock(&pool.mu);
        worker_loop(b);
        pthread_mutex_lock(&pool.mu);
        pool.idle++;
        atomic_fetch_add_explicit(&pool.finished, 1, memory_order_release);
        pthread_cond_broadcast(&pool.cv);
    }
    pthread_mutex_unlock(&pool.mu);
    atomic_fetch_add_explicit(&pool.exited, 1, memory_order_release);
    return NULL;
}

static MarkBuf mark_buf_new(void) {
    MarkBuf b;
    memset(&b, 0, sizeof b);
    b.capacity = 4096;
    b.items = mmap_grow(NULL, 0, b.capacity * sizeof(MarkItem));
    return b;
}

static void inbox_init(Inbox* in) {
    memset(in, 0, sizeof *in);
    in->capacity = 1024;
    in->items = mmap_grow(NULL, 0, in->capacity * sizeof(MarkItem));
}

static int ensure_pool(void) {
    if (pool.ready) return pool.n;
    for (int i = 0; i < MAX_WORKERS; i++) inbox_init(&inboxes[i]);
    int n = csrt_processor_count() - 1;
    const char* e = getenv("CSRT_GC_MARK_THREADS");
    if (e && *e) {
        int v = atoi(e);
        if (v < 1) v = 1;
        n = v - 1;
    }
    if (n < 1) { pool.ready = 1; pool.n = 0; return 0; }
    if (n > MAX_HELPERS) n = MAX_HELPERS;
    pool.self = mark_buf_new();
    pool.self.id = 0;
    for (int i = 0; i < n; i++) {
        pool.bufs[i] = mark_buf_new();
        pool.bufs[i].id = i + 1;
        pthread_attr_t attr;
        pthread_attr_init(&attr);
        pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_DETACHED);
        pthread_attr_setstacksize(&attr, 128 * 1024);
        if (pthread_create(&pool.threads[i], &attr, helper_main, &pool.bufs[i]) != 0) { n = i; pthread_attr_destroy(&attr); break; }
        pthread_attr_destroy(&attr);
    }
    pool.n = n;
    pthread_mutex_lock(&pool.mu);
    while (pool.idle < pool.n) pthread_cond_wait(&pool.cv, &pool.mu);
    pthread_mutex_unlock(&pool.mu);
    pool.ready = 1;
    return pool.n;
}

static void assign_owners(void) {
    int i = 0;
    for (Chunk* c = g.chunks; c; c = c->next) c->owner = (uint8_t)(i++ % g_nworkers);
}

static int owner_of_start(uint8_t* start) {
    uintptr_t base = (uintptr_t)start & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains(base)) return ((Chunk*)base)->owner;
    size_t idx = 0;
    if (!find_large((uintptr_t)start, &idx)) return 0;
    return (int)(idx % (size_t)g_nworkers);
}

/// Roots were marked by the serial scan. Put each one on its owner's stack so it is scanned once.
/// Helpers are not running yet, so their stacks are private.
static void seed_owners(void) {
    for (size_t i = 0; i < g.mark_count; i++) {
        MarkItem it = g.mark_stack[i];
        int owner = owner_of_start(it.start);
        MarkBuf* dest = owner == 0 ? &pool.self : &pool.bufs[owner - 1];
        local_push(dest, it.start, it.size);
    }
}

static void parallel_drain(void) {
    atomic_store_explicit(&mark_done, 0, memory_order_relaxed);
    nwait = 0;
    g_nworkers = pool.n + 1;
    pool.self.id = 0;
    pool.self.count = 0;
    memset(pool.self.out_n, 0, sizeof pool.self.out_n);
    for (int i = 0; i < pool.n; i++) {
        pool.bufs[i].id = i + 1;
        pool.bufs[i].count = 0;
        memset(pool.bufs[i].out_n, 0, sizeof pool.bufs[i].out_n);
    }
    for (int i = 0; i < g_nworkers; i++) inboxes[i].count = 0;
    assign_owners();
    seed_owners();
    g.mark_count = 0;

    atomic_thread_fence(memory_order_seq_cst);
    pthread_mutex_lock(&pool.mu);
    while (pool.idle < pool.n) pthread_cond_wait(&pool.cv, &pool.mu);
    atomic_store_explicit(&pool.finished, 0, memory_order_relaxed);
    pool.epoch++;
    pthread_cond_broadcast(&pool.cv);
    pthread_mutex_unlock(&pool.mu);
    worker_loop(&pool.self);
    pthread_mutex_lock(&pool.mu);
    while (atomic_load_explicit(&pool.finished, memory_order_acquire) < pool.n) pthread_cond_wait(&pool.cv, &pool.mu);
    pthread_mutex_unlock(&pool.mu);
    atomic_thread_fence(memory_order_seq_cst);
}

static void pool_shutdown(void) {
    if (!pool.ready || pool.n == 0 || pool.exit_flag) return;
    pthread_mutex_lock(&pool.mu);
    pool.exit_flag = 1;
    pthread_cond_broadcast(&pool.cv);
    pthread_mutex_unlock(&pool.mu);
    while (atomic_load_explicit(&pool.exited, memory_order_acquire) < pool.n) cpu_relax();
}
#endif

/// noinline so the caller's frame (and every frame above it) lies inside the scanned range.
CSRT_GC_NO_SANITIZE __attribute__((noinline)) static void scan_own_stack(const void* bottom) {
#ifdef __wasi__
    // The wasm stack's locals live outside linear memory; references live across the call that
    // got here were spilled into their frames' slots by csharpc (RootSpilling), which lie above us.
    volatile uintptr_t marker = 0;
    const void* top = (const void*)&marker;
#else
    jmp_buf registers;  // spills callee-saved registers onto this frame
    setjmp(registers);
    __builtin_unwind_init();
    volatile uintptr_t marker = 0;
    const void* top = (const void*)&marker;
    if ((const void*)registers < top) top = (const void*)registers;
#endif
    scan_range(top, bottom);
}

static void scan_roots(void) {
    for (size_t i = 0; i < g.segment_count; i++) scan_range((const void*)g.segments[i].lo, (const void*)g.segments[i].hi);
    for (csrt_GcThread* t = g.threads; t; t = t->next) {
        mark_candidate((uintptr_t)t->managed);  // e.g. the Thread object of a starting thread
        if (t->state != THREAD_RUNNING) continue;
        if (t->tls_hi) scan_range((const void*)t->tls_lo, (const void*)t->tls_hi);
        if (t == tl_self) scan_own_stack(t->stack_bottom);
        else {
#ifdef _WIN32
            scan_range(t->context, (const CONTEXT*)t->context + 1);  // registers (GetThreadContext)
#endif
            scan_range((const void*)t->stack_top, t->stack_bottom);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Stop the world
// ---------------------------------------------------------------------------------------------
#if defined(__wasi__)
// WASI preview 1 has no threads: the world is always stopped.
static void install_signal_handlers(void) {}
static int stop_world(void) { return 0; }
static void resume_world(void) {}
#elif defined(_WIN32)
// SuspendThread is asynchronous; GetThreadContext only returns once the thread is really stopped,
// and yields the registers to scan (all of CONTEXT: pointers can sit in vector registers too).
// Windows x64 has no red zone, so nothing live lies below the stopped thread's Rsp.
static void install_signal_handlers(void) {}

static int stop_world(void) {
    if (!g.multithreaded) return 0;
    int stopped = 0;
    for (csrt_GcThread* t = g.threads; t; t = t->next) {
        if (t->state != THREAD_RUNNING || t == tl_self) continue;
        if (SuspendThread(t->handle) == (DWORD)-1) goto fail;
        CONTEXT* context = t->context;
        memset(context, 0, sizeof *context);
        context->ContextFlags = CONTEXT_FULL | CONTEXT_FLOATING_POINT;
        if (!GetThreadContext(t->handle, context)) goto fail;
        t->stack_top = (void*)context->Rsp;
        stopped++;
    }
    return stopped;
fail:
    fputs("csrt: cannot suspend a thread for garbage collection\n", stderr);
    abort();
}

static void resume_world(void) {
    if (!g.multithreaded) return;
    for (csrt_GcThread* t = g.threads; t; t = t->next)
        if (t->state == THREAD_RUNNING && t != tl_self) ResumeThread(t->handle);
}
#else
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
    ack_post();
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
    for (int i = 0; i < signalled; i++) ack_wait();
    return signalled;
}

static void resume_world(void) {
    if (!g.multithreaded) return;
    atomic_store(&g.world_stopped, 0);
    for (csrt_GcThread* t = g.threads; t; t = t->next)
        if (t->state == THREAD_RUNNING && t != tl_self) pthread_kill(t->thread, SIG_RESUME);
}
#endif

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
    void* bottom = own_stack_bottom();
    pthread_mutex_lock(&g.lock);  // never attach in the middle of a collection
    t->thread = pthread_self();
    t->stack_bottom = bottom ? bottom : __builtin_frame_address(0);
    t->cache = tl_cache;
    tls_bounds(t);
#ifdef _WIN32
    thread_handles(t);
#endif
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
#ifdef _WIN32
    CloseHandle(t->handle);
    _aligned_free(t->context);
#endif
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
#ifdef CSRT_ALLOC_PROFILE
// Allocation profile (a runtime built with CSHARPC_RUNTIME_CFLAGS=-DCSRT_ALLOC_PROFILE, printed at exit
// with CSRT_GC_STATS=1): every object a sweep frees is counted under its method table, so the table
// shows what the program allocates and drops, by type, without touching the allocation fast path.
// Static storage: the world is stopped while it is updated (no malloc).
#define PROFILE_SLOTS 4096
static struct { const csrt_MethodTable* mt; uint64_t count, bytes; } s_profile[PROFILE_SLOTS];

static void profile_dead(const void* object, size_t size) {
    const csrt_MethodTable* mt = *(const csrt_MethodTable* const*)object;
    if (!mt) return;
    size_t h = ((uintptr_t)mt >> 4) & (PROFILE_SLOTS - 1);
    for (size_t probe = 0; probe < PROFILE_SLOTS; probe++, h = (h + 1) & (PROFILE_SLOTS - 1)) {
        if (s_profile[h].mt == mt || !s_profile[h].mt) {
            s_profile[h].mt = mt;
            s_profile[h].count++;
            s_profile[h].bytes += size;
            return;
        }
    }
}

static void profile_print(void) {
    fprintf(stderr, "csrt gc: objects freed by collections, by type (top 40 by bytes):\n");
    for (int n = 0; n < 40; n++) {
        size_t best = PROFILE_SLOTS;
        for (size_t i = 0; i < PROFILE_SLOTS; i++)
            if (s_profile[i].mt && s_profile[i].bytes && (best == PROFILE_SLOTS || s_profile[i].bytes > s_profile[best].bytes)) best = i;
        if (best == PROFILE_SLOTS) break;
        fprintf(stderr, "  %12llu objects %10.1f MiB  %s\n", (unsigned long long)s_profile[best].count,
                s_profile[best].bytes / 1048576.0, s_profile[best].mt->name ? s_profile[best].mt->name : "?");
        s_profile[best].bytes = 0;
    }
}
#endif

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
#ifdef CSRT_ALLOC_PROFILE
            for (uint64_t dead = c->allocated[w] & ~c->marked[w]; dead; dead &= dead - 1)
                profile_dead((uint8_t*)c + CHUNK_HEADER + (w * 64 + (size_t)__builtin_ctzll(dead)) * c->object_size, c->object_size);
#endif
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
#ifdef CSRT_ALLOC_PROFILE
            profile_dead(obj.start, obj.size);
#endif
        }
    }
    g.large_count = kept;
    g.large_sorted = kept;
    return live;
}

// ---------------------------------------------------------------------------------------------
// Weak references and ephemerons (WeakReference, ConditionalWeakTable: corelib/WeakReferences.cs)
// ---------------------------------------------------------------------------------------------
// A table outside the managed heap, so the conservative scan never sees the targets it holds. After
// marking, a slot whose target wasn't reached is cleared. An ephemeron slot (a ConditionalWeakTable
// entry) also keeps its value alive, but only while its key is reachable some other way. A slot
// with an owner (the WeakReference object, the table's storage) is freed when the owner dies: csharpc
// runs no finalizers, which is how .NET releases these handles.

typedef struct {
    uintptr_t target, value, owner;
    int32_t kind;       // 0 free, 1 weak, 2 ephemeron
    int32_t next_free;  // free list (index), -1 at the end
} WeakSlot;

static struct {
    WeakSlot* slots;
    size_t count, capacity;
    int32_t free_head;
    bool any;
} weak = { .free_head = -1 };

/// Whether a candidate pointer's object was marked by this collection (pointers outside the heap —
/// string literals, static data — are always alive). Valid between marking and sweeping.
static bool weak_is_marked(uintptr_t word) {
    if (word < g.heap_min || word >= g.heap_max) return true;
    uintptr_t base = word & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains(base)) {
        Chunk* c = (Chunk*)base;
        size_t offset = word - base;
        if (c->object_size == 0 || offset < CHUNK_HEADER) return false;
        size_t index = slot_index(c, offset);
        return index < c->slots && bit_get(c->allocated, index) && bit_get(c->marked, index);
    }
    size_t lo = 0, hi = g.large_count;
    while (lo < hi) {
        size_t mid = (lo + hi) / 2;
        LargeObject* obj = &g.large[mid];
        if (word < (uintptr_t)obj->start) hi = mid;
        else if (word >= (uintptr_t)obj->start + obj->size) lo = mid + 1;
        else return obj->marked;
    }
    return false;
}

static void weak_release(size_t i) {
    weak.slots[i] = (WeakSlot){ .kind = 0, .next_free = weak.free_head };
    weak.free_head = (int32_t)i;
}

/// Runs with the world stopped, after the roots are drained and before the sweep.
static void weak_process(void) {
    if (!weak.any) return;
    // Ephemerons: mark the values of entries whose key (and owning table) are reachable, to a fixed
    // point — a value may make another entry's key or table reachable.
    for (bool again = true; again;) {
        again = false;
        for (size_t i = 0; i < weak.count; i++) {
            WeakSlot* s = &weak.slots[i];
            if (s->kind != 2 || !s->target || !s->value) continue;
            if (s->owner && !weak_is_marked(s->owner)) continue;
            if (weak_is_marked(s->target) && !weak_is_marked(s->value)) {
                mark_candidate(s->value);
                drain();
                again = true;
            }
        }
    }
    for (size_t i = 0; i < weak.count; i++) {
        WeakSlot* s = &weak.slots[i];
        if (s->kind == 0) continue;
        if (s->owner && !weak_is_marked(s->owner)) { weak_release(i); continue; }
        if (s->target && !weak_is_marked(s->target)) { s->target = 0; s->value = 0; }
    }
}

intptr_t csrt_weak_alloc(void* target, void* value, void* owner, int32_t ephemeron) {
    if (!g.initialized) csrt_gc_init();
    gc_lock();
    if (weak.free_head < 0) {
        if (weak.count == weak.capacity) {
            size_t capacity = weak.capacity ? weak.capacity * 2 : 256;
            weak.slots = mmap_grow(weak.slots, weak.capacity * sizeof(WeakSlot), capacity * sizeof(WeakSlot));
            weak.capacity = capacity;
        }
        weak.slots[weak.count] = (WeakSlot){ .next_free = -1 };
        weak.free_head = (int32_t)weak.count++;
    }
    size_t i = (size_t)weak.free_head;
    weak.free_head = weak.slots[i].next_free;
    weak.slots[i] = (WeakSlot){ (uintptr_t)target, (uintptr_t)value, (uintptr_t)owner, ephemeron ? 2 : 1, -1 };
    weak.any = true;
    gc_unlock();
    return (intptr_t)i + 1;
}

void* csrt_weak_target(intptr_t handle) {
    gc_lock();
    void* target = (void*)weak.slots[handle - 1].target;
    gc_unlock();
    return target;
}

/// An ephemeron's value (NULL once its key is gone).
void* csrt_weak_value(intptr_t handle) {
    gc_lock();
    void* value = (void*)weak.slots[handle - 1].value;
    gc_unlock();
    return value;
}

void csrt_weak_set(intptr_t handle, void* target, void* value) {
    gc_lock();
    weak.slots[handle - 1].target = (uintptr_t)target;
    weak.slots[handle - 1].value = (uintptr_t)value;
    gc_unlock();
}

void csrt_weak_free(intptr_t handle) {
    gc_lock();
    weak_release((size_t)handle - 1);
    gc_unlock();
}

static void note_pause(double seconds);

__attribute__((noinline)) void csrt_gc_collect_now(void) {
    if (g.collecting) return;
    g.collecting = true;
    // Thread startup stays outside the pause: the mutator is still running, and the helpers block
    // on their condition variable until parallel_drain publishes a job.
#if defined(CSRT_GC_PARALLEL_MARK) && !defined(__wasi__)
    int parallel = want_parallel() && ensure_pool() > 0;
#endif
    struct timespec t0, t1;
    clock_gettime(CLOCK_MONOTONIC, &t0);  // GC.GetTotalPauseDuration

    stop_world();
    sort_large(g.large, g.large_count);
    struct timespec t_mark0, t_mark1, t_sweep0, t_sweep1;
    clock_gettime(CLOCK_MONOTONIC, &t_mark0);
    scan_roots();
#if defined(CSRT_GC_PARALLEL_MARK) && !defined(__wasi__)
    if (parallel && g.mark_count > 0) {
        parallel_drain();
        g.parallel_collections++;
    } else
#endif
        drain();
    weak_process();
    clock_gettime(CLOCK_MONOTONIC, &t_mark1);
    protect_caches();
    clock_gettime(CLOCK_MONOTONIC, &t_sweep0);
    g.live_bytes = sweep();
    clock_gettime(CLOCK_MONOTONIC, &t_sweep1);
    resume_world();
    for (size_t i = 0; i < g.deferred_count; i++) free(g.deferred_free[i]);
    g.deferred_count = 0;
    g.allocated_since_gc = 0;
    g.trigger = 2 * g.live_bytes > g.min_trigger ? 2 * g.live_bytes : g.min_trigger;
    g.collections++;

    clock_gettime(CLOCK_MONOTONIC, &t1);
    g.total_gc_seconds += (double)(t1.tv_sec - t0.tv_sec) + (double)(t1.tv_nsec - t0.tv_nsec) / 1e9;
    g.mark_seconds += (double)(t_mark1.tv_sec - t_mark0.tv_sec) + (double)(t_mark1.tv_nsec - t_mark0.tv_nsec) / 1e9;
    g.sweep_seconds += (double)(t_sweep1.tv_sec - t_sweep0.tv_sec) + (double)(t_sweep1.tv_nsec - t_sweep0.tv_nsec) / 1e9;
    note_pause((double)(t1.tv_sec - t0.tv_sec) + (double)(t1.tv_nsec - t0.tv_nsec) / 1e9);
#if defined(CSRT_GC_PARALLEL_MARK) && !defined(__wasi__)
    {
        double mark_s = (double)(t_mark1.tv_sec - t_mark0.tv_sec) + (double)(t_mark1.tv_nsec - t_mark0.tv_nsec) / 1e9;
        if (mark_s >= 0.003) g_mark_heavy = 1;
        // Create the helpers after a heavy serial mark, outside this pause, so the next one
        // does not pay for pthread_create.
        if (g_mark_heavy && !parallel && !g.stress && parallel_setting() != 0 && csrt_processor_count() > 1)
            ensure_pool();
    }
#endif
    g.collecting = false;
}

#define PAUSE_RING 512
static uint32_t s_pause_us[PAUSE_RING];
static uint32_t s_pause_n, s_pause_max_us;

static void note_pause(double seconds) {
    uint32_t us = seconds <= 0 ? 0 : seconds >= 4294.0 ? 4294967295u : (uint32_t)(seconds * 1e6);
    s_pause_us[s_pause_n % PAUSE_RING] = us;
    s_pause_n++;
    if (us > s_pause_max_us) s_pause_max_us = us;
}

static uint32_t pause_at(double q) {
    uint32_t n = s_pause_n < PAUSE_RING ? s_pause_n : PAUSE_RING;
    if (n == 0) return 0;
    uint32_t tmp[PAUSE_RING];
    memcpy(tmp, s_pause_us, n * sizeof tmp[0]);
    for (uint32_t i = 1; i < n; i++) {
        uint32_t v = tmp[i], j = i;
        while (j > 0 && tmp[j - 1] > v) { tmp[j] = tmp[j - 1]; j--; }
        tmp[j] = v;
    }
    return tmp[(uint32_t)(q * (n - 1))];
}

void csrt_gc_collect(void) {
    if (!g.initialized) csrt_gc_init();
    if (!g.enabled) return;
    gc_lock();
    collect();
    gc_unlock();
}

void csrt_gc_shutdown(void) {
#if defined(CSRT_GC_PARALLEL_MARK) && !defined(__wasi__)
    pool_shutdown();
#endif
    if (!g.stats) return;
    fprintf(stderr,
            "csrt gc: %zu collections (%zu parallel), %.1f ms total (mark %.1f ms, sweep %.1f ms), pause p50 %.2f ms p99 %.2f ms max %.2f ms, allocated %.1f MiB, peak heap %.1f MiB, live %.1f MiB, %zu chunks, %zu large objects\n",
            g.collections, g.parallel_collections, g.total_gc_seconds * 1000.0, g.mark_seconds * 1000.0, g.sweep_seconds * 1000.0,
            pause_at(0.50) / 1000.0, pause_at(0.99) / 1000.0, s_pause_max_us / 1000.0,
            g.total_allocated / 1048576.0, g.peak_heap / 1048576.0,
            (g.live_bytes + g.allocated_since_gc) / 1048576.0, g.chunk_count, g.large_count);
#ifdef CSRT_ALLOC_PROFILE
    profile_print();
#endif
}
