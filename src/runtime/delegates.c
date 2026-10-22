// csrt delegates: Delegate.Combine / Delegate.Remove for multicast delegates and events.
//
// Layout (FunctionCodeGen.Closures.cs): { mt, target, fn, invocation_list }. A single-cast
// delegate has a null list; a multicast delegate's list is a T[]-shaped array of single-cast
// delegates (and its own target/fn mirror the last entry).
#include "csrt.h"

#include <string.h>

typedef struct Delegate {
    const csrt_MethodTable* mt;
    void* target;
    void* fn;
    csrt_Array* list;
} Delegate;

static int64_t count_of(const Delegate* d) { return d->list ? d->list->length : 1; }
static Delegate* entry_at(Delegate* d, int64_t i) { return d->list ? ((Delegate**)d->list->data)[i] : d; }

static Delegate* make(const csrt_MethodTable* mt, Delegate** entries, int64_t n) {
    if (n == 0) return NULL;
    if (n == 1) return entries[0];
    csrt_Array* list = csrt_new_array(sizeof(void*), n);
    memcpy(list->data, entries, sizeof(void*) * (size_t)n);
    Delegate* d = csrt_alloc(mt, sizeof(Delegate));
    d->list = list;
    d->target = entries[n - 1]->target;
    d->fn = entries[n - 1]->fn;
    return d;
}

Delegate* csrt_delegate_combine(Delegate* a, Delegate* b) {
    if (!a) return b;
    if (!b) return a;
    int64_t na = count_of(a), nb = count_of(b);
    // Collect entries into a GC array first so every entry stays reachable during allocation.
    csrt_Array* tmp = csrt_new_array(sizeof(void*), na + nb);
    Delegate** entries = (Delegate**)tmp->data;
    for (int64_t i = 0; i < na; i++) entries[i] = entry_at(a, i);
    for (int64_t i = 0; i < nb; i++) entries[na + i] = entry_at(b, i);
    return make(a->mt, entries, na + nb);
}

static bool same_entry(const Delegate* x, const Delegate* y) { return x->target == y->target && x->fn == y->fn; }

/// Removes the last occurrence of value's invocation list from source's (Delegate.Remove semantics).
Delegate* csrt_delegate_remove(Delegate* source, Delegate* value) {
    if (!source) return NULL;
    if (!value) return source;
    int64_t ns = count_of(source), nv = count_of(value);
    for (int64_t start = ns - nv; start >= 0; start--) {
        bool match = true;
        for (int64_t k = 0; k < nv && match; k++) match = same_entry(entry_at(source, start + k), entry_at(value, k));
        if (!match) continue;
        csrt_Array* tmp = csrt_new_array(sizeof(void*), ns - nv > 0 ? ns - nv : 1);
        Delegate** entries = (Delegate**)tmp->data;
        int64_t n = 0;
        for (int64_t i = 0; i < ns; i++)
            if (i < start || i >= start + nv) entries[n++] = entry_at(source, i);
        return make(source->mt, entries, n);
    }
    return source;
}

/// Delegate.GetInvocationList(): a Delegate[] of the single-cast entries, in invocation order.
csrt_Array* csrt_delegate_get_invocation_list(Delegate* d) {
    if (!d) csrt_throw_null_reference();
    int64_t n = count_of(d);
    csrt_Array* result = csrt_new_array(sizeof(void*), n);
    for (int64_t i = 0; i < n; i++) ((Delegate**)result->data)[i] = entry_at(d, i);
    return result;
}
