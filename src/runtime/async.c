// csrt async: tasks, a thread-pool scheduler, timers, and iterator objects.
//
// async methods and iterators are LLVM coroutines (switch-resumed lowering, the machinery clang
// uses for C++20 coroutines). The compiler emits C-ABI wrappers csrt_coro_resume/destroy around
// llvm.coro.resume/destroy (the split resume functions are fastcc, so we never call frame[0]
// directly). Coroutine frames, tasks and the scheduler's queues are GC objects; the queues are
// reachable from globals in .bss, which the conservative collector scans.
//
// Threading model (docs/10-async.md, .NET semantics for console apps):
//   * Task.Run work items and `await` continuations run on a pool of background worker threads
//     (processor-count workers; more are injected when all of them are blocked or starving).
//   * An await on an already-completed task continues synchronously; otherwise the awaiting
//     coroutine is resumed by the thread that completes the task (inline, like .NET), falling back
//     to the pool when inlined continuations nest too deeply.
//   * .Result / .Wait() / GetResult() / async Main block the calling thread on a condition variable.
//   * A timer thread hands expired Task.Delay / CancelAfter timers to the pool.
// Each task's state and continuation list are guarded by a per-task spinlock (the `lock` word).
#define _GNU_SOURCE
#include "csrt.h"

#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

bool csrt_start_background_thread(void (*loop)(void));
int32_t csrt_processor_count(void);

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
    int32_t state;            // 0 pending, 1 ran to completion, 2 faulted, 3 canceled (atomic)
    int32_t payload_size;
    csrt_Array* conts;        // pairs (kind, ptr)
    int32_t ncont;
    int32_t lock;             // spinlock guarding state transitions + conts
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

enum { K_RESUME = 1, K_WHEN_ALL = 2, K_CHAIN = 3, K_WHEN_ANY = 4, K_RUN_ACTION = 5, K_RUN_FUNC_TASK = 6,
       K_TIMER_TASK = 7, K_TIMER_CANCEL = 8, K_RUN_THUNK = 9,
       K_INVOKE = 10 /* an Action continuation (awaiter.OnCompleted) */ };

typedef struct { const csrt_MethodTable* mt; void* target; void* fn; } Delegate;

enum { T_PENDING = 0, T_DONE = 1, T_FAULTED = 2, T_CANCELED = 3 };

// Calls fn(arg) and returns the managed exception it threw (or null). Defined by csharpc in LLVM IR
// (invoke + catch-all landing pad — C can't catch); these weak fallbacks don't catch.
__attribute__((weak)) void* csrt_call_catching_void(void (*fn)(void*), void* arg) { fn(arg); return NULL; }
__attribute__((weak)) void* csrt_call_catching_ptr(void* (*fn)(void*), void* arg, void** result) { *result = fn(arg); return NULL; }

static const csrt_MethodTable mt_invalid_operation = { NULL, "System.InvalidOperationException", 0, 0 };
static const char k_already_completed[] = "An attempt was made to transition a task to a final state when it had already completed.";

typedef struct {
    const csrt_MethodTable* mt;
    csrt_Task* result;
    csrt_Array* tasks;
    int32_t remaining;        // atomic
    int32_t element_size;     // 0: WhenAll(Task[]) ; otherwise Task<T>[] -> T[]
} WhenAllState;

static _Noreturn void async_fail(const char* message) {
    fflush(stdout);
    fprintf(stderr, "Unhandled exception. System.InvalidOperationException: %s\n", message);
    fflush(stderr);
    abort();
}

static void** words(csrt_Array* a) { return (void**)a->data; }

static int64_t now_ms(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
}

// ---------------------------------------------------------------------------------------------
// Thread pool: a FIFO of work items (kind, a, b) in a GC array rooted by a global
// ---------------------------------------------------------------------------------------------
static pthread_mutex_t s_qlock = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t s_qcond = PTHREAD_COND_INITIALIZER;
static csrt_Array* s_queue;     // ring buffer of triples
static int64_t s_qhead, s_qcount;
static int s_workers, s_idle, s_blocked;   // pool workers; idle (waiting for work); blocked in Wait
static int64_t s_dequeued;                 // progress counter (starvation detection)
static bool s_pool_started;
static _Thread_local bool tl_worker;       // this thread is a pool worker
enum { MAX_WORKERS = 512 };

static void pool_start_locked(void);
static void spawn_worker_locked(void);

static void enqueue(intptr_t kind, void* a, void* b) {
    pthread_mutex_lock(&s_qlock);
    if (!s_pool_started) pool_start_locked();
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
    if (s_idle > 0) pthread_cond_signal(&s_qcond);
    else if (s_workers - s_blocked <= 0 && s_workers < MAX_WORKERS) spawn_worker_locked();
    pthread_mutex_unlock(&s_qlock);
}

static bool dequeue_locked(intptr_t* kind, void** a, void** b) {
    if (s_qcount == 0) return false;
    int64_t cap = s_queue->length / 3;
    int64_t at = s_qhead * 3;
    *kind = (intptr_t)words(s_queue)[at];
    *a = words(s_queue)[at + 1];
    *b = words(s_queue)[at + 2];
    words(s_queue)[at + 1] = words(s_queue)[at + 2] = NULL;
    s_qhead = (s_qhead + 1) % cap;
    s_qcount--;
    s_dequeued++;
    return true;
}

static void run_item(intptr_t kind, void* a, void* b);

static void worker_loop(void) {
    tl_worker = true;
    for (;;) {
        intptr_t kind; void* a; void* b;
        pthread_mutex_lock(&s_qlock);
        while (!dequeue_locked(&kind, &a, &b)) {
            s_idle++;
            pthread_cond_wait(&s_qcond, &s_qlock);
            s_idle--;
        }
        pthread_mutex_unlock(&s_qlock);
        run_item(kind, a, b);
        a = b = NULL;  // don't keep finished work reachable from this stack
    }
}

static void spawn_worker_locked(void) {
    if (csrt_start_background_thread(worker_loop)) s_workers++;
}

static void timer_loop(void);

static void pool_start_locked(void) {
    s_pool_started = true;
    int n = csrt_processor_count();
    if (n < 2) n = 2;
    for (int i = 0; i < n; i++) spawn_worker_locked();
    if (!csrt_start_background_thread(timer_loop)) async_fail("csharpc: could not start the timer thread.");
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

static inline int32_t task_state(const csrt_Task* t) { return __atomic_load_n(&t->state, __ATOMIC_SEQ_CST); }
static inline void task_lock(csrt_Task* t) {
    while (__atomic_exchange_n(&t->lock, 1, __ATOMIC_ACQUIRE)) sched_yield();
}
static inline void task_unlock(csrt_Task* t) { __atomic_store_n(&t->lock, 0, __ATOMIC_RELEASE); }

void* csrt_task_result_ptr(csrt_Task* t) { return t ? (void*)t->payload : (void*)s_zero_payload; }

bool csrt_task_is_completed(csrt_Task* t) { return !t || task_state(t) != T_PENDING; }
bool csrt_task_is_faulted(csrt_Task* t) { return t && task_state(t) == T_FAULTED; }
bool csrt_task_is_canceled(csrt_Task* t) { return t && task_state(t) == T_CANCELED; }
bool csrt_task_is_completed_successfully(csrt_Task* t) { return !t || task_state(t) == T_DONE; }

/// Registers a continuation; false if the task already completed (the caller runs it itself).
static bool add_continuation(csrt_Task* t, intptr_t kind, void* p) {
    task_lock(t);
    if (task_state(t) != T_PENDING) { task_unlock(t); return false; }
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
    task_unlock(t);
    return true;
}

// Threads blocked in csrt_task_wait.
static pthread_mutex_t s_wlock = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t s_wcond = PTHREAD_COND_INITIALIZER;
static atomic_int s_waiters;

static void run_continuations(csrt_Task* t, csrt_Array* conts, int32_t n);

/// The one place a task leaves the pending state. False if it had already completed.
static bool try_finish(csrt_Task* t, int32_t state, void* exception) {
    task_lock(t);
    if (task_state(t) != T_PENDING) { task_unlock(t); return false; }
    if (exception) t->exception = exception;
    csrt_Array* conts = t->conts;
    int32_t n = t->ncont;
    t->conts = NULL;
    t->ncont = 0;
    __atomic_store_n(&t->state, state, __ATOMIC_SEQ_CST);
    task_unlock(t);
    run_continuations(t, conts, n);
    if (atomic_load(&s_waiters) > 0) {
        pthread_mutex_lock(&s_wlock);
        pthread_cond_broadcast(&s_wcond);
        pthread_mutex_unlock(&s_wlock);
    }
    return true;
}

static void finish(csrt_Task* t, int32_t state, void* exception) {
    if (!try_finish(t, state, exception)) csrt_throw(&mt_invalid_operation, csrt_string_from_utf8(k_already_completed));
}

void csrt_task_complete(csrt_Task* t) { finish(t, T_DONE, NULL); }

/// Stores an exception that escaped an async method (or SetException) and completes the task.
void csrt_task_fault(csrt_Task* t, void* exception) { finish(t, T_FAULTED, exception); }

void csrt_task_cancel(csrt_Task* t) { finish(t, T_CANCELED, NULL); }

bool csrt_task_try_complete(csrt_Task* t) { return try_finish(t, T_DONE, NULL); }
bool csrt_task_try_fault(csrt_Task* t, void* e) { return try_finish(t, T_FAULTED, e); }
bool csrt_task_try_cancel(csrt_Task* t) { return try_finish(t, T_CANCELED, NULL); }

static void when_all_step(WhenAllState* s) {
    if (__atomic_sub_fetch(&s->remaining, 1, __ATOMIC_ACQ_REL) > 0) return;
    // Faulted inputs: the result faults with all their exceptions (await rethrows the first);
    // otherwise any canceled input cancels the result.
    int64_t n = s->tasks->length, faulted = 0;
    bool canceled = false;
    for (int64_t i = 0; i < n; i++) {
        csrt_Task* t = words(s->tasks)[i];
        int32_t st = task_state(t);
        if (st == T_FAULTED) faulted++;
        else if (st == T_CANCELED) canceled = true;
    }
    if (faulted > 0) {
        void** inners = malloc(sizeof(void*) * (size_t)faulted);
        int64_t k = 0;
        for (int64_t i = 0; i < n; i++) {
            csrt_Task* t = words(s->tasks)[i];
            if (task_state(t) == T_FAULTED) inners[k++] = t->exception;
        }
        void* first = inners[0];
        // The aggregate is created before the result completes (nobody can observe it earlier).
        s->result->aggregate = csrt_aggregate_exception_new(inners, faulted);
        free(inners);
        finish(s->result, T_FAULTED, first);
        return;
    }
    if (canceled) { finish(s->result, T_CANCELED, NULL); return; }
    if (s->element_size > 0) {
        csrt_Array* values = csrt_new_array(s->element_size, n);
        for (int64_t i = 0; i < n; i++)
            memcpy(values->data + i * s->element_size, csrt_task_result_ptr(words(s->tasks)[i]), (size_t)s->element_size);
        memcpy(s->result->payload, &values, sizeof(void*));
    }
    csrt_task_complete(s->result);
}

static void chain(csrt_Task* from, csrt_Task* to) {
    int32_t st = from ? task_state(from) : T_DONE;
    if (st == T_FAULTED) { finish(to, T_FAULTED, from->exception); return; }
    if (st == T_CANCELED) { finish(to, T_CANCELED, NULL); return; }
    int32_t n = to->payload_size < (from ? from->payload_size : 0) ? to->payload_size : (from ? from->payload_size : 0);
    if (n > 0) memcpy(to->payload, from->payload, (size_t)n);
    csrt_task_complete(to);
}

static void when_any_hit(csrt_Task* any, csrt_Task* winner) {
    // Only the first completion wins; the payload is written under the result task's lock.
    task_lock(any);
    bool first = task_state(any) == T_PENDING && *(void**)any->payload == NULL;
    if (first) memcpy(any->payload, &winner, sizeof(void*));
    task_unlock(any);
    if (first) csrt_task_try_complete(any);
}

// Like .NET, `await` continuations run synchronously on the thread that completes the task (the
// completing thread "inlines" them) — unless the stack of inlined continuations gets too deep, in
// which case they go to the pool.
static _Thread_local int tl_inline_depth;
enum { MAX_INLINE_DEPTH = 32 };

/// Runs an Action continuation. An exception escaping it is unhandled, as on .NET's thread pool.
static void invoke_action(void* del) {
    Delegate* d = del;
    void* exception = csrt_call_catching_void((void (*)(void*))d->fn, d->target);
    if (exception) csrt_unhandled_exception(exception);
}

static void run_continuations(csrt_Task* t, csrt_Array* conts, int32_t n) {
    for (int32_t i = 0; i < n; i++) {
        intptr_t kind = (intptr_t)words(conts)[i * 2];
        void* p = words(conts)[i * 2 + 1];
        switch (kind) {
            case K_RESUME:
                if (tl_inline_depth < MAX_INLINE_DEPTH) {
                    tl_inline_depth++;
                    csrt_coro_resume(p);
                    tl_inline_depth--;
                } else enqueue(K_RESUME, p, NULL);
                break;
            case K_INVOKE:
                if (tl_inline_depth < MAX_INLINE_DEPTH) {
                    tl_inline_depth++;
                    invoke_action(p);
                    tl_inline_depth--;
                } else enqueue(K_INVOKE, p, NULL);
                break;
            case K_WHEN_ALL: when_all_step(p); break;
            case K_CHAIN: chain(t, p); break;
            case K_WHEN_ANY: when_any_hit(p, t); break;
        }
    }
}

/// `await t` / GetAwaiter().GetResult(): rethrow the original exception; canceled -> TaskCanceledException.
void csrt_task_throw_if_faulted(csrt_Task* t) {
    if (!t) return;
    int32_t st = task_state(t);
    if (st == T_FAULTED) csrt_rethrow(t->exception);
    if (st == T_CANCELED) csrt_throw_object(csrt_task_canceled_exception_new());
}

/// Task.Exception: an AggregateException for faulted tasks, else null.
void* csrt_task_exception(csrt_Task* t) {
    if (!t || task_state(t) != T_FAULTED) return NULL;
    if (!__atomic_load_n(&t->aggregate, __ATOMIC_ACQUIRE)) {
        void* created = csrt_aggregate_exception_new(&t->exception, 1);
        void* expected = NULL;
        __atomic_compare_exchange_n(&t->aggregate, &expected, created, false, __ATOMIC_ACQ_REL, __ATOMIC_ACQUIRE);
    }
    return t->aggregate;
}

/// .Result / .Wait(): failures surface wrapped in an AggregateException.
void csrt_task_throw_aggregate_if_faulted(csrt_Task* t) {
    if (!t) return;
    int32_t st = task_state(t);
    if (st == T_FAULTED) csrt_throw_object(csrt_task_exception(t));
    if (st == T_CANCELED) {
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

/// `await t` when t wasn't complete: resume `frame` on the pool once it is. The coroutine has
/// already executed llvm.coro.save, so a resume from another thread before it actually suspends
/// is fine.
void csrt_task_on_completed(csrt_Task* t, void* frame) {
    if (!add_continuation(t, K_RESUME, frame)) enqueue(K_RESUME, frame, NULL);
}

/// awaiter.OnCompleted(action) / UnsafeOnCompleted(action) (IL async state machines, custom awaiter
/// patterns): run the action when `t` completes — inline on the completing thread, or on the pool if
/// `t` already completed, as TaskAwaiter does without a SynchronizationContext.
void csrt_task_on_completed_action(csrt_Task* t, void* action) {
    if (!action) csrt_throw_null_reference();
    if (!t || !add_continuation(t, K_INVOKE, action)) enqueue(K_INVOKE, action, NULL);
}

/// YieldAwaiter.OnCompleted(action): run the action on the pool.
void csrt_queue_action(void* action) {
    if (!action) csrt_throw_null_reference();
    enqueue(K_INVOKE, action, NULL);
}

/// `await Task.Yield()`: resume on the pool, after work already queued.
void csrt_async_yield(void* frame) { enqueue(K_RESUME, frame, NULL); }

static csrt_Task* s_completed;  // Task.CompletedTask

csrt_Task* csrt_task_completed_task(void) {
    csrt_Task* t = __atomic_load_n(&s_completed, __ATOMIC_ACQUIRE);
    if (t) return t;
    t = csrt_task_new(0);
    t->state = T_DONE;
    csrt_Task* expected = NULL;
    if (!__atomic_compare_exchange_n(&s_completed, &expected, t, false, __ATOMIC_ACQ_REL, __ATOMIC_ACQUIRE)) t = expected;
    return t;
}

// ---------------------------------------------------------------------------------------------
// Timers + cancellation (guarded by s_tlock)
// ---------------------------------------------------------------------------------------------
typedef struct CancellationSource {
    const csrt_MethodTable* mt;
    int32_t canceled;
    int32_t ncallbacks;
    csrt_Array* callbacks;   // registered Action delegates
} CancellationSource;

const csrt_BuiltinMethodTable csrt_CancellationTokenSource_MT = {
    { &csrt_Object_MT.header, "System.Threading.CancellationTokenSource", sizeof(CancellationSource), CSRT_MT_DISPOSABLE },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

static pthread_mutex_t s_tlock = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t s_tcond;
static pthread_once_t s_tcond_once = PTHREAD_ONCE_INIT;
static csrt_Array* s_timers;    // triples (deadline_ms, task, token); unsorted, scanned in creation order
static int64_t s_ntimers;

static void init_tcond(void) {
#if defined(__APPLE__) || defined(_WIN32)
    // macOS has no pthread_condattr_setclock, and winpthreads' absolute timeouts are wall-clock:
    // timer_loop waits with a relative timeout there.
    pthread_cond_init(&s_tcond, NULL);
#else
    pthread_condattr_t attr;
    pthread_condattr_init(&attr);
    pthread_condattr_setclock(&attr, CLOCK_MONOTONIC);
    pthread_cond_init(&s_tcond, &attr);
    pthread_condattr_destroy(&attr);
#endif
}

static void add_timer(int64_t deadline, csrt_Task* task, CancellationSource* token) {
    pthread_once(&s_tcond_once, init_tcond);
    pthread_mutex_lock(&s_qlock);
    if (!s_pool_started) pool_start_locked();
    pthread_mutex_unlock(&s_qlock);
    pthread_mutex_lock(&s_tlock);
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
    pthread_cond_signal(&s_tcond);
    pthread_mutex_unlock(&s_tlock);
}

static void remove_timer_locked(int64_t index) {
    memmove(words(s_timers) + index * 3, words(s_timers) + (index + 1) * 3, (size_t)(s_ntimers - index - 1) * 3 * sizeof(void*));
    s_ntimers--;
    words(s_timers)[s_ntimers * 3 + 1] = words(s_timers)[s_ntimers * 3 + 2] = NULL;
}

/// The timer thread: hands expired timers to the pool (earliest first, ties in creation order) and
/// injects a worker when queued work makes no progress for a while (all workers blocked).
static void timer_loop(void) {
    pthread_once(&s_tcond_once, init_tcond);
    int64_t last_progress = -1, last_check = now_ms();
    pthread_mutex_lock(&s_tlock);
    for (;;) {
        int64_t now = now_ms();
        // Fire everything due.
        for (;;) {
            int64_t best = -1;
            for (int64_t i = 0; i < s_ntimers; i++)
                if ((intptr_t)words(s_timers)[i * 3] <= now &&
                    (best < 0 || (intptr_t)words(s_timers)[i * 3] < (intptr_t)words(s_timers)[best * 3])) best = i;
            if (best < 0) break;
            csrt_Task* t = words(s_timers)[best * 3 + 1];
            CancellationSource* token = words(s_timers)[best * 3 + 2];
            remove_timer_locked(best);
            pthread_mutex_unlock(&s_tlock);
            if (t) enqueue(K_TIMER_TASK, t, NULL); else enqueue(K_TIMER_CANCEL, token, NULL);
            pthread_mutex_lock(&s_tlock);
        }
        // Starvation: queued work but no dequeue since the last check -> add a worker.
        if (now - last_check >= 100) {
            last_check = now;
            pthread_mutex_lock(&s_qlock);
            if (s_qcount > 0 && s_idle == 0 && s_dequeued == last_progress && s_workers < MAX_WORKERS) spawn_worker_locked();
            last_progress = s_dequeued;
            pthread_mutex_unlock(&s_qlock);
        }
        // Sleep until the next deadline (at most 100 ms, for the starvation check).
        int64_t next = now + 100;
        for (int64_t i = 0; i < s_ntimers; i++)
            if ((intptr_t)words(s_timers)[i * 3] < next) next = (intptr_t)words(s_timers)[i * 3];
        struct timespec ts;
        clock_gettime(CLOCK_MONOTONIC, &ts);
        int64_t wait = next - now_ms();
#if defined(__APPLE__) || defined(_WIN32)
        if (wait > 0) {
            struct timespec relative = { (time_t)(wait / 1000), (long)(wait % 1000) * 1000000 };
            pthread_cond_timedwait_relative_np(&s_tcond, &s_tlock, &relative);
        }
#else
        if (wait > 0) {
            ts.tv_sec += wait / 1000;
            ts.tv_nsec += (wait % 1000) * 1000000;
            if (ts.tv_nsec >= 1000000000) { ts.tv_sec++; ts.tv_nsec -= 1000000000; }
            pthread_cond_timedwait(&s_tcond, &s_tlock, &ts);
        }
#endif
    }
}

csrt_Task* csrt_task_delay_token(int32_t ms, CancellationSource* token);
void csrt_token_register(CancellationSource* token, void* action);

csrt_Task* csrt_task_delay(int32_t ms) { return csrt_task_delay_token(ms, NULL); }

csrt_Task* csrt_task_delay_token(int32_t ms, CancellationSource* token) {
    if (token && __atomic_load_n(&token->canceled, __ATOMIC_ACQUIRE)) { csrt_Task* t = csrt_task_new(0); t->state = T_CANCELED; return t; }
    if (ms == 0) return csrt_task_completed_task();
    if (ms < -1) {
        fflush(stdout);
        fputs("Unhandled exception. System.ArgumentOutOfRangeException: The value needs to be either -1 (signifying an infinite timeout), 0 or a positive integer. (Parameter 'millisecondsDelay')\n", stderr);
        abort();
    }
    csrt_Task* t = csrt_task_new(0);
    if (ms == -1 && !token) return t;  // never completes
    if (token) csrt_token_register(token, t);  // .NET: the delay's cancellation is a token callback
    if (ms != -1) add_timer(now_ms() + ms, t, token);
    return t;
}

// ---------------------------------------------------------------------------------------------
// CancellationTokenSource / CancellationToken (a token is its source pointer; default = null)
// ---------------------------------------------------------------------------------------------
CancellationSource* csrt_cts_new(void) { return csrt_alloc(&csrt_CancellationTokenSource_MT.header, sizeof(CancellationSource)); }

void csrt_cts_cancel(CancellationSource* cts) {
    if (!cts) csrt_throw_null_reference();
    pthread_mutex_lock(&s_tlock);
    if (cts->canceled) { pthread_mutex_unlock(&s_tlock); return; }
    __atomic_store_n(&cts->canceled, 1, __ATOMIC_RELEASE);
    // Timers of delays bound to this token are dropped; the delays themselves are canceled below,
    // as token callbacks (they registered one when created, as in .NET).
    for (int64_t i = 0; i < s_ntimers;) {
        if (words(s_timers)[i * 3 + 2] == cts && words(s_timers)[i * 3 + 1]) remove_timer_locked(i);
        else i++;
    }
    csrt_Array* callbacks = cts->callbacks;
    int32_t ncallbacks = cts->ncallbacks;
    pthread_mutex_unlock(&s_tlock);
    // Registered callbacks run synchronously, most recently registered first (like .NET).
    for (int32_t i = ncallbacks - 1; i >= 0; i--) {
        void* entry = words(callbacks)[i];
        if (((csrt_Object*)entry)->mt == &csrt_Task_MT.header) { csrt_task_try_cancel(entry); continue; }
        Delegate* d = entry;
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

bool csrt_token_is_cancellation_requested(CancellationSource* token) { return token && __atomic_load_n(&token->canceled, __ATOMIC_ACQUIRE); }

void csrt_token_throw_if_cancellation_requested(CancellationSource* token) {
    if (csrt_token_is_cancellation_requested(token)) csrt_throw_object(csrt_operation_canceled_exception_new());
}

/// Token.Register(Action); the runtime also registers Task.Delay tasks here (canceled, not invoked).
void csrt_token_register(CancellationSource* token, void* action) {
    if (!action) csrt_throw_null_reference();
    if (!token) return;  // CancellationToken.None never cancels
    pthread_mutex_lock(&s_tlock);
    if (token->canceled) {
        pthread_mutex_unlock(&s_tlock);
        if (((csrt_Object*)action)->mt == &csrt_Task_MT.header) { csrt_task_try_cancel(action); return; }
        Delegate* d = action;
        ((void (*)(void*))d->fn)(d->target);
        return;
    }
    int64_t cap = token->callbacks ? token->callbacks->length : 0;
    if (token->ncallbacks == cap) {
        csrt_Array* grown = csrt_new_array(sizeof(void*), cap ? cap * 2 : 4);
        if (token->callbacks) memcpy(grown->data, token->callbacks->data, (size_t)token->ncallbacks * sizeof(void*));
        token->callbacks = grown;
    }
    words(token->callbacks)[token->ncallbacks++] = action;
    pthread_mutex_unlock(&s_tlock);
}

// ---------------------------------------------------------------------------------------------
// Combinators and Task.Run
// ---------------------------------------------------------------------------------------------
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
        __atomic_add_fetch(&s->remaining, 1, __ATOMIC_ACQ_REL);
        if (!add_continuation(t, K_WHEN_ALL, s)) when_all_step(s);
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
        if (task_state(t) != T_PENDING) { when_any_hit(result, t); return result; }
    }
    for (int64_t i = 0; i < tasks->length; i++)
        if (!add_continuation(words(tasks)[i], K_WHEN_ANY, result)) when_any_hit(result, words(tasks)[i]);
    return result;
}

/// Task.Run(Action): runs on the thread pool.
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

/// Task.Run(Func<T>) for a non-task T: `thunk(pack)` (compiler-generated per T) invokes the
/// delegate and stores the result into the task's payload. pack = { mt, delegate, task }.
typedef struct { const csrt_MethodTable* mt; void* del; csrt_Task* task; void (*thunk)(void*); } RunThunk;

csrt_Task* csrt_task_run_thunk(void (*thunk)(void*), void* del, int32_t payload_size) {
    if (!del) csrt_throw_null_reference();
    csrt_Task* t = csrt_task_new(payload_size);
    RunThunk* pack = csrt_alloc(&csrt_Object_MT.header, sizeof(RunThunk));
    pack->del = del;
    pack->task = t;
    pack->thunk = thunk;
    enqueue(K_RUN_THUNK, pack, t);
    return t;
}

static void run_item(intptr_t kind, void* a, void* b) {
    switch (kind) {
        case K_RESUME: csrt_coro_resume(a); break;
        case K_INVOKE: invoke_action(a); break;
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
            else if (!inner || !add_continuation(inner, K_CHAIN, b)) chain(inner, b);
            break;
        }
        case K_RUN_THUNK: {
            RunThunk* pack = a;
            void* exception = csrt_call_catching_void(pack->thunk, pack);
            if (exception) csrt_task_fault(b, exception);
            else csrt_task_complete(b);
            break;
        }
        case K_TIMER_TASK: csrt_task_try_complete(a); break;
        case K_TIMER_CANCEL: csrt_cts_cancel(a); break;
    }
}

/// Blocks the calling thread until `t` completes (async Main, .Result, .Wait(), GetResult()).
void csrt_task_wait(csrt_Task* t) {
    if (csrt_task_is_completed(t)) return;
    pthread_mutex_lock(&s_qlock);
    if (!s_pool_started) pool_start_locked();
    if (tl_worker) {
        // A pool worker blocking on a task: make sure someone else can run the work it waits for.
        s_blocked++;
        if (s_idle == 0 && s_workers - s_blocked <= 0 && s_workers < MAX_WORKERS) spawn_worker_locked();
    }
    pthread_mutex_unlock(&s_qlock);
    atomic_fetch_add(&s_waiters, 1);
    pthread_mutex_lock(&s_wlock);
    while (!csrt_task_is_completed(t)) pthread_cond_wait(&s_wcond, &s_wlock);
    pthread_mutex_unlock(&s_wlock);
    atomic_fetch_sub(&s_waiters, 1);
    if (tl_worker) {
        pthread_mutex_lock(&s_qlock);
        s_blocked--;
        pthread_mutex_unlock(&s_qlock);
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
// finishes, and parks it here; the C caller (same thread) rethrows it to the foreach. Thread-local,
// and the GC scans every thread's TLS.
static _Thread_local void* s_iterator_exception;

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
    done->state = T_DONE;
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
    if (it->state == 2) { t->state = T_DONE; return t; }
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
