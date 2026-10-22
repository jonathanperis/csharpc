// csrt garbage collector — conservative, non-moving mark-sweep. See gc.c.
#ifndef CSRT_GC_H
#define CSRT_GC_H

#include <stddef.h>
#include <stdint.h>

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
