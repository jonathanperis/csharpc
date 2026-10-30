// csrt garbage collector — conservative, non-moving mark-sweep. See gc.c.
#ifndef CSRT_GC_H
#define CSRT_GC_H

#include "csrt_pthread.h"
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
#ifdef _WIN32
    void* handle;               // HANDLE for SuspendThread / GetThreadContext
    void* context;              // CONTEXT: the registers of the stopped thread (scanned as roots)
#endif
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

#if defined(CSRT_GC_NURSERY_DEFAULT)
// Generational write barrier (ADR 0004): dirty the card for `slot`, then store `value`.
// The compiler emits this instead of a plain store for heap reference writes. Dirty-before-store
// so a stop-the-world suspend between the two steps cannot hide a young pointer in an old object.
void csrt_gc_write(void** slot, void* value);
// Dirty every card covering [start, start+n): bulk reference copies (Array.Copy).
void csrt_gc_cards_dirty(void* start, size_t n);
// Coroutine frames are written without barriers (LLVM spills): minors scan every live one instead.
void csrt_gc_remember(void* obj);
bool csrt_gc_remembered_object(const void* obj);  // async.c: is this object a frame (a task or iterator frame)?
#endif

#endif
