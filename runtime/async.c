// csrt async: tasks, a single-threaded deterministic scheduler, and iterator objects.
//
// async methods and iterators are LLVM coroutines (switch-resumed lowering, the machinery clang
// uses for C++20 coroutines). The compiler emits C-ABI wrappers csrt_coro_resume/destroy around
// llvm.coro.resume/destroy (the split resume functions are fastcc, so we never call frame[0]
// directly). Coroutine frames, tasks and the scheduler's queues are GC objects; the queues are
// reachable from globals in .bss, which the conservative collector scans.
//
// Threading model (docs/10-async.md): everything runs on the main thread. Continuations, timers
// and Task.Run work items are queued and run in FIFO order while someone waits on a task
// (async Main, .Result, .Wait(), GetAwaiter().GetResult()).
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

// Emitted by csharpc in modules that contain coroutines; these weak fallbacks are never reached.
__attribute__((weak)) void csrt_coro_resume(void* frame) { (void)frame; abort(); }
__attribute__((weak)) void csrt_coro_destroy(void* frame) { (void)frame; abort(); }

const csrt_BuiltinMethodTable csrt_Task_MT = {
    { &csrt_Object_MT.header, "System.Threading.Tasks.Task", 0, 0 },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};
const csrt_BuiltinMethodTable csrt_Iterator_MT = {
    { &csrt_Object_MT.header, "System.Collections.Generic.IEnumerable", 0, 0 },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};
static const csrt_BuiltinMethodTable csrt_CoroFrame_MT = {
    { &csrt_Object_MT.header, "<coroutine frame>", 0, 0 },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

// Layout shared with the compiler (FunctionCodeGen.Async.cs): payload at offset 48.
typedef struct csrt_Task {
    const csrt_MethodTable* mt;
    int32_t state;            // 0 pending, 1 completed
    int32_t payload_size;
    csrt_Array* conts;        // pairs (kind, ptr)
    int32_t ncont;
    int32_t pad;
    void* reserved0;
    void* reserved1;
    uint8_t payload[];        // Task<T> result
} csrt_Task;

_Static_assert(offsetof(csrt_Task, payload) == 48, "task payload offset is part of the compiler ABI");

typedef struct csrt_Iterator {
    const csrt_MethodTable* mt;
    int32_t state;            // 0 not started / running, 1 has current, 2 finished
    int32_t taken;            // GetEnumerator() already returned this object
    void* frame;
    void* args;               // saved arguments, for GetEnumerator() on an already-used enumerable
    void* (*clone)(void* args);
    void* reserved;
    uint8_t payload[];        // Current
} csrt_Iterator;

_Static_assert(offsetof(csrt_Iterator, payload) == 48, "iterator payload offset is part of the compiler ABI");
_Static_assert(offsetof(csrt_Iterator, state) == 8, "iterator state offset is part of the compiler ABI");

enum { K_RESUME = 1, K_WHEN_ALL = 2, K_CHAIN = 3, K_WHEN_ANY = 4, K_RUN_ACTION = 5, K_RUN_FUNC_TASK = 6 };

typedef struct { const csrt_MethodTable* mt; void* target; void* fn; } Delegate;

typedef struct {
    const csrt_MethodTable* mt;
    csrt_Task* result;
    csrt_Array* tasks;
    int32_t remaining;
    int32_t element_size;     // 0: WhenAll(Task[]) ; otherwise Task<T>[] -> T[]
} WhenAllState;

static _Noreturn void async_fail(const char* message) {
    fflush(stdout);
    fprintf(stderr, "Unhandled exception. System.InvalidOperationException: %s\n", message);
    fflush(stderr);
    abort();
}

// ---------------------------------------------------------------------------------------------
// GC-visible growable arrays of words, rooted by globals
// ---------------------------------------------------------------------------------------------
static csrt_Array* s_queue;     // ring buffer of triples (kind, a, b)
static int64_t s_qhead, s_qcount;
static csrt_Array* s_timers;    // triples (deadline_ms, task, unused)
static int64_t s_ntimers;
static csrt_Task* s_completed;  // Task.CompletedTask

static void** words(csrt_Array* a) { return (void**)a->data; }

static void enqueue(intptr_t kind, void* a, void* b) {
    int64_t cap = s_queue ? s_queue->length / 3 : 0;
    if (s_qcount == cap) {
        int64_t ncap = cap ? cap * 2 : 64;
        csrt_Array* grown = csrt_new_array(sizeof(void*), ncap * 3);
        for (int64_t i = 0; i < s_qcount; i++) {
            int64_t from = ((s_qhead + i) % cap) * 3;
            memcpy(words(grown) + i * 3, words(s_queue) + from, 3 * sizeof(void*));
        }
        s_queue = grown;
        s_qhead = 0;
        cap = ncap;
    }
    int64_t at = ((s_qhead + s_qcount) % cap) * 3;
    words(s_queue)[at] = (void*)kind;
    words(s_queue)[at + 1] = a;
    words(s_queue)[at + 2] = b;
    s_qcount++;
}

static bool dequeue(intptr_t* kind, void** a, void** b) {
    if (s_qcount == 0) return false;
    int64_t cap = s_queue->length / 3;
    int64_t at = s_qhead * 3;
    *kind = (intptr_t)words(s_queue)[at];
    *a = words(s_queue)[at + 1];
    *b = words(s_queue)[at + 2];
    words(s_queue)[at + 1] = words(s_queue)[at + 2] = NULL;
    s_qhead = (s_qhead + 1) % cap;
    s_qcount--;
    return true;
}

static int64_t now_ms(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
}

// ---------------------------------------------------------------------------------------------
// Tasks
// ---------------------------------------------------------------------------------------------
void* csrt_coro_alloc(int64_t size) { return csrt_alloc(&csrt_CoroFrame_MT.header, size); }

csrt_Task* csrt_task_new(int32_t payload_size) {
    csrt_Task* t = csrt_alloc(&csrt_Task_MT.header, (int64_t)sizeof(csrt_Task) + (payload_size > 0 ? payload_size : 0));
    t->payload_size = payload_size;
    return t;
}

static uint8_t s_zero_payload[64];

void* csrt_task_result_ptr(csrt_Task* t) { return t ? (void*)t->payload : (void*)s_zero_payload; }

bool csrt_task_is_completed(csrt_Task* t) { return !t || t->state != 0; }

static void add_continuation(csrt_Task* t, intptr_t kind, void* p) {
    int64_t cap = t->conts ? t->conts->length / 2 : 0;
    if (t->ncont == cap) {
        int64_t ncap = cap ? cap * 2 : 2;
        csrt_Array* grown = csrt_new_array(sizeof(void*), ncap * 2);
        if (t->conts) memcpy(grown->data, t->conts->data, (size_t)t->ncont * 2 * sizeof(void*));
        t->conts = grown;
    }
    words(t->conts)[t->ncont * 2] = (void*)kind;
    words(t->conts)[t->ncont * 2 + 1] = p;
    t->ncont++;
}

void csrt_task_complete(csrt_Task* t);

static void when_all_step(WhenAllState* s) {
    if (--s->remaining > 0) return;
    if (s->element_size > 0) {
        int64_t n = s->tasks->length;
        csrt_Array* values = csrt_new_array(s->element_size, n);
        for (int64_t i = 0; i < n; i++)
            memcpy(values->data + i * s->element_size, csrt_task_result_ptr(words(s->tasks)[i]), (size_t)s->element_size);
        memcpy(s->result->payload, &values, sizeof(void*));
    }
    csrt_task_complete(s->result);
}

static void chain(csrt_Task* from, csrt_Task* to) {
    int32_t n = to->payload_size < (from ? from->payload_size : 0) ? to->payload_size : (from ? from->payload_size : 0);
    if (n > 0) memcpy(to->payload, from->payload, (size_t)n);
    csrt_task_complete(to);
}

void csrt_task_complete(csrt_Task* t) {
    if (t->state != 0) async_fail("An attempt was made to transition a task to a final state when it had already completed.");
    t->state = 1;
    for (int32_t i = 0; i < t->ncont; i++) {
        intptr_t kind = (intptr_t)words(t->conts)[i * 2];
        void* p = words(t->conts)[i * 2 + 1];
        switch (kind) {
            case K_RESUME: enqueue(K_RESUME, p, NULL); break;
            case K_WHEN_ALL: when_all_step(p); break;
            case K_CHAIN: chain(t, p); break;
            case K_WHEN_ANY: {
                csrt_Task* any = p;
                if (any->state == 0) { memcpy(any->payload, &t, sizeof(void*)); csrt_task_complete(any); }
                break;
            }
        }
    }
    t->conts = NULL;
    t->ncont = 0;
}

/// `await t` when t isn't complete: resume `frame` once it is.
void csrt_task_on_completed(csrt_Task* t, void* frame) { add_continuation(t, K_RESUME, frame); }

/// `await Task.Yield()`: resume after everything already queued.
void csrt_async_yield(void* frame) { enqueue(K_RESUME, frame, NULL); }

csrt_Task* csrt_task_completed_task(void) {
    if (!s_completed) { s_completed = csrt_task_new(0); s_completed->state = 1; }
    return s_completed;
}

csrt_Task* csrt_task_delay(int32_t ms) {
    if (ms == 0) return csrt_task_completed_task();
    if (ms < -1) {
        fflush(stdout);
        fputs("Unhandled exception. System.ArgumentOutOfRangeException: The value needs to be either -1 (signifying an infinite timeout), 0 or a positive integer. (Parameter 'millisecondsDelay')\n", stderr);
        abort();
    }
    csrt_Task* t = csrt_task_new(0);
    if (ms == -1) return t;  // never completes
    int64_t cap = s_timers ? s_timers->length / 3 : 0;
    if (s_ntimers == cap) {
        int64_t ncap = cap ? cap * 2 : 16;
        csrt_Array* grown = csrt_new_array(sizeof(void*), ncap * 3);
        if (s_timers) memcpy(grown->data, s_timers->data, (size_t)s_ntimers * 3 * sizeof(void*));
        s_timers = grown;
    }
    words(s_timers)[s_ntimers * 3] = (void*)(intptr_t)(now_ms() + ms);
    words(s_timers)[s_ntimers * 3 + 1] = t;
    words(s_timers)[s_ntimers * 3 + 2] = NULL;
    s_ntimers++;
    return t;
}

csrt_Task* csrt_task_when_all(csrt_Array* tasks, int32_t element_size) {
    if (!tasks) csrt_throw_null_reference();
    csrt_Task* result = csrt_task_new(element_size > 0 ? (int32_t)sizeof(void*) : 0);
    WhenAllState* s = csrt_alloc(&csrt_Object_MT.header, sizeof(WhenAllState));
    s->result = result;
    s->tasks = tasks;
    s->element_size = element_size;
    s->remaining = 1;  // guard: completes only after registration
    for (int64_t i = 0; i < tasks->length; i++) {
        csrt_Task* t = words(tasks)[i];
        if (!t) csrt_throw_null_reference();
        s->remaining++;
        if (t->state != 0) when_all_step(s);
        else add_continuation(t, K_WHEN_ALL, s);
    }
    when_all_step(s);
    return result;
}

csrt_Task* csrt_task_when_any(csrt_Array* tasks) {
    if (!tasks) csrt_throw_null_reference();
    if (tasks->length == 0) async_fail("The tasks argument contains no tasks.");
    csrt_Task* result = csrt_task_new(sizeof(void*));
    for (int64_t i = 0; i < tasks->length; i++) {
        csrt_Task* t = words(tasks)[i];
        if (t->state != 0) { memcpy(result->payload, &t, sizeof(void*)); csrt_task_complete(result); return result; }
    }
    for (int64_t i = 0; i < tasks->length; i++) add_continuation(words(tasks)[i], K_WHEN_ANY, result);
    return result;
}

/// Task.Run(Action): queued, runs when the main thread next drains the scheduler.
csrt_Task* csrt_task_run_action(void* del) {
    if (!del) csrt_throw_null_reference();
    csrt_Task* t = csrt_task_new(0);
    enqueue(K_RUN_ACTION, del, t);
    return t;
}

/// Task.Run(Func<Task>) / Task.Run(Func<Task<T>>): the returned task is unwrapped.
csrt_Task* csrt_task_run_func_task(void* del, int32_t payload_size) {
    if (!del) csrt_throw_null_reference();
    csrt_Task* t = csrt_task_new(payload_size);
    enqueue(K_RUN_FUNC_TASK, del, t);
    return t;
}

static void run_item(intptr_t kind, void* a, void* b) {
    switch (kind) {
        case K_RESUME: csrt_coro_resume(a); break;
        case K_RUN_ACTION: {
            Delegate* d = a;
            ((void (*)(void*))d->fn)(d->target);
            csrt_task_complete(b);
            break;
        }
        case K_RUN_FUNC_TASK: {
            Delegate* d = a;
            csrt_Task* inner = ((csrt_Task * (*)(void*)) d->fn)(d->target);
            if (csrt_task_is_completed(inner)) chain(inner, b);
            else add_continuation(inner, K_CHAIN, b);
            break;
        }
    }
}

static bool fire_timers(void) {
    if (s_ntimers == 0) return false;
    // Earliest deadline first; ties fire in creation order.
    int64_t best = 0;
    for (int64_t i = 1; i < s_ntimers; i++)
        if ((intptr_t)words(s_timers)[i * 3] < (intptr_t)words(s_timers)[best * 3]) best = i;
    int64_t deadline = (intptr_t)words(s_timers)[best * 3];
    int64_t wait = deadline - now_ms();
    if (wait > 0) {
        struct timespec ts = { wait / 1000, (wait % 1000) * 1000000 };
        nanosleep(&ts, NULL);
    }
    csrt_Task* t = words(s_timers)[best * 3 + 1];
    memmove(words(s_timers) + best * 3, words(s_timers) + (best + 1) * 3, (size_t)(s_ntimers - best - 1) * 3 * sizeof(void*));
    s_ntimers--;
    words(s_timers)[s_ntimers * 3 + 1] = NULL;
    csrt_task_complete(t);
    return true;
}

/// Drives the scheduler until `t` completes (async Main, .Result, .Wait(), GetResult()).
void csrt_task_wait(csrt_Task* t) {
    while (!csrt_task_is_completed(t)) {
        intptr_t kind; void* a; void* b;
        if (dequeue(&kind, &a, &b)) { run_item(kind, a, b); continue; }
        if (fire_timers()) continue;
        async_fail("csharpc: deadlock — the awaited task can never complete (single-threaded scheduler).");
    }
}

// ---------------------------------------------------------------------------------------------
// Iterators
// ---------------------------------------------------------------------------------------------
csrt_Iterator* csrt_iterator_new(int32_t payload_size, void* clone, void* args) {
    csrt_Iterator* it = csrt_alloc(&csrt_Iterator_MT.header, (int64_t)sizeof(csrt_Iterator) + (payload_size > 0 ? payload_size : 0));
    it->clone = (void* (*)(void*))clone;
    it->args = args;
    return it;
}

void csrt_iterator_set_frame(csrt_Iterator* it, void* frame) { it->frame = frame; }

bool csrt_is_iterator(void* obj) { return obj && ((csrt_Object*)obj)->mt == &csrt_Iterator_MT.header; }

static csrt_Iterator* require_iterator(void* obj) {
    if (!obj) csrt_throw_null_reference();
    if (!csrt_is_iterator(obj)) {
        fflush(stdout);
        fprintf(stderr, "Unhandled exception. System.NotSupportedException: csharpc: enumerating a '%s' is not supported yet.\n",
                ((csrt_Object*)obj)->mt->name);
        abort();
    }
    return obj;
}

void* csrt_iterator_get_enumerator(void* obj) {
    csrt_Iterator* it = require_iterator(obj);
    if (!it->taken && it->state == 0) { it->taken = 1; return it; }
    if (!it->clone) return it;
    csrt_Iterator* fresh = it->clone(it->args);
    fresh->taken = 1;
    return fresh;
}

bool csrt_iterator_move_next(void* obj) {
    csrt_Iterator* it = require_iterator(obj);
    if (it->state == 2) return false;
    it->state = 0;
    csrt_coro_resume(it->frame);
    return it->state == 1;
}

void* csrt_iterator_current_ptr(void* obj) { return require_iterator(obj)->payload; }

/// Dispose(): a suspended iterator is destroyed, which runs its pending finally blocks.
void csrt_iterator_dispose(void* obj) {
    if (!obj || !csrt_is_iterator(obj)) return;
    csrt_Iterator* it = obj;
    if (it->state == 2) return;
    it->state = 2;
    csrt_coro_destroy(it->frame);
}

/// ValueTask.AsTask(): default(ValueTask) is a null task pointer meaning "completed, default result".
csrt_Task* csrt_valuetask_as_task(csrt_Task* t, int32_t payload_size) {
    if (t) return t;
    csrt_Task* done = csrt_task_new(payload_size);
    done->state = 1;
    return done;
}
