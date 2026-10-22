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
static const csrt_BuiltinMethodTable csrt_CoroFrame_MT = {
    { &csrt_Object_MT.header, "<coroutine frame>", 0, 0 },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

// Layout shared with the compiler (FunctionCodeGen.Async.cs): payload at offset 48.
typedef struct csrt_Task {
    const csrt_MethodTable* mt;
    int32_t state;            // 0 pending, 1 ran to completion, 2 faulted, 3 canceled
    int32_t payload_size;
    csrt_Array* conts;        // pairs (kind, ptr)
    int32_t ncont;
    int32_t pad;
    void* exception;          // faulted: the exception `await` rethrows (the first one)
    void* aggregate;          // faulted: Task.Exception (AggregateException), created lazily
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

enum { K_RESUME = 1, K_WHEN_ALL = 2, K_CHAIN = 3, K_WHEN_ANY = 4, K_RUN_ACTION = 5, K_RUN_FUNC_TASK = 6, K_CANCEL_SOURCE = 7 };

typedef struct { const csrt_MethodTable* mt; void* target; void* fn; } Delegate;

enum { T_PENDING = 0, T_DONE = 1, T_FAULTED = 2, T_CANCELED = 3 };

// Calls fn(arg) and returns the managed exception it threw (or null). Defined by csharpc in LLVM IR
// (invoke + catch-all landing pad — C can't catch); these weak fallbacks don't catch.
__attribute__((weak)) void* csrt_call_catching_void(void (*fn)(void*), void* arg) { fn(arg); return NULL; }
__attribute__((weak)) void* csrt_call_catching_ptr(void* (*fn)(void*), void* arg, void** result) { *result = fn(arg); return NULL; }

static void finish(csrt_Task* t, int32_t state);
static const csrt_MethodTable mt_invalid_operation = { NULL, "System.InvalidOperationException", 0, 0 };
static const char k_already_completed[] = "An attempt was made to transition a task to a final state when it had already completed.";

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

bool csrt_task_is_completed(csrt_Task* t) { return !t || t->state != T_PENDING; }
bool csrt_task_is_faulted(csrt_Task* t) { return t && t->state == T_FAULTED; }
bool csrt_task_is_canceled(csrt_Task* t) { return t && t->state == T_CANCELED; }
bool csrt_task_is_completed_successfully(csrt_Task* t) { return !t || t->state == T_DONE; }

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
    // Faulted inputs: the result faults with all their exceptions (await rethrows the first);
    // otherwise any canceled input cancels the result.
    int64_t n = s->tasks->length, faulted = 0;
    bool canceled = false;
    for (int64_t i = 0; i < n; i++) {
        csrt_Task* t = words(s->tasks)[i];
        if (t->state == T_FAULTED) faulted++;
        else if (t->state == T_CANCELED) canceled = true;
    }
    if (faulted > 0) {
        void** inners = malloc(sizeof(void*) * (size_t)faulted);
        int64_t k = 0;
        for (int64_t i = 0; i < n; i++) {
            csrt_Task* t = words(s->tasks)[i];
            if (t->state == T_FAULTED) inners[k++] = t->exception;
        }
        s->result->exception = inners[0];
        s->result->aggregate = csrt_aggregate_exception_new(inners, faulted);
        free(inners);
        finish(s->result, T_FAULTED);
        return;
    }
    if (canceled) { finish(s->result, T_CANCELED); return; }
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
    if (from && from->state == T_FAULTED) { to->exception = from->exception; finish(to, T_FAULTED); return; }
    if (from && from->state == T_CANCELED) { finish(to, T_CANCELED); return; }
    int32_t n = to->payload_size < (from ? from->payload_size : 0) ? to->payload_size : (from ? from->payload_size : 0);
    if (n > 0) memcpy(to->payload, from->payload, (size_t)n);
    csrt_task_complete(to);
}

void csrt_task_complete(csrt_Task* t) {
    if (t->state != T_PENDING) csrt_throw(&mt_invalid_operation, csrt_string_from_utf8(k_already_completed));
    finish(t, T_DONE);
}

/// Stores an exception that escaped an async method (or SetException) and completes the task.
void csrt_task_fault(csrt_Task* t, void* exception) {
    if (t->state != T_PENDING) csrt_throw(&mt_invalid_operation, csrt_string_from_utf8(k_already_completed));
    t->exception = exception;
    finish(t, T_FAULTED);
}

void csrt_task_cancel(csrt_Task* t) {
    if (t->state != T_PENDING) csrt_throw(&mt_invalid_operation, csrt_string_from_utf8(k_already_completed));
    finish(t, T_CANCELED);
}

bool csrt_task_try_complete(csrt_Task* t) { if (t->state != T_PENDING) return false; finish(t, T_DONE); return true; }
bool csrt_task_try_fault(csrt_Task* t, void* e) { if (t->state != T_PENDING) return false; t->exception = e; finish(t, T_FAULTED); return true; }
bool csrt_task_try_cancel(csrt_Task* t) { if (t->state != T_PENDING) return false; finish(t, T_CANCELED); return true; }

/// `await t` / GetAwaiter().GetResult(): rethrow the original exception; canceled -> TaskCanceledException.
void csrt_task_throw_if_faulted(csrt_Task* t) {
    if (!t) return;
    if (t->state == T_FAULTED) csrt_rethrow(t->exception);
    if (t->state == T_CANCELED) csrt_throw_object(csrt_task_canceled_exception_new());
}

/// Task.Exception: an AggregateException for faulted tasks, else null.
void* csrt_task_exception(csrt_Task* t) {
    if (!t || t->state != T_FAULTED) return NULL;
    if (!t->aggregate) t->aggregate = csrt_aggregate_exception_new(&t->exception, 1);
    return t->aggregate;
}

/// .Result / .Wait(): failures surface wrapped in an AggregateException.
void csrt_task_throw_aggregate_if_faulted(csrt_Task* t) {
    if (!t) return;
    if (t->state == T_FAULTED) csrt_throw_object(csrt_task_exception(t));
    if (t->state == T_CANCELED) {
        void* inner = csrt_task_canceled_exception_new();
        csrt_throw_object(csrt_aggregate_exception_new(&inner, 1));
    }
}

/// Task.FromException(e) / FromException<T>(e)
csrt_Task* csrt_task_from_exception(void* exception, int32_t payload_size) {
    if (!exception) csrt_throw_null_reference();
    csrt_Task* t = csrt_task_new(payload_size);
    t->exception = exception;
    t->state = T_FAULTED;
    return t;
}

static void finish(csrt_Task* t, int32_t state) {
    t->state = state;
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

typedef struct CancellationSource {
    const csrt_MethodTable* mt;
    int32_t canceled;
    int32_t ncallbacks;
    csrt_Array* callbacks;   // registered Action delegates
} CancellationSource;

const csrt_BuiltinMethodTable csrt_CancellationTokenSource_MT = {
    { &csrt_Object_MT.header, "System.Threading.CancellationTokenSource", sizeof(CancellationSource), 0 },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

static void add_timer(int64_t deadline, csrt_Task* task, CancellationSource* token) {
    int64_t cap = s_timers ? s_timers->length / 3 : 0;
    if (s_ntimers == cap) {
        int64_t ncap = cap ? cap * 2 : 16;
        csrt_Array* grown = csrt_new_array(sizeof(void*), ncap * 3);
        if (s_timers) memcpy(grown->data, s_timers->data, (size_t)s_ntimers * 3 * sizeof(void*));
        s_timers = grown;
    }
    words(s_timers)[s_ntimers * 3] = (void*)(intptr_t)deadline;
    words(s_timers)[s_ntimers * 3 + 1] = task;
    words(s_timers)[s_ntimers * 3 + 2] = token;
    s_ntimers++;
}

static void remove_timer(int64_t index) {
    memmove(words(s_timers) + index * 3, words(s_timers) + (index + 1) * 3, (size_t)(s_ntimers - index - 1) * 3 * sizeof(void*));
    s_ntimers--;
    words(s_timers)[s_ntimers * 3 + 1] = words(s_timers)[s_ntimers * 3 + 2] = NULL;
}

csrt_Task* csrt_task_delay_token(int32_t ms, CancellationSource* token);

csrt_Task* csrt_task_delay(int32_t ms) { return csrt_task_delay_token(ms, NULL); }

csrt_Task* csrt_task_delay_token(int32_t ms, CancellationSource* token) {
    if (token && token->canceled) { csrt_Task* t = csrt_task_new(0); t->state = T_CANCELED; return t; }
    if (ms == 0) return csrt_task_completed_task();
    if (ms < -1) {
        fflush(stdout);
        fputs("Unhandled exception. System.ArgumentOutOfRangeException: The value needs to be either -1 (signifying an infinite timeout), 0 or a positive integer. (Parameter 'millisecondsDelay')\n", stderr);
        abort();
    }
    csrt_Task* t = csrt_task_new(0);
    if (ms == -1 && !token) return t;  // never completes
    add_timer(ms == -1 ? INT64_MAX : now_ms() + ms, t, token);
    return t;
}

// ---------------------------------------------------------------------------------------------
// CancellationTokenSource / CancellationToken (a token is its source pointer; default = null)
// ---------------------------------------------------------------------------------------------
CancellationSource* csrt_cts_new(void) { return csrt_alloc(&csrt_CancellationTokenSource_MT.header, sizeof(CancellationSource)); }

void csrt_cts_cancel(CancellationSource* cts) {
    if (!cts) csrt_throw_null_reference();
    if (cts->canceled) return;
    cts->canceled = 1;
    // Delays waiting on this token are canceled (in creation order).
    for (int64_t i = 0; i < s_ntimers;) {
        if (words(s_timers)[i * 3 + 2] == cts && words(s_timers)[i * 3 + 1]) {
            csrt_Task* t = words(s_timers)[i * 3 + 1];
            remove_timer(i);
            if (t->state == T_PENDING) finish(t, T_CANCELED);
        } else i++;
    }
    // Registered callbacks run synchronously, most recently registered first (like .NET).
    for (int32_t i = cts->ncallbacks - 1; i >= 0; i--) {
        Delegate* d = words(cts->callbacks)[i];
        ((void (*)(void*))d->fn)(d->target);
    }
}

CancellationSource* csrt_cts_new_after(int32_t ms) {
    CancellationSource* cts = csrt_cts_new();
    if (ms == 0) cts->canceled = 1;
    else if (ms > 0) add_timer(now_ms() + ms, NULL, cts);
    return cts;
}

void csrt_cts_cancel_after(CancellationSource* cts, int32_t ms) {
    if (!cts) csrt_throw_null_reference();
    if (ms == 0) csrt_cts_cancel(cts);
    else if (ms > 0) add_timer(now_ms() + ms, NULL, cts);
}

bool csrt_token_is_cancellation_requested(CancellationSource* token) { return token && token->canceled; }

void csrt_token_throw_if_cancellation_requested(CancellationSource* token) {
    if (token && token->canceled) csrt_throw_object(csrt_operation_canceled_exception_new());
}

void csrt_token_register(CancellationSource* token, void* action) {
    if (!action) csrt_throw_null_reference();
    if (!token) return;  // CancellationToken.None never cancels
    if (token->canceled) { Delegate* d = action; ((void (*)(void*))d->fn)(d->target); return; }
    int64_t cap = token->callbacks ? token->callbacks->length : 0;
    if (token->ncallbacks == cap) {
        csrt_Array* grown = csrt_new_array(sizeof(void*), cap ? cap * 2 : 4);
        if (token->callbacks) memcpy(grown->data, token->callbacks->data, (size_t)token->ncallbacks * sizeof(void*));
        token->callbacks = grown;
    }
    words(token->callbacks)[token->ncallbacks++] = action;
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
            void* exception = csrt_call_catching_void((void (*)(void*))d->fn, d->target);
            if (exception) csrt_task_fault(b, exception);
            else csrt_task_complete(b);
            break;
        }
        case K_RUN_FUNC_TASK: {
            Delegate* d = a;
            csrt_Task* inner = NULL;
            void* exception = csrt_call_catching_ptr((void* (*)(void*))d->fn, d->target, (void**)&inner);
            if (exception) csrt_task_fault(b, exception);
            else if (csrt_task_is_completed(inner)) chain(inner, b);
            else add_continuation(inner, K_CHAIN, b);
            break;
        }
        case K_CANCEL_SOURCE: break;
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
    if (deadline == INT64_MAX) return false;  // only token-bound infinite delays left
    csrt_Task* t = words(s_timers)[best * 3 + 1];
    CancellationSource* token = words(s_timers)[best * 3 + 2];
    remove_timer(best);
    if (!t) csrt_cts_cancel(token);              // CancelAfter
    else if (t->state == T_PENDING) csrt_task_complete(t);
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
/// `mt` is a compiler-emitted iterator method table (ModuleCodeGen.Async.cs) whose interface map
/// implements IEnumerable<T>/IEnumerator<T>/IDisposable (or the async counterparts).
csrt_Iterator* csrt_iterator_new(const csrt_MethodTable* mt, int32_t payload_size, void* clone, void* args) {
    csrt_Iterator* it = csrt_alloc(mt, (int64_t)sizeof(csrt_Iterator) + (payload_size > 0 ? payload_size : 0));
    it->clone = (void* (*)(void*))clone;
    it->args = args;
    return it;
}

void csrt_iterator_set_frame(csrt_Iterator* it, void* frame) { it->frame = frame; }

bool csrt_is_iterator(void* obj) { return obj && (((csrt_Object*)obj)->mt->flags & CSRT_MT_ITERATOR); }

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

// An exception that escaped an iterator body during MoveNext()/Dispose(): the coroutine catches it,
// finishes, and parks it here; the C caller rethrows it to the foreach (single-threaded runtime).
static void* volatile s_iterator_exception;

void csrt_iterator_fault(csrt_Iterator* it, void* exception) {
    it->state = 2;
    s_iterator_exception = exception;
}

static void rethrow_iterator_exception(void) {
    void* e = s_iterator_exception;
    if (!e) return;
    s_iterator_exception = NULL;
    csrt_rethrow(e);
}

bool csrt_iterator_move_next(void* obj) {
    csrt_Iterator* it = require_iterator(obj);
    if (it->state == 2) return false;
    it->state = 0;
    csrt_coro_resume(it->frame);
    rethrow_iterator_exception();
    return it->state == 1;
}

void* csrt_iterator_current_ptr(void* obj) { return require_iterator(obj)->payload; }

_Noreturn void csrt_iterator_reset(void* obj) {
    (void)obj;
    fflush(stdout);
    fputs("Unhandled exception. System.NotSupportedException: Specified method is not supported.\n", stderr);
    abort();
}

/// Dispose(): a suspended iterator is destroyed, which runs its pending finally blocks.
void csrt_iterator_dispose(void* obj) {
    if (!obj || !csrt_is_iterator(obj)) return;
    csrt_Iterator* it = obj;
    if (it->state == 2) return;
    it->state = 2;
    csrt_coro_destroy(it->frame);
    rethrow_iterator_exception();
}

/// ValueTask.AsTask(): default(ValueTask) is a null task pointer meaning "completed, default result".
csrt_Task* csrt_valuetask_as_task(csrt_Task* t, int32_t payload_size) {
    if (t) return t;
    csrt_Task* done = csrt_task_new(payload_size);
    done->state = 1;
    return done;
}

// ---------------------------------------------------------------------------------------------
// Async iterators (IAsyncEnumerable<T>): each MoveNextAsync() returns a pending ValueTask<bool>
// (a csrt task) that the next `yield return` completes with true, or the end with false.
// The pending task lives in the iterator's `reserved` slot.
// ---------------------------------------------------------------------------------------------
static void complete_pending(csrt_Iterator* it, bool value) {
    csrt_Task* t = it->reserved;
    if (!t) return;
    it->reserved = NULL;
    t->payload[0] = value;
    csrt_task_complete(t);
}

void csrt_async_iterator_yielded(csrt_Iterator* it) { complete_pending(it, true); }

/// An exception escaped an async iterator: the pending MoveNextAsync() task faults with it.
void csrt_async_iterator_fault(csrt_Iterator* it, void* exception) {
    it->state = 2;
    csrt_Task* t = it->reserved;
    if (!t) { s_iterator_exception = exception; return; }
    it->reserved = NULL;
    csrt_task_fault(t, exception);
}
void csrt_async_iterator_finished(csrt_Iterator* it) { complete_pending(it, false); }

csrt_Task* csrt_async_iterator_move_next(void* obj) {
    csrt_Iterator* it = require_iterator(obj);
    csrt_Task* t = csrt_task_new(1);
    if (it->state == 2) { t->state = 1; return t; }
    if (it->reserved) async_fail("MoveNextAsync() called while a previous MoveNextAsync() is still pending.");
    it->reserved = t;
    it->state = 0;
    csrt_coro_resume(it->frame);
    return t;
}

csrt_Task* csrt_async_iterator_dispose(void* obj) {
    csrt_iterator_dispose(obj);
    return NULL;  // default(ValueTask): already completed
}
