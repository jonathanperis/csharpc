// csrt garbage collector: conservative, non-moving, stop-the-world mark-sweep with an optional
// sticky nursery (docs/adr/0004-memory-management.md).
//
// Design:
//   * Small objects (<= 2048 bytes) live in 256 KiB chunks, aligned to their size, each serving a
//     single size class. A chunk starts with a header holding the per-slot "allocated" and "mark"
//     bitmaps, so any candidate pointer maps to (chunk, slot) with a mask, a hash lookup and a
//     division — interior pointers included.
//   * Allocation works directly on the allocated bitmap: each size class caches one 64-slot
//     bitmap word's free mask, and the fast path is ctz + clear-lowest-bit + multiply (no free
//     lists to thread or rebuild). A whole word is claimed at once; before marking, the unused
//     part of each claimed word is handed back. Sweeping is just `allocated &= marked`.
//   * Large objects get their own calloc'd block and are tracked in an array that is sorted at the
//     start of each collection, so candidate pointers are resolved by binary search. They are born
//     old: a minor collection finds young pointers in them only through the card table.
//   * Roots are scanned conservatively: the main thread's stack (with callee-saved registers
//     spilled by setjmp) and the writable segments (.data/.bss) of the main executable, which is
//     where the compiler puts static fields. Pointers outside the heap — e.g. string literals in
//     .rodata — are ignored. Every word of a reachable object is scanned conservatively, too.
//   * Nursery (opt-in via --gc-nursery / CSHARPC_GC_NURSERY=1, which compiles with
//     -DCSRT_GC_NURSERY_DEFAULT): small objects are allocated into young chunks. A minor
//     collection marks only young objects from stacks, statics, dirty old chunks/large objects
//     and remembered coroutine frames, then sweeps young chunks. Survivors stay in place but their
//     chunks are promoted to old (so old→young edges from this collection become old→old and cards
//     can be cleared). A major collection is today's full mark-sweep. CSRT_GC_NURSERY=0 disables
//     the nursery in a nursery-built binary. The default binary does not compile nursery/card code
//     in: barriers blow the async size budget and churn throughput drops (docs/adr/0004).
//   * Cards: per-chunk and per-large-object dirty bits (mmap heaps are sparse; a flat table over
//     [heap_min, heap_max) is not viable). csharpc turns heap reference stores into csrt_gc_write
//     (dirty, then store) and dirties before copies (ModuleCodeGen.WriteBarriers.cs); the runtime
//     uses CSRT_STORE_REF / CSRT_DIRTY (csrt.h). A minor runs every CSRT_GC_NURSERY_MB (32) MiB
//     of allocation; a major after 32 minors or once the heap doubles since the last major.
//     CSRT_GC_STRESS collections are minors too.
//
// Threads (docs/13-threading.md): every managed thread registers itself (stack bounds, TLS block,
// allocation cache). Allocation caches are thread-local, so the fast path stays lock-free; slow
// paths take one global lock. A collection stops the world with a signal handshake (as Boehm GC
// does): each mutator spills its registers in a signal handler, records its stack pointer, acks and
// sleeps in sigsuspend until the collector resumes it. While the world is stopped the collector
// never calls malloc/free, stdio or the dynamic loader (a stopped thread may hold their locks).
//
// Limitations: conservative scanning can retain garbage that happens to look like a pointer; no
// finalizers; empty chunks are kept mapped for reuse by any size class (with the thread pool, its
// timer thread gives their pages and other free slot pages back to the OS once allocation goes quiet). An
// ownership parallel marker can be compiled in with -DCSRT_GC_PARALLEL_MARK. It was slower than this serial marker
// on a 4-core machine, so the default binary does not include it (docs/adr/0004-memory-management.md).
//
// Environment: CSRT_GC=0 disables collection; CSRT_GC_STATS=1 prints statistics at exit;
// CSRT_GC_STRESS=N collects on every N-th allocation (every allocation takes the slow path), a test
// mode for root discovery. With -DCSRT_GC_PARALLEL_MARK, CSRT_GC_PARALLEL=1 marks with one worker
// per CPU (0 is serial, -1 after a pause of at least 3 ms) and CSRT_GC_MARK_THREADS caps the count.
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
#if defined(__GLIBC__)
#include <malloc.h>  // malloc_trim
#endif
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
// Released slot pages (release_chunk) read as zero when touched again with Linux's MADV_DONTNEED and Windows'
// decommit + recommit; macOS's MADV_FREE_REUSABLE may leave their old contents.
#if defined(__APPLE__) || defined(__wasi__)
#define RELEASED_PAGES_READ_ZERO 0
#else
#define RELEASED_PAGES_READ_ZERO 1
#endif
#define MIN_TRIGGER  ((size_t)32 * 1024 * 1024)
// 32 MiB matches the historical full-heap trigger cadence on churn; 4 MiB minors were too frequent
// once survivors promote (each minor then walks dirty old chunks).
#define NURSERY_MIN  ((size_t)32 * 1024 * 1024)
// Nursery support is compiled only into --gc-nursery / CSHARPC_GC_NURSERY=1 binaries so the default
// runtime stays inside the size budgets (async is ~176 bytes under).
#if defined(CSRT_GC_NURSERY_DEFAULT)
#define CSRT_HAS_NURSERY 1
#else
#define CSRT_HAS_NURSERY 0
#endif
// Concurrent marking (--gc-concurrent, docs/adr/0020-concurrent-marking.md): native targets only, and not
// together with the nursery.
#if defined(CSRT_GC_CONCURRENT) && !defined(__wasi__) && !CSRT_HAS_NURSERY
#define CSRT_HAS_CONCURRENT 1
#else
#define CSRT_HAS_CONCURRENT 0
#endif
// A new object, as the compiler sees it after the allocator is inlined (runtime LTO): an empty asm that
// ConcurrentBarrierElision.cs recognizes, after the optimizer, to drop the barriers of stores of objects
// that were held since they were allocated (they are marked whenever a cycle runs), and then removes.
#if CSRT_HAS_CONCURRENT
#define FRESH(p) __asm__("/* csrt.fresh */" : "+r"(p))
#else
#define FRESH(p) ((void)0)
#endif

static const uint32_t k_size_classes[17] = {16, 32, 48, 64, 80, 96, 128, 160, 192, 256, 320, 384, 512, 768, 1024, 1536, 2048};
#define NUM_CLASSES (sizeof k_size_classes / sizeof k_size_classes[0])

typedef struct Chunk Chunk;
struct Chunk {
    uint32_t object_size;  // 0 = empty chunk, available for any size class
    uint32_t slots;
    uint32_t live;         // live slots after the last sweep
    uint32_t size_class;
    uint32_t slot_magic;   // floor(2^32 / object_size) + 1: offset / object_size as a multiply (slot_of)
#if CSRT_HAS_NURSERY
    uint8_t young;         // 1: nursery chunk (minor collections sweep these)
    uint8_t dirty;         // old→young card: set by the write barrier, cleared when scanned
    uint8_t remembered;    // may hold a coroutine frame: minors scan those objects (csrt_gc_remember)
#endif
#if defined(CSRT_GC_PARALLEL_MARK)
    uint8_t owner;         // which parallel marker writes this chunk's mark bits
#endif
#if CSRT_HAS_CONCURRENT
    uint8_t remembered;    // may hold a coroutine frame: the remark rescans those objects (csrt_gc_remember)
#endif
    uint8_t released;      // empty, and its slot pages were given back to the OS (release_chunk)
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
#if defined(CSRT_GC_CONCURRENT) && !defined(__wasi__)
    uint64_t claimed;     // the word's free slots when it was claimed (the initial mark's in-flight slot)
#endif
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

typedef struct {
    uint8_t* start;
    size_t size;
    bool marked;
#if CSRT_HAS_NURSERY
    bool dirty;  // write barrier: any store into this large object since the last minor
#endif
} LargeObject;

static struct {
    bool enabled, stats, initialized, collecting, multithreaded;
#if CSRT_HAS_CONCURRENT
    bool large_remembered; // a large coroutine frame may exist (csrt_gc_remember)
#endif
#if CSRT_HAS_NURSERY
    bool nursery;          // generational mode (--gc-nursery build; CSRT_GC_NURSERY=0 disables)
    bool minor;            // true while a minor collection is marking/sweeping
    bool force_major;      // next collect is major (GC.Collect, promotion pressure)
    bool large_dirty;      // any large-object card dirty (scanned wholesale on minor)
    bool large_remembered; // a large coroutine frame may exist (csrt_gc_remember)
#endif
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
    struct { uint8_t* start; size_t size; }* mark_stack;
    size_t mark_count, mark_capacity;

    size_t allocated_since_gc, live_bytes, trigger, min_trigger;
    size_t growth_pct, budget_floor;  // the budget after a collection: growth_pct % of live, at least budget_floor (adapt_budget)
    double last_gc_end;               // CLOCK_MONOTONIC seconds when the previous collection resumed the world
    const void* own_stack_top;        // the collecting thread's scan starts here (csrt_gc_collect_now)
    size_t released_chunks;           // empty chunks whose pages went back to the OS (release_surplus_chunks)
#if CSRT_HAS_NURSERY
    // Minors run every nursery_trigger bytes of allocation of any kind (small, large, into reused
    // old chunks); a collection is major once the heap reaches major_trigger (2x the last major's live).
    size_t nursery_trigger, nursery_trigger_min, major_trigger;
    size_t minors_since_major;
#endif
    size_t stress, stress_count;  // CSRT_GC_STRESS
    size_t collections, peak_heap, total_allocated;
#if CSRT_HAS_NURSERY
    size_t major_collections;  // CollectionCount(gen>0); equals collections when nursery is off
#endif
    double total_gc_seconds;
} g;
#if CSRT_HAS_CONCURRENT
// The large-object table marking searches: g.large as sorted when the cycle (or collection) began.
static LargeObject* cm_large;
static size_t cm_large_count;
static void defer_free(void* table);
static bool cm_sweeping;          // the remark is over; chunks are being swept (lazily, and by the marker)
static bool cm_active;            // between the initial mark and the end of the remark
static bool sweep_some(size_t chunks);
static void sort_large_appended(LargeObject* a, size_t sorted, size_t n);
static bool concurrent_trigger(void);
static void concurrent_wait(void);
static size_t cm_alloc_at_start, cm_cycle_alloc;  // allocation before the initial mark, and during the cycle
static size_t cm_start_at;        // allocation that starts a cycle (cm_pace): below the trigger, 0 = the trigger
static inline size_t cm_threshold(void) { return cm_start_at && cm_start_at < g.trigger ? cm_start_at : g.trigger; }
static void cm_pace(void);
bool csrt_gc_remembered_object(const void* obj);  // async.c
#endif

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
#if CSRT_HAS_NURSERY || CSRT_HAS_CONCURRENT
        // Write barriers (and the concurrent marker) look chunks up without the lock (chunk_set_contains_unlocked): the capacity
        // travels with the table, and a table a mutator may still be reading is never freed.
        uintptr_t* table = calloc(capacity + 1, sizeof *table);
        if (!table) { fputs("csrt: out of memory (gc metadata)\n", stderr); abort(); }
        *table++ = capacity;
        for (size_t i = 0; i < g.chunk_set_capacity; i++)
            if (g.chunk_set[i]) chunk_set_insert_raw(table, capacity, g.chunk_set[i]);
        __atomic_store_n(&g.chunk_set, table, __ATOMIC_RELEASE);
#else
        uintptr_t* table = calloc(capacity, sizeof *table);
        if (!table) { fputs("csrt: out of memory (gc metadata)\n", stderr); abort(); }
        for (size_t i = 0; i < g.chunk_set_capacity; i++)
            if (g.chunk_set[i]) chunk_set_insert_raw(table, capacity, g.chunk_set[i]);
        free(g.chunk_set);
        g.chunk_set = table;
#endif
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

#if CSRT_HAS_CONCURRENT
/// The concurrent marker's chunk lookup: mutators may grow the set meanwhile (see the nursery's
/// chunk_set_contains_unlocked below; the same table layout). A chunk mapped after the table was read
/// holds only objects allocated during the cycle, which are black.
static inline bool chunk_set_contains_marker(uintptr_t base) {
    const uintptr_t* table = __atomic_load_n(&g.chunk_set, __ATOMIC_ACQUIRE);
    if (!table) return false;
    size_t mask = (size_t)table[-1] - 1;
    for (size_t i = hash_ptr(base) & mask;; i = (i + 1) & mask) {
        uintptr_t v = __atomic_load_n(&table[i], __ATOMIC_RELAXED);
        if (v == base) return true;
        if (!v) return false;
    }
}
#endif

static void note_range(uintptr_t lo, uintptr_t hi) {
    if (!g.heap_min || lo < g.heap_min) g.heap_min = lo;
    if (hi > g.heap_max) g.heap_max = hi;
}

#if CSRT_HAS_NURSERY
/// chunk_set_contains for mutators, which run the barrier without the lock while another thread may be
/// growing the set: a table is published together with its capacity (the word before it) and old
/// tables are never freed (chunk_set_insert), so a racing lookup reads a complete, consistent table.
/// A chunk added after the table was read belongs to the thread that mapped it, not to this store.
static inline bool chunk_set_contains_unlocked(uintptr_t base) {
    const uintptr_t* table = __atomic_load_n(&g.chunk_set, __ATOMIC_ACQUIRE);
    if (!table) return false;
    size_t mask = (size_t)table[-1] - 1;
    for (size_t i = hash_ptr(base) & mask;; i = (i + 1) & mask) {
        uintptr_t v = __atomic_load_n(&table[i], __ATOMIC_RELAXED);
        if (v == base) return true;
        if (!v) return false;
    }
}

/// Mark the old chunk or large object containing `slot` dirty. Young targets are ignored (no old→young).
/// Flat card tables over [heap_min, heap_max) are unusable: mmap chunks are sparse under ASLR.
static inline void card_dirty(void* slot) {
    uintptr_t p = (uintptr_t)slot;
    if (p < g.heap_min || p >= g.heap_max) return;
    uintptr_t base = p & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains_unlocked(base)) {
        Chunk* c = (Chunk*)base;
        if (!c->young) c->dirty = 1;
        return;
    }
    g.large_dirty = true;
}

/// Coroutine frames (async methods' tasks, iterators): LLVM's coroutine lowering stores spilled locals
/// into them with plain stores, after csharpc placed its write barriers, so no card is dirtied for
/// those. Instead the chunk (or the large-object list) is remembered: every minor collection scans the
/// frames in it for as long as one is allocated there (scan_cards). Called right after allocation.
void csrt_gc_remember(void* obj) {
    if (!g.nursery) return;
    uintptr_t base = (uintptr_t)obj & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains_unlocked(base)) ((Chunk*)base)->remembered = 1;
    else g.large_remembered = true;
}

void csrt_gc_cards_dirty(void* start, size_t n) {
    if (!g.nursery || n == 0) return;
    uint8_t* p = (uint8_t*)start;
    uint8_t* end = p + n;
    while (p < end) {
        card_dirty(p);
        uintptr_t next = ((uintptr_t)p & ~(CHUNK_SIZE - 1)) + CHUNK_SIZE;
        p = (uint8_t*)next;
    }
}

void csrt_gc_write(void** slot, void* value) {
    // Dirty first: a signal stop between store and dirty would hide an old→young edge.
    if (g.nursery) card_dirty(slot);
    *slot = value;
}
#endif
// Without CSRT_HAS_NURSERY the barrier symbols are not defined: default binaries must not
// reference them (codegen emits calls only under --gc-nursery; bclmisc gates Array.Copy).

#if defined(CSRT_GC_CONCURRENT)
// The concurrent marker's barrier (docs/adr/0020-concurrent-marking.md, step 1: the barrier and its
// cost). Generated code and CSRT_STORE_REF test `csrt_gc_marking` inline and call these only while a
// cycle marks; no cycle sets it yet, so they are never reached.
volatile uint8_t csrt_gc_marking;
#if !CSRT_HAS_CONCURRENT
__attribute__((noinline, cold)) void csrt_gc_log(void* value) { (void)value; }
__attribute__((noinline, cold)) void csrt_gc_log_range(void* start, size_t bytes) { (void)start; (void)bytes; }
#endif
#endif

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
    c->slots = (uint32_t)((CHUNK_SIZE - CHUNK_HEADER) / size);
    c->slot_magic = (uint32_t)((((uint64_t)1 << 32) / size) + 1);
    c->live = 0;
#if CSRT_HAS_NURSERY
    c->young = g.nursery ? 1 : 0;
    c->dirty = 0;
    c->remembered = 0;
#endif
    memset(c->allocated, 0, sizeof c->allocated);
    memset(c->marked, 0, sizeof c->marked);
#if CSRT_HAS_CONCURRENT
    c->remembered = 0;
#endif
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
                // Prefer an empty chunk that last held this size class (among the first few): it needs no
                // clearing below.
                Chunk** link = &g.empty;
                for (int n = 0; n < 8 && *link && (*link)->size_class != cls; n++) link = &(*link)->next_free;
                if (!*link || (*link)->size_class != cls) link = &g.empty;
                c = *link;
                *link = c->next_free;
                // Slots claimed but not handed out yet count as allocated, and so does the one a stopped
                // thread is halfway through allocating: a stale pointer into one (a conservative root, a
                // register) makes the collector read its first word as a method table. Within one size
                // class that is a dead object's, or zero; across classes it can be any field of an old
                // object, so each new slot starts with a null method table, which marking skips. Without it
                // the 4-thread GC benchmark (benchmarks/gc/gclatency.cs) crashed in about one run in ten,
                // reading flags through a garbage "method table".
                // Slot starts of a class whose size is a multiple of the old one's are old slot starts.
                uint32_t old_size = k_size_classes[c->size_class], new_size = k_size_classes[cls];
                bool zeroed = c->released && RELEASED_PAGES_READ_ZERO;  // nothing to clear
                if (c->released) { c->released = 0; g.released_chunks--; }
                if (zeroed) {}  // its pages read as zero: nothing to clear
                else
#if CSRT_HAS_NURSERY
                if (g.nursery && c->size_class != cls) memset((uint8_t*)c + CHUNK_HEADER, 0, CHUNK_SIZE - CHUNK_HEADER);  // minors scan whole chunks
                else
#endif
                if (new_size % old_size != 0)
                    for (uint8_t *slot = (uint8_t*)c + CHUNK_HEADER, *end = slot + (CHUNK_SIZE - CHUNK_HEADER) / new_size * new_size;
                         slot < end; slot += new_size)
                        *(void**)slot = NULL;
                chunk_format(c, cls);
            } else {
#if CSRT_HAS_CONCURRENT
                // Chunks the last cycle hasn't swept yet may free slots: sweep some before mapping more.
                if (cm_sweeping && sweep_some(16)) continue;
#endif
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
#if CSRT_HAS_CONCURRENT
            // Allocation is black while a cycle marks: the cycle never frees what was allocated during it.
            // Not while the chunks are swept: claim_word then only takes swept chunks (the lists are rebuilt
            // by the sweep) or new ones, which this sweep never visits, so marks set there would outlive it
            // and the next cycle would take those objects as already traced.
            if (csrt_gc_marking) __atomic_fetch_or(&c->marked[w], free_bits, __ATOMIC_RELAXED);
#endif
            cache->free_bits = free_bits;
#if CSRT_HAS_CONCURRENT
            cache->claimed = free_bits;
#endif
            cache->word_base = (uint8_t*)c + CHUNK_HEADER + w * 64 * (size_t)c->object_size;
            sc->word = w + 1;
            size_t bytes = (size_t)__builtin_popcountll(free_bits) * c->object_size;
            g.allocated_since_gc += bytes;
#if CSRT_HAS_NURSERY
            // Objects handed out of an old chunk (reused after a major) are born old: their first stores
            // may be the runtime's unbarriered initialization. The next minor scans the chunk.
            if (!c->young) c->dirty = 1;
#endif
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
#if CSRT_HAS_NURSERY
            // A minor neither sweeps nor unmarks old chunks: a mark set here would survive into the next
            // major, which would then take those slots as already traced and never scan them.
            if (g.minor && !c->young) continue;
#endif
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
    g.growth_pct = 200;
    g.budget_floor = g.min_trigger;
#if CSRT_HAS_NURSERY
    // Built with --gc-nursery: on unless CSRT_GC_NURSERY=0. Coarse per-chunk cards help RSS on
    // livemutate but cost churn throughput and binary size (docs/adr/0004).
    env = getenv("CSRT_GC_NURSERY");
    g.nursery = !(env && env[0] == '0');
    env = getenv("CSRT_GC_NURSERY_MB");
    g.nursery_trigger_min = env && atol(env) > 0 ? (size_t)atol(env) * 1024 * 1024 : NURSERY_MIN;
    g.nursery_trigger = g.nursery_trigger_min;
    g.major_trigger = g.min_trigger;
#endif
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
        // A collection can run before the caller stores the method table (after the lock is released):
        // the block is registered and the caller's registers point at it, so marking reads its first word,
        // which is malloc's free-list data until then.
        else *(void**)p = NULL;
    }
    if (g.large_count == g.large_capacity) {
        size_t capacity = g.large_capacity ? g.large_capacity * 2 : 64;
#if CSRT_HAS_CONCURRENT
        // The marker may be reading the old table: copy, and free the old one after the cycle.
        LargeObject* grown = malloc(capacity * sizeof *grown);
        if (!grown) { free(p); return NULL; }
        if (g.large_count) memcpy(grown, g.large, g.large_count * sizeof *grown);
        if (g.large && csrt_gc_marking) defer_free(g.large);
        else free(g.large);
#else
        LargeObject* grown = realloc(g.large, capacity * sizeof *grown);
        if (!grown) { free(p); return NULL; }
#endif
        g.large = grown;
        g.large_capacity = capacity;
    }
#if CSRT_HAS_NURSERY
    g.large[g.large_count++] = (LargeObject){ p, size, false, false };
#elif CSRT_HAS_CONCURRENT
    g.large[g.large_count++] = (LargeObject){ p, size, csrt_gc_marking != 0 };  // black during a cycle
#else
    g.large[g.large_count++] = (LargeObject){ p, size, false };
#endif
    note_range((uintptr_t)p, (uintptr_t)p + size);
    return p;
}

#if CSRT_HAS_NURSERY
__attribute__((noinline, cold)) static void* alloc_small_slow(uint32_t cls) {
    if (!g.initialized) csrt_gc_init();
    gc_lock();
    SizeClass* sc = &g.classes[cls];
    AllocCache* cache = &tl_cache[cls];
    bool collected = false;
    void* result = NULL;
    // Stress collections are minors too (a major every 32): they exercise the write barriers.
    if (g.stress && g.enabled && ++g.stress_count % g.stress == 0 && !g.collecting) { collect(); collected = true; }
    for (;;) {
        if (g.enabled && !g.collecting) {
            if (g.nursery) {
                if (g.allocated_since_gc >= g.nursery_trigger) { collect(); collected = true; }
            } else if (g.allocated_since_gc >= g.trigger) {
                collect(); collected = true;
            }
        }
        if (cache->free_bits || claim_word(sc, cache, cls)) break;
        if (!g.enabled || collected) goto out;
        g.force_major = true;
        collect();
        collected = true;
    }
    size_t heap = g.live_bytes + g.allocated_since_gc;
    if (heap > g.peak_heap) g.peak_heap = heap;
    uint64_t bits = cache->free_bits;
    if (g.stress) {
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

static void* alloc_large_locked(size_t size, bool clear) {
    if (g.enabled) {
        if (g.stress && ++g.stress_count % g.stress == 0) collect();
        else if (g.allocated_since_gc >= (g.nursery ? g.nursery_trigger : g.trigger)) collect();
    }
    void* result = alloc_large(size, clear);
    if (!result && g.enabled) {
        g.force_major = true;
        collect();
        result = alloc_large(size, clear);
    }
    if (result) {
        size_t charged = (size + 15) & ~(size_t)15;
        g.allocated_since_gc += charged;
        g.total_allocated += charged;
        size_t heap = g.live_bytes + g.allocated_since_gc;
        if (heap > g.peak_heap) g.peak_heap = heap;
        // Large objects are born old, and the runtime may fill one before any barrier runs.
        g.large_dirty = true;
    }
    return result;
}
#else
// Keep the single-generation shapes identical to main: a shared `!g.collecting` wrapper around the
// trigger check grew alloc_small_slow by ~60 bytes on aarch64 and tipped typeinfo's RX LOAD over a
// 64 KiB file boundary (+4 KiB file size).
__attribute__((noinline, cold)) static void* alloc_small_slow(uint32_t cls) {
    if (!g.initialized) csrt_gc_init();
    gc_lock();
    SizeClass* sc = &g.classes[cls];
    AllocCache* cache = &tl_cache[cls];
    bool collected = false;
    void* result = NULL;
    if (g.stress && g.enabled && ++g.stress_count % g.stress == 0 && !g.collecting) { collect(); collected = true; }
    for (;;) {
#if CSRT_HAS_CONCURRENT
        if (g.enabled && g.allocated_since_gc >= cm_threshold() && !g.collecting && !concurrent_trigger()) { collect(); collected = true; }
#else
        if (g.enabled && g.allocated_since_gc >= g.trigger && !g.collecting) { collect(); collected = true; }
#endif
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

static void* alloc_large_locked(size_t size, bool clear) {
#if CSRT_HAS_CONCURRENT
    if (g.enabled && g.stress && ++g.stress_count % g.stress == 0) collect();
    else if (g.enabled && g.allocated_since_gc >= cm_threshold() && !g.collecting && !concurrent_trigger()) collect();
#else
    if (g.enabled && (g.allocated_since_gc >= g.trigger || (g.stress && ++g.stress_count % g.stress == 0))) collect();
#endif
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
#endif

__attribute__((noinline)) static void* alloc_large_slow(size_t size, bool clear) {
    if (!g.initialized) csrt_gc_init();
    gc_lock();
    void* result = alloc_large_locked(size, clear);
    gc_unlock();
    return result;
}

static inline void* gc_alloc(size_t size, bool clear) {
    if (__builtin_expect(size > LARGE_LIMIT, 0)) {
        void* large = alloc_large_slow(size, clear);
        FRESH(large);
        return large;
    }
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
    FRESH(slot);
    if (clear) memset(slot, 0, k_size_classes[cls]);  // C# semantics: fresh objects are zeroed
    return slot;
}

void* csrt_gc_alloc(size_t size) { return gc_alloc(size, true); }

void* csrt_gc_alloc_noclear(size_t size) { return gc_alloc(size, false); }

/// GC.CollectionCount(generation): gen 0 counts every collection; higher gens count majors only
/// (majors == all collections when the nursery is not compiled in).
int64_t csrt_gc_collection_count(int32_t generation) {
    gc_lock();
#if CSRT_HAS_NURSERY
    int64_t n = generation <= 0 ? (int64_t)g.collections : (int64_t)g.major_collections;
#else
    (void)generation;
    int64_t n = (int64_t)g.collections;
#endif
    gc_unlock();
    return n;
}

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
    // Between collections the table is almost sorted (survivors stay in order, new objects are
    // appended, malloc mostly hands out rising addresses): then an insertion sort is a few passes
    // over the table, where the heapsort took ~n log n swaps (36,000 large objects in a server).
    size_t descents = 0;
    for (size_t i = 1; i < n && descents <= 64; i++) descents += a[i].start < a[i - 1].start;
    if (descents <= 64) {
        for (size_t i = 1; i < n; i++) {
            if (a[i].start >= a[i - 1].start) continue;
            LargeObject t = a[i];
            size_t j = i;
            while (j > 0 && a[j - 1].start > t.start) { a[j] = a[j - 1]; j--; }
            a[j] = t;
        }
        return;
    }
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

/// The slot an offset into a chunk's slot area falls in, without a division: exact for offsets below
/// 2^18 (CHUNK_SIZE) and sizes up to 2048 (the error of the magic multiply stays under 1/size).
static inline __attribute__((always_inline)) size_t slot_of(const Chunk* c, size_t offset) {
    return (size_t)(((uint64_t)offset * c->slot_magic) >> 32);
}

/// If `word` points into a live, unmarked heap object, marks it and queues it for scanning.
static inline __attribute__((always_inline)) void mark_candidate(uintptr_t word) {
    if (word < g.heap_min || word >= g.heap_max) return;

    uintptr_t base = word & ~(CHUNK_SIZE - 1);
#if CSRT_HAS_CONCURRENT
    if (chunk_set_contains_marker(base)) {
#else
    if (chunk_set_contains(base)) {
#endif
        Chunk* c = (Chunk*)base;
#if CSRT_HAS_NURSERY
        if (g.minor && !c->young) return;
#endif
        size_t offset = word - base;
        if (c->object_size == 0 || offset < CHUNK_HEADER) return;
        size_t index = slot_of(c, offset - CHUNK_HEADER);
        if (index >= c->slots || !bit_get(c->allocated, index) || bit_get(c->marked, index)) return;
#if CSRT_HAS_CONCURRENT
        // Mutators set mark bits too while a cycle runs (allocation is black, claim_word): atomically
        // then, and with a plain store in stop-the-world collections (a locked OR per object cost
        // single-threaded allocation benchmarks several % of their wall time).
        if (__builtin_expect(cm_active, 0)) {
            uint64_t bit = (uint64_t)1 << (index & 63);
            if (__atomic_fetch_or(&c->marked[index >> 6], bit, __ATOMIC_RELAXED) & bit) return;
        } else bit_set(c->marked, index);
#else
        bit_set(c->marked, index);
#endif
        mark_push((uint8_t*)base + CHUNK_HEADER + index * c->object_size, c->object_size);
        return;
    }

#if CSRT_HAS_NURSERY
    // Large objects are old: a minor collection ignores them as mark roots (cards scan their bodies).
    if (g.minor) return;
#endif

    // Large objects: binary search the (sorted) table; interior pointers count.
#if CSRT_HAS_CONCURRENT
    // The table the cycle sorted: mutators may append to (or regrow) g.large while the marker reads it.
    LargeObject* table = cm_large;
    size_t lo = 0, hi = cm_large_count;
#else
    LargeObject* table = g.large;
    size_t lo = 0, hi = g.large_count;
#endif
    while (lo < hi) {
        size_t mid = (lo + hi) / 2;
        LargeObject* obj = &table[mid];
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
#if CSRT_HAS_CONCURRENT
    if (g.large_count - g.large_sorted > 64 && !csrt_gc_marking) {
        sort_large_appended(g.large, g.large_sorted, g.large_count);
#else
    if (g.large_count - g.large_sorted > 64) {
        sort_large(g.large, g.large_count);
#endif
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
                size_t index = (offset - CHUNK_HEADER) / c->object_size;
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

static inline __attribute__((always_inline)) void scan_object(uint8_t* start, size_t size) {
    // Word 0 is the method-table pointer (static data, never a heap reference); strings and
    // arrays of primitives hold no references, so they have nothing to scan at all.
    // (The compiler also emits element-typed array method tables, e.g. "System.Int32[]", with the flag.)
    const csrt_MethodTable* mt = *(const csrt_MethodTable* const*)start;
    if (mt && (mt->flags & CSRT_MT_NO_REFERENCES)) return;
    scan_range(start + sizeof(void*), start + size);
}

/// Marks everything reachable from the mark stack. Objects popped go through a small FIFO and are
/// prefetched when they enter it, so each is in cache by the time it is scanned: the marker is
/// bound by misses on the objects it visits (OpenJDK's and Boehm's prefetch-on-grey).
#define MARK_PREFETCH 8
static void drain(void) {
    struct { uint8_t* start; size_t size; } fifo[MARK_PREFETCH];
    size_t head = 0, filled = 0;
    for (;;) {
        while (filled < MARK_PREFETCH && g.mark_count) {
            g.mark_count--;
            size_t slot = (head + filled) % MARK_PREFETCH;
            fifo[slot].start = g.mark_stack[g.mark_count].start;
            fifo[slot].size = g.mark_stack[g.mark_count].size;
            __builtin_prefetch(fifo[slot].start);
            filled++;
        }
        if (!filled) return;
        uint8_t* start = fifo[head].start;
        size_t size = fifo[head].size;
        head = (head + 1) % MARK_PREFETCH;
        filled--;
        scan_object(start, size);
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
typedef struct { uint8_t* start; size_t size; } MarkItem;
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
        size_t index = (offset - CHUNK_HEADER) / c->object_size;
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
        size_t index = (start - base - CHUNK_HEADER) / c->object_size;
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
        MarkItem it = { g.mark_stack[i].start, g.mark_stack[i].size };
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
    // From where the collection was entered (csrt_gc_collect_now saved the caller's registers there),
    // not from here: the collector's own frames below it hold only its values, and their unwritten
    // slots still hold whatever the program's returned calls left there, which kept whole dead
    // structures alive (a 50 MB list built by a method that had returned, in tests/samples/833).
    const void* top = g.own_stack_top;
#endif
    scan_range(top, bottom);
}

static void scan_roots(void) {
    for (size_t i = 0; i < g.segment_count; i++) scan_range((const void*)g.segments[i].lo, (const void*)g.segments[i].hi);
    for (csrt_GcThread* t = g.threads; t; t = t->next) {
        mark_candidate((uintptr_t)t->managed);  // e.g. the Thread object of a starting thread
        if (t->state != THREAD_RUNNING) continue;
        if (t->tls_hi) {
            // Not the thread's allocation caches (tl_cache, in its TLS block): a cache's word_base is the
            // first slot of the bitmap word it claimed, whatever object sits there, and as a root it kept
            // that object and everything it reaches alive (one per size class and thread).
            uintptr_t cache = (uintptr_t)t->cache, cache_end = cache + sizeof tl_cache;
            if (cache >= t->tls_lo && cache_end <= t->tls_hi) {
                scan_range((const void*)t->tls_lo, (const void*)cache);
                scan_range((const void*)cache_end, (const void*)t->tls_hi);
            } else scan_range((const void*)t->tls_lo, (const void*)t->tls_hi);
        }
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
// ---------------------------------------------------------------------------------------------
// Idle trimming
// ---------------------------------------------------------------------------------------------
// Sweeping never unmaps a chunk: the heap stays at its peak, which until the next collection is what
// the program needs again. A server that went quiet after start-up keeps that peak resident, though:
// TechEmpower's Platform entry fills its cache with 37 MiB of allocations, one collection leaves 8 MiB
// live, and 31 MiB of chunks stayed resident at idle (NativeAOT, which compacts, kept 16 MiB). Once the
// allocation rate falls below TRIM_QUIET_BYTES in half a second, the timer thread gives the pages that
// hold no allocated slot back to the OS. Free slots are never read (marking follows allocated slots; a
// stale pointer into a free slot reads its first word, which reads as zero on a given-back page, a null
// method table that marking skips) and are zeroed when allocated, so the pages may come back zeroed or,
// on macOS, unchanged. Under load the allocation rate keeps it from ever trimming, so no allocation pays a
// page fault for it. Only programs with the thread pool's timer thread link this (servers, async code).
#if !defined(__wasi__)
#define TRIM_QUIET_BYTES ((size_t)1024 * 1024)

static bool release_pages(uint8_t* from, uint8_t* to) {
    if (from >= to) return true;
#if defined(_WIN32)
    // Decommit + recommit: the range stays usable and reads as zero, but holds no memory until touched.
    return VirtualFree(from, (size_t)(to - from), MEM_DECOMMIT) && VirtualAlloc(from, (size_t)(to - from), MEM_COMMIT, PAGE_READWRITE);
#elif defined(__APPLE__)
    return madvise(from, (size_t)(to - from), MADV_FREE_REUSABLE) == 0;  // Darwin's MADV_DONTNEED frees nothing
#else
    return madvise(from, (size_t)(to - from), MADV_DONTNEED) == 0;
#endif
}

/// An empty chunk's slot pages go back to the OS (its header, with the bitmaps and links, stays). Both the
/// collector's surplus release and the idle trim use it; `released` keeps a chunk from being released twice
/// and tells claim_word that, where RELEASED_PAGES_READ_ZERO, its slots need no clearing when it is reused.
static bool release_chunk(Chunk* c) {
    if (c->released) return true;
    if (!release_pages((uint8_t*)c + CHUNK_HEADER, (uint8_t*)c + CHUNK_SIZE)) return false;
    c->released = 1;
    g.released_chunks++;
    return true;
}

/// Any allocated slot among [first, last]?
static bool any_allocated(const Chunk* c, size_t first, size_t last) {
    for (size_t w = first >> 6; w <= last >> 6; w++) {
        uint64_t bits = c->allocated[w];
        if (w == first >> 6) bits &= ~(uint64_t)0 << (first & 63);
        if (w == last >> 6 && (last & 63) != 63) bits &= ((uint64_t)1 << ((last & 63) + 1)) - 1;
        if (bits) return true;
    }
    return false;
}

/// Gives back every page of slot memory that holds no allocated slot. Caller holds g.lock (so no
/// collection or claim runs); slots claimed into a thread's allocation cache count as allocated.
static void trim_free_pages(void) {
    const size_t page = 4096;
    for (Chunk* c = g.chunks; c; c = c->next) {
        uint8_t* data = (uint8_t*)c + CHUNK_HEADER;
        if (c->object_size == 0) {
            release_chunk(c);
            continue;
        }
        if (c->live == c->slots) continue;
        size_t size = c->object_size, used = (size_t)c->slots * size;
        uint8_t* run = NULL;  // start of the current run of free pages
        for (size_t off = 0; off < used; off += page) {
            size_t end = off + page < used ? off + page : used;
            bool free_page = end - off == page && !any_allocated(c, off / size, (end - 1) / size);
            if (free_page && !run) run = data + off;
            if (!free_page && run) { release_pages(run, data + off); run = NULL; }
        }
        if (run) release_pages(run, data + used);
    }
}

/// Called by the task scheduler's timer thread (async.c) every TRIM_PERIOD_MS: trims once the allocation
/// of the last period was under TRIM_QUIET_BYTES and something was freed since the last trim.
void csrt_gc_idle_trim(void) {
    static size_t seen, trimmed_at;
    size_t now = __atomic_load_n(&g.total_allocated, __ATOMIC_RELAXED);
    size_t recent = now - seen;
    seen = now;
    // Before the first collection no slot was ever freed; after a trim, only allocation frees more.
    if (recent >= TRIM_QUIET_BYTES || now == trimmed_at || __atomic_load_n(&g.collections, __ATOMIC_RELAXED) == 0) return;
    gc_lock();
    trim_free_pages();
    gc_unlock();
#if defined(__GLIBC__)
    malloc_trim(0);  // large objects freed by sweeps are glibc's free memory
#endif
    trimmed_at = now;
}
#endif

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

#if CSRT_HAS_NURSERY
/// Scan dirty old chunks (and large objects) for pointers into the nursery.
CSRT_GC_NO_SANITIZE static void scan_cards(void) {
    for (Chunk* c = g.chunks; c; c = c->next) {
        if (c->young || c->object_size == 0) { c->dirty = 0; continue; }
        if (c->dirty) {
            c->dirty = 0;
            scan_range((const uint8_t*)c + CHUNK_HEADER, (const uint8_t*)c + CHUNK_SIZE);
            continue;
        }
        if (!c->remembered) continue;
        // Only the frames: the rest of the chunk is clean. The flag stays while one is allocated here.
        bool any = false;
        size_t words = (c->slots + 63) / 64;
        for (size_t w = 0; w < words; w++)
            for (uint64_t bits = c->allocated[w]; bits; bits &= bits - 1) {
                const uint8_t* start = (const uint8_t*)c + CHUNK_HEADER + (w * 64 + (size_t)__builtin_ctzll(bits)) * c->object_size;
                if (!csrt_gc_remembered_object(start)) continue;
                any = true;
                scan_range(start + sizeof(void*), start + c->object_size);
            }
        c->remembered = any;
    }
    if (!g.large_dirty && !g.large_remembered) return;
    bool all = g.large_dirty, any = false;
    g.large_dirty = false;
    for (size_t i = 0; i < g.large_count; i++) {
        LargeObject* obj = &g.large[i];
        const csrt_MethodTable* mt = *(const csrt_MethodTable* const*)obj->start;
        if (mt && (mt->flags & CSRT_MT_NO_REFERENCES)) continue;
        bool frame = csrt_gc_remembered_object(obj->start);
        any |= frame;
        if (all || frame) scan_range(obj->start + sizeof(void*), obj->start + obj->size);
    }
    g.large_remembered = any;
}
#endif

#if CSRT_HAS_NURSERY
static size_t sweep(bool major) {
    size_t live = 0;
    for (size_t cls = 0; cls < NUM_CLASSES; cls++) {
        g.classes[cls].chunk = NULL;
        g.classes[cls].partial = NULL;
    }
    g.empty = NULL;

    for (Chunk* c = g.chunks; c; c = c->next) {
        if (c->object_size == 0) { c->young = 0; c->next_free = g.empty; g.empty = c; continue; }
        if (!major && !c->young) {
            uint32_t still_live = 0;
            size_t words = (c->slots + 63) / 64;
            for (size_t w = 0; w < words; w++) still_live += (uint32_t)__builtin_popcountll(c->allocated[w]);
            c->live = still_live;
            live += (size_t)still_live * c->object_size;
            continue;
        }
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
            c->object_size = 0;
            c->young = 0;
            c->next_free = g.empty;
            g.empty = c;
            continue;
        }
        // Survivors are old: after a minor this turns old→young edges into old→old so cards can
        // clear. Promoted chunks are not allocated into again until a later major frees them.
        c->young = 0;
        c->dirty = 0;
        if (major && still_live < c->slots) {
            SizeClass* sc = &g.classes[c->size_class];
            c->next_free = sc->partial;
            sc->partial = c;
        }
        live += (size_t)still_live * c->object_size;
    }

    if (major) {
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
    } else {
        for (size_t i = 0; i < g.large_count; i++) {
            g.large[i].marked = false;
            live += g.large[i].size;
        }
    }
    return live;
}
#else
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
            c->object_size = 0;
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
#endif

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
/// During a minor collection, old objects are treated as marked (they are not re-traced).
static bool weak_is_marked(uintptr_t word) {
    if (word < g.heap_min || word >= g.heap_max) return true;
    uintptr_t base = word & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains(base)) {
        Chunk* c = (Chunk*)base;
#if CSRT_HAS_NURSERY
        if (g.minor && !c->young) return true;
#endif
        size_t offset = word - base;
        if (c->object_size == 0 || offset < CHUNK_HEADER) return false;
        size_t index = (offset - CHUNK_HEADER) / c->object_size;
        return index < c->slots && bit_get(c->allocated, index) && bit_get(c->marked, index);
    }
#if CSRT_HAS_NURSERY
    if (g.minor) return true;  // large objects are old
#endif
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

/// The allocation budget follows what collecting costs. It starts at the throughput setting (twice the
/// live heap, at least min_trigger) and steps toward the live heap and half of min_trigger while pauses
/// take under 2 % of the time, back up past 4 %. Allocation-bound programs (binarytrees, churn: GC is
/// several % of their time) keep the large budget; a server whose request garbage is cheap to collect
/// runs near its live size. A fixed growth of 100 % with 16 MiB cost binarytrees +26 % and churn +17 %
/// executed instructions, and took the TechEmpower server from 72 to 52-56 MiB (NativeAOT: 65).
static void adapt_budget(struct timespec start, struct timespec end) {
    double now = (double)end.tv_sec + (double)end.tv_nsec / 1e9;
    double pause = now - ((double)start.tv_sec + (double)start.tv_nsec / 1e9);
    double since = now - g.last_gc_end;
    bool first = g.last_gc_end == 0;
    g.last_gc_end = now;
    if (first || since <= pause) return;
    double overhead = pause / since;
    size_t floor_min = g.min_trigger / 2, step = g.min_trigger / 8;
    if (overhead < 0.02) {
        if (g.growth_pct > 100) g.growth_pct -= 25;
        g.budget_floor = g.budget_floor > floor_min + step ? g.budget_floor - step : floor_min;
    } else if (overhead > 0.04) {
        g.growth_pct = g.growth_pct + 50 < 200 ? g.growth_pct + 50 : 200;
        g.budget_floor = g.budget_floor + 2 * step < g.min_trigger ? g.budget_floor + 2 * step : g.min_trigger;
    }
}

/// Empty chunks beyond what the next allocation budget can fill give their pages back to the OS, so the
/// process shrinks after a burst: a server's heap peaks under its warm-up's connections and, without
/// this, its RSS never came down again (TechEmpower JSON: 52 MiB at every later load level, against
/// NativeAOT's 30-44). In a steady state the empty chunks a sweep finds are about the budget, so none
/// are released and none fault in again (release_chunk; claim_word skips clearing a released chunk where
/// its pages come back zeroed).
static void release_surplus_chunks(void) {
#if !defined(__wasi__)
    size_t keep = g.trigger / CHUNK_SIZE + 4, committed = 0;
    for (Chunk* c = g.empty; c; c = c->next_free) {
        if (c->released || ++committed <= keep) continue;
        if (!release_chunk(c)) return;
    }
#endif
}

#if CSRT_HAS_CONCURRENT
// ---------------------------------------------------------------------------------------------
// Concurrent marking (docs/adr/0020-concurrent-marking.md)
// ---------------------------------------------------------------------------------------------
// A cycle: the allocating thread that reaches the trigger stops the world for the initial mark
// (roots, the threads' allocation words made black, the large-object table sorted and snapshotted),
// sets csrt_gc_marking and wakes the marker thread. The marker drains the mark stack while the
// mutators run; their barrier logs what they store into the heap (values, and ranges for copies)
// into per-thread buffers, which the marker takes as they fill. Objects allocated during the cycle
// are black. When the marker runs dry it stops the world for the remark: the threads' partial
// buffers, the roots again, every live coroutine frame (LLVM's coroutine lowering writes frames
// without barriers), the ephemerons and weak handles, then the sweep. A mutator that allocates
// another full budget before the cycle ends waits for it.
#define LOG_ENTRIES 512
#define LOG_RANGE_TAG ((uintptr_t)1)  // an entry with the low bit set is a range start; the next is its length
#define LOG_FRAME_TAG ((uintptr_t)2)  // else bit 1: a coroutine frame (or its task) to rescan whole
#define RUNNING_FRAMES 64

typedef struct LogBuffer { struct LogBuffer* next; size_t count; uintptr_t entries[LOG_ENTRIES]; } LogBuffer;
// A thread's log, and the coroutine frames it is running: resumed (csrt_coro_resume) or being created
// (an async method's or iterator's first run), not yet suspended. LLVM's coroutine lowering writes
// frames without barriers, so a frame is logged when it suspends, and the remark rescans the frames
// still running then. More than RUNNING_FRAMES nested ones (overflow): the remark rescans every frame.
typedef struct ThreadLog {
    struct ThreadLog* next;
    LogBuffer* current;
    void* running[RUNNING_FRAMES];
    uint32_t depth;
    bool overflow;
} ThreadLog;

static pthread_mutex_t cm_lock = PTHREAD_MUTEX_INITIALIZER;  // the lists below
static pthread_cond_t cm_start = PTHREAD_COND_INITIALIZER;   // a cycle began (the marker waits)
static pthread_cond_t cm_done = PTHREAD_COND_INITIALIZER;    // a cycle ended (waits on g.lock)
static LogBuffer* cm_full;        // filled buffers for the marker
static LogBuffer* cm_spare;       // empty buffers to reuse (no malloc while the world is stopped)
static ThreadLog* cm_logs;        // every thread's log (threads that exited keep theirs: it is small)
static CSRT_TLS ThreadLog* tl_log;
static bool cm_marker_started;
static uint64_t cm_cycles;
static double cm_pause_max_ms, cm_pause_total_ms;
// The longest remark's phases (CSRT_GC_STATS=1): buffers, roots, frames, drain, weak+sweep; and the longest initial mark.
static double cm_phase_ms[5], cm_initial_max_ms, cm_remark_max_ms;
static double cm_pauses[4096];
static size_t cm_pause_count;
static void* cm_old_tables[64];   // large-object tables replaced during the cycle (the marker may read them)
static size_t cm_old_count;

static void defer_free(void* table) {
    if (cm_old_count < sizeof cm_old_tables / sizeof cm_old_tables[0]) cm_old_tables[cm_old_count++] = table;
    // More than 64 doublings of the table in one cycle cannot happen (2^64 entries); keep it anyway.
}

static LogBuffer* log_buffer_new(void) {
    LogBuffer* b = NULL;
    pthread_mutex_lock(&cm_lock);
    if (cm_spare) { b = cm_spare; cm_spare = b->next; }
    pthread_mutex_unlock(&cm_lock);
    if (!b) {
        b = malloc(sizeof *b);
        if (!b) { fputs("csrt: out of memory (gc metadata)\n", stderr); abort(); }
    }
    b->next = NULL;
    b->count = 0;
    return b;
}

static ThreadLog* thread_log(void) {
    ThreadLog* t = tl_log;
    if (t) return t;
    t = malloc(sizeof *t);
    if (!t) { fputs("csrt: out of memory (gc metadata)\n", stderr); abort(); }
    t->current = log_buffer_new();
    t->depth = 0;
    t->overflow = false;
    pthread_mutex_lock(&cm_lock);
    t->next = cm_logs;
    cm_logs = t;
    pthread_mutex_unlock(&cm_lock);
    tl_log = t;
    return t;
}

/// Hands a full buffer to the marker and starts a new one. The entries were all logged before stores
/// that came before this call in program order, so the marker reads finished stores.
static void log_flush(ThreadLog* t) {
    LogBuffer* full = t->current;
    LogBuffer* fresh = log_buffer_new();
    pthread_mutex_lock(&cm_lock);
    full->next = cm_full;
    cm_full = full;
    pthread_mutex_unlock(&cm_lock);
    t->current = fresh;
}

__attribute__((noinline, cold)) void csrt_gc_log(void* value) {
    if (!value) return;
    ThreadLog* t = thread_log();
    if (t->current->count >= LOG_ENTRIES) log_flush(t);
    LogBuffer* b = t->current;
    b->entries[b->count] = (uintptr_t)value & ~LOG_RANGE_TAG;
    __atomic_store_n(&b->count, b->count + 1, __ATOMIC_RELEASE);
}

__attribute__((noinline, cold)) void csrt_gc_log_range(void* start, size_t bytes) {
    if (!start || !bytes) return;
    ThreadLog* t = thread_log();
    if (t->current->count + 2 > LOG_ENTRIES) log_flush(t);
    LogBuffer* b = t->current;
    b->entries[b->count] = (uintptr_t)start | LOG_RANGE_TAG;
    b->entries[b->count + 1] = (uintptr_t)bytes;
    __atomic_store_n(&b->count, b->count + 2, __ATOMIC_RELEASE);
}

/// Scans the heap object `p` points into, whole (a coroutine frame, or the task holding one), if it is
/// allocated; unmarked ones are scanned too (harmless: marking them later scans them again).
CSRT_GC_NO_SANITIZE static void scan_object_at(uintptr_t p) {
    if (p < g.heap_min || p >= g.heap_max) return;
    uintptr_t base = p & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains_marker(base)) {
        Chunk* c = (Chunk*)base;
        size_t offset = p - base;
        if (c->object_size == 0 || offset < CHUNK_HEADER) return;
        size_t index = slot_of(c, offset - CHUNK_HEADER);
        if (index >= c->slots || !bit_get(c->allocated, index)) return;
        const uint8_t* start = (const uint8_t*)base + CHUNK_HEADER + index * c->object_size;
        scan_range(start + sizeof(void*), start + c->object_size);
        return;
    }
    size_t lo = 0, hi = cm_large_count;
    while (lo < hi) {
        size_t mid = (lo + hi) / 2;
        LargeObject* obj = &cm_large[mid];
        if (p < (uintptr_t)obj->start) hi = mid;
        else if (p >= (uintptr_t)obj->start + obj->size) lo = mid + 1;
        else { scan_range(obj->start + sizeof(void*), obj->start + obj->size); return; }
    }
}

CSRT_GC_NO_SANITIZE static void log_process(const LogBuffer* b, size_t count) {
    for (size_t i = 0; i < count; i++) {
        uintptr_t e = b->entries[i];
        if (e & LOG_RANGE_TAG) {
            const uint8_t* start = (const uint8_t*)(e & ~LOG_RANGE_TAG);
            size_t bytes = i + 1 < count ? (size_t)b->entries[++i] : 0;
            scan_range(start, start + bytes);
        } else if (e & LOG_FRAME_TAG) {
            uintptr_t frame = e & ~LOG_FRAME_TAG;
            mark_candidate(frame);
            scan_object_at(frame);
        } else mark_candidate(e);
    }
}

/// The first run of an async method that returns nothing (async void) has ended: its frame is the top.
void csrt_gc_frame_exit_top(void) {
    ThreadLog* t = tl_log;
    if (!t || t->depth == 0) return;
    if (t->depth <= RUNNING_FRAMES) csrt_gc_frame_exit(t->running[t->depth - 1]);
    else __atomic_store_n(&t->depth, t->depth - 1, __ATOMIC_RELEASE);
}

/// A coroutine frame starts running on this thread: csrt_coro_resume, or its creation (csrt_coro_alloc*).
void csrt_gc_frame_enter(void* frame) {
    ThreadLog* t = thread_log();
    if (t->depth < RUNNING_FRAMES) t->running[t->depth] = frame;
    else t->overflow = true;
    __atomic_store_n(&t->depth, t->depth + 1, __ATOMIC_RELEASE);
}

/// ...and suspends, or finishes: logged for the marker while a cycle runs (it was written without
/// barriers). Frames above it that never exited (an exception unwound through them) are popped too; a
/// frame not on the stack (a first run whose frame LLVM put on the caller's stack) pops nothing.
void csrt_gc_frame_exit(void* frame) {
    ThreadLog* t = tl_log;
    if (!t) return;
    // A frame created by csrt_coro_alloc* is entered with its object and exited with what its first
    // run returns: the task (the same object) or the frame (16 bytes into it).
    uint32_t depth = t->depth, top = depth < RUNNING_FRAMES ? depth : RUNNING_FRAMES;
    for (uint32_t i = top; i > 0; i--)
        if ((uintptr_t)frame - (uintptr_t)t->running[i - 1] < 64) {
            depth = depth > RUNNING_FRAMES ? depth : i - 1;
            break;
        }
    __atomic_store_n(&t->depth, depth, __ATOMIC_RELEASE);
    if (depth == 0) t->overflow = false;
    if (!csrt_gc_marking || !frame) return;
    if (t->current->count >= LOG_ENTRIES) log_flush(t);
    LogBuffer* b = t->current;
    b->entries[b->count] = ((uintptr_t)frame & ~(LOG_RANGE_TAG | LOG_FRAME_TAG)) | LOG_FRAME_TAG;
    __atomic_store_n(&b->count, b->count + 1, __ATOMIC_RELEASE);
}

/// The marker's side of the full buffers: true when it took one.
static bool take_full_buffer(void) {
    pthread_mutex_lock(&cm_lock);
    LogBuffer* b = cm_full;
    if (b) cm_full = b->next;
    pthread_mutex_unlock(&cm_lock);
    if (!b) return false;
    log_process(b, b->count);
    b->count = 0;
    pthread_mutex_lock(&cm_lock);
    b->next = cm_spare;
    cm_spare = b;
    pthread_mutex_unlock(&cm_lock);
    return true;
}

/// Every live coroutine frame (and task: an async method's frame is inside its task), from the
/// remembered chunks and large objects. Unmarked ones are not live yet; marking them later scans them.
CSRT_GC_NO_SANITIZE static void rescan_frames(void) {
    for (Chunk* c = g.chunks; c; c = c->next) {
        if (!c->remembered || c->object_size == 0) continue;
        size_t words = (c->slots + 63) / 64;
        for (size_t w = 0; w < words; w++)
            for (uint64_t bits = c->allocated[w] & c->marked[w]; bits; bits &= bits - 1) {
                const uint8_t* start = (const uint8_t*)c + CHUNK_HEADER + (w * 64 + (size_t)__builtin_ctzll(bits)) * c->object_size;
                if (csrt_gc_remembered_object(start)) scan_range(start + sizeof(void*), start + c->object_size);
            }
    }
    if (!g.large_remembered) return;
    for (size_t i = 0; i < cm_large_count; i++) {
        LargeObject* obj = &cm_large[i];
        if (obj->marked && csrt_gc_remembered_object(obj->start)) scan_range(obj->start + sizeof(void*), obj->start + obj->size);
    }
}

static double elapsed_ms(struct timespec a, struct timespec b) {
    return (double)(b.tv_sec - a.tv_sec) * 1e3 + (double)(b.tv_nsec - a.tv_nsec) / 1e6;
}

static void note_pause(struct timespec a, struct timespec b) {
    double ms = elapsed_ms(a, b);
    if (cm_pause_count < sizeof cm_pauses / sizeof cm_pauses[0]) cm_pauses[cm_pause_count++] = ms;
    cm_pause_total_ms += ms;
    if (ms > cm_pause_max_ms) cm_pause_max_ms = ms;
    g.total_gc_seconds += ms / 1e3;
}

static void* marker_main(void* arg);
extern int32_t csrt_processor_count(void);
void csrt_os_thread_name(const char* name);

/// Whether a collection runs concurrently: a heap worth it, threads, and no stress mode.
static bool concurrent_wanted(void) {
    // CSRT_GC_CONCURRENT=0 turns cycles off; CSRT_GC_CONCURRENT_MIN_MB (default 8) is the live heap
    // below which collections stay stop-the-world (tests set 0 to run cycles on small heaps).
    static int setting = -2;
    static size_t min_live;
    if (setting == -2) {
        const char* e = getenv("CSRT_GC_CONCURRENT");
        setting = e ? atoi(e) : 1;
        const char* m = getenv("CSRT_GC_CONCURRENT_MIN_MB");
        min_live = (m ? (size_t)atol(m) : 8) * 1024 * 1024;
    }
    return setting != 0 && !g.stress && g.multithreaded && csrt_processor_count() > 1 && g.live_bytes >= min_live;
}

// Sweeping after the remark, outside the pause: chunks are swept a batch at a time under g.lock, by the
// marker and by allocations that would otherwise map a new chunk. Until a chunk is swept its dead
// objects stay allocated, and no word is claimed in it: the size classes' lists hold swept chunks only.
static Chunk* cm_sweep_cursor;
static size_t cm_sweep_live;

/// Under g.lock, at the remark: start sweeping. Large objects are swept now (their table is small).
static void sweep_begin(void) {
    for (size_t cls = 0; cls < NUM_CLASSES; cls++) {
        g.classes[cls].chunk = NULL;
        g.classes[cls].partial = NULL;
    }
    g.empty = NULL;
    size_t live = 0, kept = 0, sorted_kept = 0;
    for (size_t i = 0; i < g.large_count; i++) {
        LargeObject obj = g.large[i];
        if (obj.marked) {
            obj.marked = false;
            g.large[kept++] = obj;
            if (i < cm_large_count) sorted_kept = kept;
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
    g.large_sorted = sorted_kept;
    cm_sweep_live = live;
    cm_sweep_cursor = g.chunks;
    cm_sweeping = true;
}

static void sweep_end(void) {
    cm_sweeping = false;
    g.live_bytes = cm_sweep_live;
    // The budget grows from the live heap less what the cycle allocated: that is black, survives this
    // cycle whether reachable or not, and would otherwise compound into every next budget.
    size_t base = cm_cycle_alloc < g.live_bytes / 2 ? g.live_bytes - cm_cycle_alloc : g.live_bytes / 2;
    size_t grown = base / 100 * g.growth_pct;
    g.trigger = grown > g.budget_floor ? grown : g.budget_floor;
    cm_pace();
    release_surplus_chunks();
}

/// Under g.lock: sweeps up to `chunks` chunks; false once nothing is left to sweep.
static bool sweep_some(size_t chunks) {
    if (!cm_sweeping) return false;
    for (size_t n = 0; n < chunks && cm_sweep_cursor; n++) {
        Chunk* c = cm_sweep_cursor;
        cm_sweep_cursor = c->next;
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
            c->object_size = 0;
            c->next_free = g.empty;
            g.empty = c;
            continue;
        }
        if (still_live < c->slots) {
            SizeClass* sc = &g.classes[c->size_class];
            c->next_free = sc->partial;
            sc->partial = c;
        }
        cm_sweep_live += (size_t)still_live * c->object_size;
    }
    if (!cm_sweep_cursor) { sweep_end(); return false; }
    return true;
}

static void sweep_all(void) { while (sweep_some(256)) {} }

/// The large-object table sorted for the initial mark: [0, sorted) is sorted already (sweeps keep the
/// order), so only the objects appended since are sorted, then merged into it through a scratch table
/// (mmap'd: this runs with the world stopped). The full heapsort took 5-6 ms for 56,000 objects.
static LargeObject* cm_scratch;
static size_t cm_scratch_capacity;

static void sort_large_appended(LargeObject* a, size_t sorted, size_t n) {
    if (sorted > n) sorted = 0;
    if (n == sorted) return;
    bool tail_sorted = true;
    for (size_t i = sorted + 1; i < n && tail_sorted; i++) tail_sorted = a[i - 1].start <= a[i].start;
    if (!tail_sorted) sort_large(a + sorted, n - sorted);
    if (sorted == 0 || a[sorted - 1].start <= a[sorted].start) return;
    if (cm_scratch_capacity < n) {
        size_t capacity = cm_scratch_capacity ? cm_scratch_capacity : 1024;
        while (capacity < n) capacity *= 2;
        cm_scratch = mmap_grow(cm_scratch, cm_scratch_capacity * sizeof *cm_scratch, capacity * sizeof *cm_scratch);
        cm_scratch_capacity = capacity;
    }
    size_t i = 0, j = sorted, k = 0;
    while (i < sorted && j < n) cm_scratch[k++] = a[i].start <= a[j].start ? a[i++] : a[j++];
    while (i < sorted) cm_scratch[k++] = a[i++];
    while (j < n) cm_scratch[k++] = a[j++];
    memcpy(a, cm_scratch, n * sizeof *a);
}

/// On the marker, after the sweep: sorts the large objects allocated since the table was last sorted
/// outside the lock (a copy), then merges them in under it, so the next initial mark only sorts what was
/// allocated since. Gives up if a collection or a cycle ran meanwhile.
static LargeObject* cm_presort;
static size_t cm_presort_capacity;

static void presort_large(void) {
    gc_lock();
    size_t sorted = g.large_sorted, count = g.large_count, collections = g.collections;
    if (cm_active || cm_sweeping || count - sorted < 64 || sorted > count) { gc_unlock(); return; }
    size_t tail = count - sorted;
    if (cm_presort_capacity < tail) {
        size_t capacity = cm_presort_capacity ? cm_presort_capacity : 1024;
        while (capacity < tail) capacity *= 2;
        cm_presort = mmap_grow(cm_presort, cm_presort_capacity * sizeof *cm_presort, capacity * sizeof *cm_presort);
        cm_presort_capacity = capacity;
    }
    memcpy(cm_presort, g.large + sorted, tail * sizeof *cm_presort);
    gc_unlock();
    sort_large(cm_presort, tail);
    gc_lock();
    // Only appends happen between collections: [sorted, count) still holds what was copied.
    if (!cm_active && !cm_sweeping && g.collections == collections && g.large_sorted == sorted && g.large_count >= count) {
        memcpy(g.large + sorted, cm_presort, tail * sizeof *cm_presort);
        sort_large_appended(g.large, sorted, count);  // [sorted, count) is sorted: a merge
        g.large_sorted = count;
    }
    gc_unlock();
}

/// Under g.lock: the initial mark. Returns false (nothing started) if the marker thread can't run.
/// Like csrt_gc_collect_now, it spills the caller's registers into its frame and scans the calling
/// thread's stack from there (scan_own_stack).
CSRT_GC_NO_SANITIZE __attribute__((noinline)) static bool cycle_begin(void) {
    jmp_buf registers;
    setjmp(registers);
    __builtin_unwind_init();
    volatile uintptr_t own = 0;
    const void* top = (const void*)&own;
    if ((const void*)registers < top) top = (const void*)registers;
    g.own_stack_top = top;
    if (!cm_marker_started) {
        pthread_t thread;
        if (pthread_create(&thread, NULL, marker_main, NULL) != 0) return false;
        pthread_detach(thread);
        cm_marker_started = true;
    }
    struct timespec t0, t1;
    clock_gettime(CLOCK_MONOTONIC, &t0);
    g.collecting = true;
    stop_world();
    sort_large_appended(g.large, g.large_sorted, g.large_count);
    g.large_sorted = g.large_count;
    cm_large = g.large;
    cm_large_count = g.large_count;
    cm_old_count = 0;
    __atomic_store_n(&csrt_gc_marking, 1, __ATOMIC_SEQ_CST);
    // The slots the threads' allocation words still hold are black too: they are handed out during the cycle.
    // The last one handed out is a root: a thread stopped in the inlined fast path may have taken its bit
    // out of free_bits without holding the slot's address anywhere yet, and the compiler drops the barriers
    // of stores of new objects (ConcurrentBarrierElision.cs), so it must not stay white. Slots go out
    // lowest first: the last is the highest of the claimed ones no longer free. (Gray, not black: it may
    // be an older object, with references.)
    for (csrt_GcThread* t = g.threads; t; t = t->next) {
        if (t->state != THREAD_RUNNING || !t->cache) continue;
        for (size_t cls = 0; cls < NUM_CLASSES; cls++) {
            AllocCache* ac = &((AllocCache*)t->cache)[cls];
            if (!ac->word_base) continue;
            size_t w;
            Chunk* c = cache_word(ac, &w);
            if (ac->free_bits) __atomic_fetch_or(&c->marked[w], ac->free_bits, __ATOMIC_RELAXED);
            uint64_t handed = ac->claimed & ~ac->free_bits;
            if (handed) mark_candidate((uintptr_t)(ac->word_base + (size_t)(63 - __builtin_clzll(handed)) * c->object_size));
        }
    }
    scan_roots();
    cm_alloc_at_start = g.allocated_since_gc;
    cm_active = true;
    g.collecting = false;
    resume_world();
    clock_gettime(CLOCK_MONOTONIC, &t1);
    note_pause(t0, t1);
    if (elapsed_ms(t0, t1) > cm_initial_max_ms) cm_initial_max_ms = elapsed_ms(t0, t1);
    pthread_mutex_lock(&cm_lock);
    pthread_cond_signal(&cm_start);
    pthread_mutex_unlock(&cm_lock);
    __asm__ volatile("" : : "r"(&registers), "r"(&own) : "memory");
    return true;
}

/// The remark, on the marker thread: stop the world, finish marking, sweep.
CSRT_GC_NO_SANITIZE static void cycle_finish(void) {
    gc_lock();
    struct timespec t0, t1;
    clock_gettime(CLOCK_MONOTONIC, &t0);
    g.collecting = true;
    stop_world();
    struct timespec tp[6];
    clock_gettime(CLOCK_MONOTONIC, &tp[0]);
    // The threads' partial buffers and anything flushed since the marker last looked.
    for (ThreadLog* t = cm_logs; t; t = t->next) {
        LogBuffer* b = t->current;
        size_t count = __atomic_load_n(&b->count, __ATOMIC_ACQUIRE);
        log_process(b, count);
        b->count = 0;
    }
    for (LogBuffer* b = cm_full; b; ) {
        LogBuffer* next = b->next;
        log_process(b, b->count);
        b->count = 0;
        b->next = cm_spare;
        cm_spare = b;
        b = next;
    }
    cm_full = NULL;
    drain();
    clock_gettime(CLOCK_MONOTONIC, &tp[1]);
    scan_roots();
    drain();
    clock_gettime(CLOCK_MONOTONIC, &tp[2]);
    // The frames still running: written since they were last logged, or not logged yet.
    bool overflow = false;
    for (ThreadLog* t = cm_logs; t; t = t->next) {
        uint32_t depth = t->depth < RUNNING_FRAMES ? t->depth : RUNNING_FRAMES;
        for (uint32_t i = 0; i < depth; i++) { mark_candidate((uintptr_t)t->running[i]); scan_object_at((uintptr_t)t->running[i]); }
        overflow |= t->overflow;
    }
    if (overflow) rescan_frames();
    clock_gettime(CLOCK_MONOTONIC, &tp[3]);
    drain();
    clock_gettime(CLOCK_MONOTONIC, &tp[4]);
    // The marker set bits in the table it searched; a table regrown during the cycle is a copy.
    if (g.large != cm_large)
        for (size_t i = 0; i < cm_large_count; i++) g.large[i].marked |= cm_large[i].marked;
    // No sort here: the sweep keeps the order, so the survivors of the sorted part stay sorted, and
    // what was allocated during the cycle is sorted (and merged) at the next initial mark.
    __atomic_store_n(&csrt_gc_marking, 0, __ATOMIC_SEQ_CST);
    weak_process();
    protect_caches();
    sweep_begin();
    cm_active = false;
    clock_gettime(CLOCK_MONOTONIC, &tp[5]);
    resume_world();
    clock_gettime(CLOCK_MONOTONIC, &t1);
    note_pause(t0, t1);
    if (elapsed_ms(t0, t1) >= cm_remark_max_ms) {
        cm_remark_max_ms = elapsed_ms(t0, t1);
        for (int k = 0; k < 5; k++) cm_phase_ms[k] = elapsed_ms(tp[k], tp[k + 1]);
    }
    adapt_budget(t0, t1);
    for (size_t i = 0; i < g.deferred_count; i++) free(g.deferred_free[i]);
    g.deferred_count = 0;
    for (size_t i = 0; i < cm_old_count; i++) free(cm_old_tables[i]);
    cm_old_count = 0;
    cm_cycle_alloc = g.allocated_since_gc - cm_alloc_at_start;
    g.allocated_since_gc = 0;
    g.collections++;
    cm_cycles++;
    g.collecting = false;
    gc_unlock();
    // The sweep, a batch at a time: allocations take the lock between batches.
    for (bool more = true; more; ) {
        gc_lock();
        more = sweep_some(32);
        if (!more && g.multithreaded) pthread_cond_broadcast(&cm_done);
        gc_unlock();
    }
    presort_large();
}

CSRT_GC_NO_SANITIZE static void* marker_main(void* arg) {
    (void)arg;
    csrt_os_thread_name(".NET GC");
    for (;;) {
        // Between cycles, every 20 ms: keep the large-object table sorted, so the initial mark sorts
        // only what was allocated in the last few milliseconds.
        pthread_mutex_lock(&cm_lock);
        while (!cm_active) {
            struct timespec until;
            clock_gettime(CLOCK_REALTIME, &until);
            until.tv_nsec += 20 * 1000000;
            if (until.tv_nsec >= 1000000000) { until.tv_sec++; until.tv_nsec -= 1000000000; }
            if (pthread_cond_timedwait(&cm_start, &cm_lock, &until) != 0 && !cm_active) {
                pthread_mutex_unlock(&cm_lock);
                presort_large();
                pthread_mutex_lock(&cm_lock);
            }
        }
        pthread_mutex_unlock(&cm_lock);
        // Concurrent marking and pre-cleaning: until the mark stack and the full buffers run dry.
        for (int idle = 0; idle < 2; ) {
            drain();
            if (take_full_buffer()) { idle = 0; continue; }
            idle++;
        }
        cycle_finish();
    }
    return NULL;
}

/// Under g.lock, from an allocation that reached the trigger: start a cycle, keep allocating while it
/// runs, or wait for it after another full budget. Returns false when a stop-the-world collection
/// should run instead.
static uint64_t cm_waits;
static double cm_wait_max_ms;

/// Cycles start early enough to end near the trigger: the budget less what the mutators allocated
/// during the last cycle (at most half of it). Starting at the trigger, the heap reached live + the
/// budget + a cycle's allocation, which is black and survives it. With sweep_end's discount, the GC
/// suite's peak RSS went from 256 to 207 MiB (stop-the-world: 209).
static void cm_pace(void) {
    size_t early = cm_cycle_alloc < g.trigger / 2 ? cm_cycle_alloc : g.trigger / 2;
    cm_start_at = g.trigger - early;
}

static bool concurrent_trigger(void) {
    if (cm_sweeping) sweep_all();
    if (!cm_active && g.allocated_since_gc < g.trigger && !concurrent_wanted()) return true;  // early start: cycles only
    if (cm_active) {
        if (g.allocated_since_gc < 2 * g.trigger) return true;
        // The mutators outran the marker by another full budget: wait for the cycle (counted: a stall).
        struct timespec a, b;
        clock_gettime(CLOCK_MONOTONIC, &a);
        while (cm_active) pthread_cond_wait(&cm_done, &g.lock);
        clock_gettime(CLOCK_MONOTONIC, &b);
        cm_waits++;
        if (elapsed_ms(a, b) > cm_wait_max_ms) cm_wait_max_ms = elapsed_ms(a, b);
        return true;
    }
    if (!concurrent_wanted()) return false;
    return cycle_begin();
}

/// Under g.lock: a stop-the-world collection waits for a running cycle first (GC.Collect, out of memory).
static void concurrent_wait(void) {
    while (cm_active) pthread_cond_wait(&cm_done, &g.lock);
    sweep_all();
}

/// Coroutine frames: LLVM's coroutine lowering stores into them without barriers, so the remark
/// rescans every live one. Called right after allocation.
void csrt_gc_remember(void* obj) {
    uintptr_t base = (uintptr_t)obj & ~(CHUNK_SIZE - 1);
    if (chunk_set_contains_marker(base)) ((Chunk*)base)->remembered = 1;
    else g.large_remembered = true;
}
#endif

static void collect_now(void);

/// Every collection starts here. The caller's callee-saved registers are spilled into this frame (the
/// jmp_buf, and the prologue's saves that __builtin_unwind_init forces), and the collecting thread's
/// stack is scanned from this frame up (scan_own_stack).
CSRT_GC_NO_SANITIZE __attribute__((noinline)) void csrt_gc_collect_now(void) {
    if (g.collecting) return;
#if CSRT_HAS_CONCURRENT
    // Before taking the stack's top: waiting releases the lock, and the cycle's initial mark (another
    // thread) sets g.own_stack_top for its own stack meanwhile.
    concurrent_wait();
    if (g.collecting) return;
#endif
#ifndef __wasi__
    jmp_buf registers;
    setjmp(registers);
    __builtin_unwind_init();
    volatile uintptr_t marker = 0;
    const void* top = (const void*)&marker;
    if ((const void*)registers < top) top = (const void*)registers;
    g.own_stack_top = top;
#endif
    collect_now();
#ifndef __wasi__
    // Keeps the frame (and so the saved registers) alive until the collection is over.
    __asm__ volatile("" : : "r"(&registers), "r"(&marker) : "memory");
#endif
}

__attribute__((noinline)) static void collect_now(void) {
    g.collecting = true;
#if CSRT_HAS_NURSERY
    // Stress and explicit Collect always major. Many minors without a major would never sweep
    // dead objects in promoted chunks.
    // A minor reclaims nothing old (promoted survivors, large objects, objects born in reused chunks):
    // once those reach twice the live heap of the last major, collect them.
    bool major = !g.nursery || g.force_major || g.minors_since_major >= 32 || g.live_bytes + g.allocated_since_gc >= g.major_trigger;
    g.force_major = false;
    g.minor = g.nursery && !major;
    bool was_minor = g.minor;
#endif
#if defined(CSRT_GC_PARALLEL_MARK) && !defined(__wasi__)
    // Helpers are created outside the pause. They block until parallel_drain publishes a job.
    // Parallel marking is only wired for full collections (ownership table covers every chunk).
#if CSRT_HAS_NURSERY
    int parallel = !g.minor && want_parallel() && ensure_pool() > 0;
#else
    int parallel = want_parallel() && ensure_pool() > 0;
#endif
#endif
    struct timespec t0, t1;
    clock_gettime(CLOCK_MONOTONIC, &t0);  // GC.GetTotalPauseDuration

    stop_world();
    sort_large(g.large, g.large_count);
#if CSRT_HAS_CONCURRENT
    cm_large = g.large;
    cm_large_count = g.large_count;
#endif
    scan_roots();
#if CSRT_HAS_NURSERY
    if (g.minor) scan_cards();
#endif
#if defined(CSRT_GC_PARALLEL_MARK) && !defined(__wasi__)
    if (parallel && g.mark_count > 0) parallel_drain();
    else
#endif
    drain();
    weak_process();
    protect_caches();
#if CSRT_HAS_NURSERY
    g.live_bytes = sweep(!g.minor);
    if (!g.minor) {
        for (Chunk* c = g.chunks; c; c = c->next) c->dirty = 0;
        g.large_dirty = false;
    }
    // Every survivor is old now, the chunks of the threads' claimed allocation words included. Their
    // remaining slots are still handed out (a stopped thread may be inside the lock-free fast path, so
    // its cache cannot be taken away): those objects are born old, so keep the chunks dirty until the
    // next minor has seen what was stored into them.
    for (csrt_GcThread* t = g.threads; t; t = t->next) {
        if (t->state != THREAD_RUNNING || !t->cache) continue;
        for (size_t cls = 0; cls < NUM_CLASSES; cls++) {
            AllocCache* ac = &((AllocCache*)t->cache)[cls];
            if (!ac->word_base) continue;
            size_t w;
            Chunk* c = cache_word(ac, &w);
            if (!c->young) c->dirty = 1;
        }
    }
#else
    g.live_bytes = sweep();
#endif
    resume_world();
    clock_gettime(CLOCK_MONOTONIC, &t1);
    adapt_budget(t0, t1);
    for (size_t i = 0; i < g.deferred_count; i++) free(g.deferred_free[i]);
    g.deferred_count = 0;
    g.allocated_since_gc = 0;
    size_t grown = g.live_bytes / 100 * g.growth_pct;
    g.trigger = grown > g.budget_floor ? grown : g.budget_floor;
#if CSRT_HAS_CONCURRENT
    cm_pace();
#endif
    release_surplus_chunks();
#if CSRT_HAS_NURSERY
    // Majors stay at twice the last major's live heap (live + a smaller budget would make every one major).
    if (!was_minor) g.major_trigger = 2 * g.live_bytes > g.min_trigger ? 2 * g.live_bytes : g.min_trigger;
    if (g.nursery && !was_minor) g.nursery_trigger = g.nursery_trigger_min;
    g.collections++;
    if (!was_minor) { g.major_collections++; g.minors_since_major = 0; }
    else g.minors_since_major++;
    g.minor = false;
#else
    g.collections++;
#endif

    g.total_gc_seconds += (double)(t1.tv_sec - t0.tv_sec) + (double)(t1.tv_nsec - t0.tv_nsec) / 1e9;
#if defined(CSRT_GC_PARALLEL_MARK) && !defined(__wasi__)
    if ((double)(t1.tv_sec - t0.tv_sec) + (double)(t1.tv_nsec - t0.tv_nsec) / 1e9 >= 0.003) g_mark_heavy = 1;
    // The pool is created after the pause, and only once a collection was already expensive.
    if (g_mark_heavy && !parallel && !g.stress && parallel_setting() != 0 && csrt_processor_count() > 1)
        ensure_pool();
#endif
    g.collecting = false;
}

void csrt_gc_collect(void) {
    if (!g.initialized) csrt_gc_init();
    if (!g.enabled) return;
    gc_lock();
#if CSRT_HAS_NURSERY
    g.force_major = true;
#endif
    collect();
    gc_unlock();
}

void csrt_gc_shutdown(void) {
#if defined(CSRT_GC_PARALLEL_MARK) && !defined(__wasi__)
    pool_shutdown();
#endif
    if (!g.stats) return;
#if CSRT_HAS_NURSERY
    fprintf(stderr,
            "csrt gc: %zu collections (%zu major), %.1f ms total, allocated %.1f MiB, peak heap %.1f MiB, live %.1f MiB, %zu chunks (%zu released), %zu large objects%s\n",
            g.collections, g.major_collections, g.total_gc_seconds * 1000.0, g.total_allocated / 1048576.0, g.peak_heap / 1048576.0,
            (g.live_bytes + g.allocated_since_gc) / 1048576.0, g.chunk_count, g.released_chunks, g.large_count,
            g.nursery ? ", nursery on" : "");
#else
    fprintf(stderr,
            "csrt gc: %zu collections, %.1f ms total, allocated %.1f MiB, peak heap %.1f MiB, live %.1f MiB, %zu chunks (%zu released), %zu large objects\n",
            g.collections, g.total_gc_seconds * 1000.0, g.total_allocated / 1048576.0, g.peak_heap / 1048576.0,
            (g.live_bytes + g.allocated_since_gc) / 1048576.0, g.chunk_count, g.released_chunks, g.large_count);
#endif
#if CSRT_HAS_CONCURRENT
    fprintf(stderr, "csrt gc: %llu allocation waits for a cycle, %.2f ms max\n", (unsigned long long)cm_waits, cm_wait_max_ms);
    fprintf(stderr, "csrt gc: %llu concurrent cycles, pauses %.1f ms total, %.2f ms max (initial mark max %.2f; longest remark %.2f: buffers %.2f, roots %.2f, frames %.2f, drain %.2f, weak+sweep %.2f)\n",
            (unsigned long long)cm_cycles, cm_pause_total_ms, cm_pause_max_ms, cm_initial_max_ms, cm_remark_max_ms,
            cm_phase_ms[0], cm_phase_ms[1], cm_phase_ms[2], cm_phase_ms[3], cm_phase_ms[4]);
    if (cm_pause_count) {
        for (size_t i = 1; i < cm_pause_count; i++)
            for (size_t j = i; j > 0 && cm_pauses[j - 1] > cm_pauses[j]; j--) { double t = cm_pauses[j]; cm_pauses[j] = cm_pauses[j - 1]; cm_pauses[j - 1] = t; }
        fprintf(stderr, "csrt gc: pause p50 %.2f ms, p99 %.2f ms, max %.2f ms (%zu pauses)\n", cm_pauses[cm_pause_count / 2],
                cm_pauses[cm_pause_count * 99 / 100], cm_pauses[cm_pause_count - 1], cm_pause_count);
    }
#endif
#ifdef CSRT_ALLOC_PROFILE
    profile_print();
    void csrt_task_sites_print(void);  // async.c
    csrt_task_sites_print();
    void csrt_dispatch_print(void);    // csrt.c
    csrt_dispatch_print();
#endif
}
