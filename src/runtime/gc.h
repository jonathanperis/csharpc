// csrt garbage collector — conservative, non-moving mark-sweep. See gc.c.
#ifndef CSRT_GC_H
#define CSRT_GC_H

#include <pthread.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/// A managed thread as the collector sees it (registry entry).
typedef struct csrt_GcThread csrt_GcThread;
struct csrt_GcThread {
    csrt_GcThread* next;
    pthread_t thread;
    int state;                  // starting / running
    void* stack_bottom;         // highest address of the stack
    void* volatile stack_top;   // recorded while stopped
    uintptr_t tls_lo, tls_hi;   // static TLS block of the executable
    void* cache;                // the thread's allocation caches
    void* managed;              // GC root: the System.Threading.Thread object
};

// Thread registration: the creator prepares an entry (rooting `managed`), the new thread attaches
// itself before touching the managed heap and detaches before it exits.
csrt_GcThread* csrt_gc_thread_prepare(void* managed);
void csrt_gc_thread_attach(csrt_GcThread* t);
void csrt_gc_thread_detach(void);
void csrt_gc_thread_abandon(csrt_GcThread* t);
bool csrt_gc_is_multithreaded(void);

void csrt_gc_init(void);
void csrt_gc_shutdown(void);

// Returns zeroed memory of at least `size` bytes, 16-byte aligned, or NULL when out of memory.
void* csrt_gc_alloc(size_t size);

// Like csrt_gc_alloc, but small blocks are NOT zeroed: only for objects the caller fully
// initializes and the collector never scans (strings, primitive arrays it then fills).
void* csrt_gc_alloc_noclear(size_t size);

// System.GC.Collect() / System.GC.GetTotalMemory(bool).
void csrt_gc_collect(void);
int64_t csrt_gc_total_memory(void);

#endif
