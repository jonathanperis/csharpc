// csrt threads: System.Threading.Thread, Monitor (`lock`), System.Threading.Lock, wait handles,
// a worker pool for Parallel.For/ForEach/Invoke. Built on pthreads; see docs/13-threading.md.
//
// Every managed thread registers with the collector (gc.c) before it touches the managed heap and
// unregisters before it exits. Blocking calls here are fine during a collection: a blocked thread
// is still stopped by the GC's signal handshake (SA_RESTART restarts interrupted syscalls; sleeps
// resume with their remaining time).
#define _GNU_SOURCE
#include "csrt.h"
#include "gc.h"

#include <errno.h>
#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

csrt_String* csrt_string_from_utf8(const char* text);

static const csrt_MethodTable mt_thread_state = { NULL, "System.Threading.ThreadStateException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_sync_lock = { NULL, "System.Threading.SynchronizationLockException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_argument_null = { NULL, "System.ArgumentNullException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_argument_out_of_range = { NULL, "System.ArgumentOutOfRangeException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_invalid_operation = { NULL, "System.InvalidOperationException", 0, 0, NULL, 0, 0 };

_Noreturn static void throw_msg(const csrt_MethodTable* mt, const char* message) {
    csrt_throw(mt, csrt_string_from_utf8(message));
}

// ---------------------------------------------------------------------------------------------
// Delegates: { mt, target, fn, invocation_list } (see delegates.c)
// ---------------------------------------------------------------------------------------------
typedef struct Delegate {
    const csrt_MethodTable* mt;
    void* target;
    void* fn;
    csrt_Array* list;
} Delegate;

static void invoke_void(Delegate* d, bool with_arg, void* arg) {
    if (!d) csrt_throw_null_reference();
    int64_t n = d->list ? d->list->length : 1;
    for (int64_t i = 0; i < n; i++) {
        Delegate* e = d->list ? ((Delegate**)d->list->data)[i] : d;
        if (with_arg) ((void (*)(void*, void*))e->fn)(e->target, arg);
        else ((void (*)(void*))e->fn)(e->target);
    }
}

// ---------------------------------------------------------------------------------------------
// Time helpers
// ---------------------------------------------------------------------------------------------
static void deadline_after(struct timespec* ts, int64_t ms) {
    clock_gettime(CLOCK_REALTIME, ts);
    ts->tv_sec += ms / 1000;
    ts->tv_nsec += (ms % 1000) * 1000000;
    if (ts->tv_nsec >= 1000000000) { ts->tv_sec++; ts->tv_nsec -= 1000000000; }
}

/// Thread.Sleep: resumes with the remaining time when a GC signal interrupts it.
void csrt_thread_sleep(int32_t ms) {
    if (ms < -1) throw_msg(&mt_argument_out_of_range, "Number must be either non-negative and less than or equal to Int32.MaxValue or -1. (Parameter 'millisecondsTimeout')");
    if (ms == 0) { sched_yield(); return; }
    if (ms == -1) { for (;;) pause(); }
    struct timespec req = { ms / 1000, (long)(ms % 1000) * 1000000 }, rem;
    while (nanosleep(&req, &rem) != 0 && errno == EINTR) req = rem;
}

bool csrt_thread_yield(void) { sched_yield(); return true; }

// ---------------------------------------------------------------------------------------------
// System.Threading.Thread
// ---------------------------------------------------------------------------------------------
enum { THREAD_UNSTARTED, THREAD_RUNNING, THREAD_STOPPED };

typedef struct csrt_Thread {
    const csrt_MethodTable* mt;
    Delegate* start;
    void* parameter;
    csrt_String* name;
    int32_t id;
    _Atomic int32_t state;
    bool background;
    bool parameterized;
    bool pool;               // a pool worker (always background)
} csrt_Thread;

const csrt_BuiltinMethodTable csrt_Thread_MT = {
    { &csrt_Object_MT.header, "System.Threading.Thread", sizeof(csrt_Thread), 0, NULL, 0, 0 },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

static _Thread_local __attribute__((tls_model("local-exec"))) csrt_Thread* tl_current;  // rooted: the GC scans every thread's TLS
static atomic_int s_next_id = 2;               // the main thread is 1, like .NET
static pthread_mutex_t s_join_lock = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t s_join_cond = PTHREAD_COND_INITIALIZER;
static int s_foreground;                       // started, unfinished foreground threads

static csrt_Thread* thread_alloc(void) {
    csrt_Thread* t = csrt_alloc(&csrt_Thread_MT.header, sizeof(csrt_Thread));
    t->id = atomic_fetch_add(&s_next_id, 1);
    return t;
}

csrt_Thread* csrt_thread_new(Delegate* start, bool parameterized) {
    if (!start) throw_msg(&mt_argument_null, "Value cannot be null. (Parameter 'start')");
    csrt_Thread* t = thread_alloc();
    t->start = start;
    t->parameterized = parameterized;
    return t;
}

csrt_Thread* csrt_thread_current(void) {
    if (!tl_current) {
        // The main thread (or any thread csrt didn't start) gets a Thread object on first use.
        csrt_Thread* t = csrt_alloc(&csrt_Thread_MT.header, sizeof(csrt_Thread));
        t->id = 1;
        atomic_store(&t->state, THREAD_RUNNING);
        tl_current = t;
    }
    return tl_current;
}

static void thread_finished(csrt_Thread* t) {
    pthread_mutex_lock(&s_join_lock);
    atomic_store(&t->state, THREAD_STOPPED);
    if (!t->background) s_foreground--;
    pthread_cond_broadcast(&s_join_cond);
    pthread_mutex_unlock(&s_join_lock);
}

static void* thread_main(void* arg) {
    csrt_GcThread* gt = arg;
    csrt_gc_thread_attach(gt);
    csrt_Thread* t = gt->managed;
    tl_current = t;
    // An exception escaping the delegate is unhandled: eh.c reports it and aborts the process,
    // exactly like an unhandled exception on a .NET thread.
    invoke_void(t->start, t->parameterized, t->parameter);
    thread_finished(t);
    tl_current = NULL;
    csrt_gc_thread_detach();
    return NULL;
}

static void spawn(csrt_Thread* t) {
    csrt_GcThread* gt = csrt_gc_thread_prepare(t);
    pthread_attr_t attr;
    pthread_attr_init(&attr);
    pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_DETACHED);
    pthread_attr_setstacksize(&attr, (size_t)8 * 1024 * 1024);
    pthread_t tid;
    int rc = gt ? pthread_create(&tid, &attr, thread_main, gt) : ENOMEM;
    pthread_attr_destroy(&attr);
    if (rc != 0) {
        if (gt) csrt_gc_thread_abandon(gt);
        thread_finished(t);
        throw_msg(&mt_invalid_operation, "Thread failed to start.");
    }
}

static void thread_start(csrt_Thread* t, void* parameter, bool has_parameter) {
    if (!t) csrt_throw_null_reference();
    if (has_parameter && !t->parameterized)
        throw_msg(&mt_invalid_operation, "The thread was created with a ThreadStart delegate that does not accept a parameter.");
    pthread_mutex_lock(&s_join_lock);
    if (atomic_load(&t->state) != THREAD_UNSTARTED) {
        pthread_mutex_unlock(&s_join_lock);
        throw_msg(&mt_thread_state, "Thread is running or terminated; it cannot restart.");
    }
    atomic_store(&t->state, THREAD_RUNNING);
    if (!t->background) s_foreground++;
    pthread_mutex_unlock(&s_join_lock);
    t->parameter = parameter;
    spawn(t);
}

void csrt_thread_start(csrt_Thread* t) { thread_start(t, NULL, false); }
void csrt_thread_start_with(csrt_Thread* t, void* parameter) { thread_start(t, parameter, true); }

bool csrt_thread_join_timeout(csrt_Thread* t, int32_t ms) {
    if (!t) csrt_throw_null_reference();
    if (atomic_load(&t->state) == THREAD_UNSTARTED) throw_msg(&mt_thread_state, "Thread has not been started.");
    if (t == tl_current) return ms == -1 ? (csrt_thread_sleep(-1), true) : (csrt_thread_sleep(ms), false);
    struct timespec deadline;
    if (ms >= 0) deadline_after(&deadline, ms);
    pthread_mutex_lock(&s_join_lock);
    bool done = true;
    while (atomic_load(&t->state) != THREAD_STOPPED) {
        if (ms < 0) pthread_cond_wait(&s_join_cond, &s_join_lock);
        else if (pthread_cond_timedwait(&s_join_cond, &s_join_lock, &deadline) == ETIMEDOUT) {
            done = atomic_load(&t->state) == THREAD_STOPPED;
            break;
        }
    }
    pthread_mutex_unlock(&s_join_lock);
    return done;
}

void csrt_thread_join(csrt_Thread* t) { csrt_thread_join_timeout(t, -1); }

bool csrt_thread_is_alive(csrt_Thread* t) {
    if (!t) csrt_throw_null_reference();
    return atomic_load(&t->state) == THREAD_RUNNING;
}

int32_t csrt_thread_id(csrt_Thread* t) { if (!t) csrt_throw_null_reference(); return t->id; }
csrt_String* csrt_thread_get_name(csrt_Thread* t) { if (!t) csrt_throw_null_reference(); return t->name; }
void csrt_thread_set_name(csrt_Thread* t, csrt_String* name) {
    if (!t) csrt_throw_null_reference();
    if (t->name) throw_msg(&mt_invalid_operation, "This property has already been set and cannot be modified.");
    t->name = name;
}
bool csrt_thread_get_background(csrt_Thread* t) { if (!t) csrt_throw_null_reference(); return t->background; }
void csrt_thread_set_background(csrt_Thread* t, bool background) {
    if (!t) csrt_throw_null_reference();
    pthread_mutex_lock(&s_join_lock);
    if (t->background != background && atomic_load(&t->state) == THREAD_RUNNING && t->id != 1)
        s_foreground += background ? -1 : 1;
    t->background = background;
    pthread_cond_broadcast(&s_join_cond);
    pthread_mutex_unlock(&s_join_lock);
}

/// Main returned: like .NET, the process lives on until every foreground thread has finished.
void csrt_threads_wait_foreground(void) {
    pthread_mutex_lock(&s_join_lock);
    while (s_foreground > 0) pthread_cond_wait(&s_join_cond, &s_join_lock);
    pthread_mutex_unlock(&s_join_lock);
}

// ---------------------------------------------------------------------------------------------
// Monitors (`lock`, Monitor.*, System.Threading.Lock). The GC never moves objects, so a side
// table keyed by address works; an entry lives while some thread holds, waits for or enters it.
// ---------------------------------------------------------------------------------------------
typedef struct Monitor {
    void* obj;
    pthread_mutex_t mutex;
    pthread_cond_t cond;      // Monitor.Wait / Pulse
    _Atomic(pthread_t) owner;
    atomic_bool owned;
    int recursion;
    int refs;                 // threads holding a reference (enter/hold/wait)
    struct Monitor* next;
} Monitor;

#define MONITOR_BUCKETS 256
static struct { pthread_mutex_t lock; Monitor* head; } s_monitors[MONITOR_BUCKETS];
static pthread_once_t s_monitors_once = PTHREAD_ONCE_INIT;

static void monitors_init(void) {
    for (int i = 0; i < MONITOR_BUCKETS; i++) pthread_mutex_init(&s_monitors[i].lock, NULL);
}

static inline size_t bucket_of(void* obj) { return ((uintptr_t)obj >> 4) * 0x9E3779B97F4A7C15ull >> 56; }

static Monitor* monitor_ref(void* obj, bool create) {
    pthread_once(&s_monitors_once, monitors_init);
    size_t b = bucket_of(obj) % MONITOR_BUCKETS;
    pthread_mutex_lock(&s_monitors[b].lock);
    Monitor* m = s_monitors[b].head;
    while (m && m->obj != obj) m = m->next;
    if (!m && create) {
        m = calloc(1, sizeof *m);
        if (!m) abort();
        m->obj = obj;
        pthread_mutex_init(&m->mutex, NULL);
        pthread_cond_init(&m->cond, NULL);
        m->next = s_monitors[b].head;
        s_monitors[b].head = m;
    }
    if (m) m->refs++;
    pthread_mutex_unlock(&s_monitors[b].lock);
    return m;
}

static void monitor_unref(Monitor* m) {
    size_t b = bucket_of(m->obj) % MONITOR_BUCKETS;
    pthread_mutex_lock(&s_monitors[b].lock);
    if (--m->refs == 0) {
        for (Monitor** p = &s_monitors[b].head; *p; p = &(*p)->next)
            if (*p == m) { *p = m->next; break; }
        pthread_mutex_destroy(&m->mutex);
        pthread_cond_destroy(&m->cond);
        free(m);
    }
    pthread_mutex_unlock(&s_monitors[b].lock);
}

static inline bool owns(Monitor* m) { return m && atomic_load(&m->owned) && pthread_equal(atomic_load(&m->owner), pthread_self()); }

static void check_obj(void* obj) { if (!obj) throw_msg(&mt_argument_null, "Value cannot be null. (Parameter 'obj')"); }

void csrt_monitor_enter(void* obj) {
    check_obj(obj);
    Monitor* m = monitor_ref(obj, true);
    if (owns(m)) { m->recursion++; return; }
    pthread_mutex_lock(&m->mutex);
    atomic_store(&m->owner, pthread_self());
    atomic_store(&m->owned, true);
    m->recursion = 1;
}

/// Monitor.TryEnter(obj[, timeout]). ms < 0 = infinite, 0 = don't wait.
bool csrt_monitor_try_enter(void* obj, int32_t ms) {
    check_obj(obj);
    Monitor* m = monitor_ref(obj, true);
    if (owns(m)) { m->recursion++; return true; }
    int rc;
    if (ms < 0) rc = pthread_mutex_lock(&m->mutex);
    else if (ms == 0) rc = pthread_mutex_trylock(&m->mutex);
    else { struct timespec d; deadline_after(&d, ms); rc = pthread_mutex_timedlock(&m->mutex, &d); }
    if (rc != 0) { monitor_unref(m); return false; }
    atomic_store(&m->owner, pthread_self());
    atomic_store(&m->owned, true);
    m->recursion = 1;
    return true;
}

static Monitor* owned_monitor(void* obj) {
    check_obj(obj);
    Monitor* m = monitor_ref(obj, false);
    if (!owns(m)) {
        if (m) monitor_unref(m);
        throw_msg(&mt_sync_lock, "Object synchronization method was called from an unsynchronized block of code.");
    }
    return m;
}

void csrt_monitor_exit(void* obj) {
    Monitor* m = owned_monitor(obj);  // +1 ref for this lookup
    if (--m->recursion == 0) {
        atomic_store(&m->owned, false);
        pthread_mutex_unlock(&m->mutex);
    }
    monitor_unref(m);  // the lookup
    monitor_unref(m);  // the matching Enter
}

bool csrt_monitor_is_entered(void* obj) {
    check_obj(obj);
    Monitor* m = monitor_ref(obj, false);
    bool result = owns(m);
    if (m) monitor_unref(m);
    return result;
}

/// Monitor.Wait(obj[, timeout]): releases the lock (even when held recursively) and reacquires it.
bool csrt_monitor_wait(void* obj, int32_t ms) {
    Monitor* m = owned_monitor(obj);
    int recursion = m->recursion;
    atomic_store(&m->owned, false);
    bool signalled = true;
    if (ms < 0) pthread_cond_wait(&m->cond, &m->mutex);
    else {
        struct timespec d;
        deadline_after(&d, ms);
        signalled = pthread_cond_timedwait(&m->cond, &m->mutex, &d) != ETIMEDOUT;
    }
    atomic_store(&m->owner, pthread_self());
    atomic_store(&m->owned, true);
    m->recursion = recursion;
    monitor_unref(m);
    return signalled;
}

void csrt_monitor_pulse(void* obj) { Monitor* m = owned_monitor(obj); pthread_cond_signal(&m->cond); monitor_unref(m); }
void csrt_monitor_pulse_all(void* obj) { Monitor* m = owned_monitor(obj); pthread_cond_broadcast(&m->cond); monitor_unref(m); }

// System.Threading.Lock (.NET 9): a plain object whose monitor is the lock. `lock (l)` lowers to
// l.EnterScope() + Scope.Dispose(); a Scope is represented by the Lock pointer itself.
const csrt_BuiltinMethodTable csrt_Lock_MT = {
    { &csrt_Object_MT.header, "System.Threading.Lock", sizeof(csrt_Object), 0, NULL, 0, 0 },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

void* csrt_lock_new(void) { return csrt_alloc(&csrt_Lock_MT.header, sizeof(csrt_Object)); }

static void lock_exit(void* lock) {
    if (!lock) csrt_throw_null_reference();
    Monitor* m = monitor_ref(lock, false);
    if (!owns(m)) {
        if (m) monitor_unref(m);
        throw_msg(&mt_sync_lock, "The calling thread does not hold the lock.");
    }
    if (--m->recursion == 0) {
        atomic_store(&m->owned, false);
        pthread_mutex_unlock(&m->mutex);
    }
    monitor_unref(m);
    monitor_unref(m);
}

void csrt_lock_enter(void* lock) { if (!lock) csrt_throw_null_reference(); csrt_monitor_enter(lock); }
void csrt_lock_exit(void* lock) { lock_exit(lock); }
bool csrt_lock_try_enter(void* lock, int32_t ms) { if (!lock) csrt_throw_null_reference(); return csrt_monitor_try_enter(lock, ms); }
void* csrt_lock_enter_scope(void* lock) { csrt_lock_enter(lock); return lock; }
bool csrt_lock_is_held(void* lock) { if (!lock) csrt_throw_null_reference(); return csrt_monitor_is_entered(lock); }

// ---------------------------------------------------------------------------------------------
// Wait handles: ManualResetEventSlim / ManualResetEvent / AutoResetEvent / SemaphoreSlim /
// CountdownEvent. One runtime object kind with a count and a mode.
// ---------------------------------------------------------------------------------------------
enum { EVENT_MANUAL, EVENT_AUTO, EVENT_SEMAPHORE, EVENT_COUNTDOWN };

typedef struct {
    const csrt_MethodTable* mt;
    pthread_mutex_t* mutex;    // malloc'd: pthread objects must not move (they don't) or be scanned
    pthread_cond_t* cond;
    int32_t count;             // set (1/0), semaphore count, or countdown remaining
    int32_t max;
    int32_t initial;
    int32_t kind;
} csrt_Event;

#define EVENT_MT(sym, name) \
    const csrt_BuiltinMethodTable sym = { { &csrt_Object_MT.header, name, sizeof(csrt_Event), CSRT_MT_DISPOSABLE, NULL, 0, 0 }, \
        { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default } };
EVENT_MT(csrt_ManualResetEventSlim_MT, "System.Threading.ManualResetEventSlim")
EVENT_MT(csrt_ManualResetEvent_MT, "System.Threading.ManualResetEvent")
EVENT_MT(csrt_AutoResetEvent_MT, "System.Threading.AutoResetEvent")
EVENT_MT(csrt_SemaphoreSlim_MT, "System.Threading.SemaphoreSlim")
EVENT_MT(csrt_CountdownEvent_MT, "System.Threading.CountdownEvent")

csrt_Event* csrt_event_new(int32_t kind, int32_t count, int32_t max) {
    const csrt_MethodTable* mt = kind == EVENT_AUTO ? &csrt_AutoResetEvent_MT.header
        : kind == EVENT_SEMAPHORE ? &csrt_SemaphoreSlim_MT.header
        : kind == EVENT_COUNTDOWN ? &csrt_CountdownEvent_MT.header
        : max == 1 ? &csrt_ManualResetEvent_MT.header : &csrt_ManualResetEventSlim_MT.header;
    if (kind == EVENT_SEMAPHORE && (count < 0 || (max > 0 && count > max)))
        throw_msg(&mt_argument_out_of_range, "Specified argument was out of the range of valid values. (Parameter 'initialCount')");
    if (kind == EVENT_COUNTDOWN && count < 0)
        throw_msg(&mt_argument_out_of_range, "Specified argument was out of the range of valid values. (Parameter 'initialCount')");
    csrt_Event* e = csrt_alloc(mt, sizeof(csrt_Event));
    e->mutex = malloc(sizeof(pthread_mutex_t));
    e->cond = malloc(sizeof(pthread_cond_t));
    if (!e->mutex || !e->cond) abort();
    pthread_mutex_init(e->mutex, NULL);
    pthread_cond_init(e->cond, NULL);
    e->count = count;
    e->initial = count;
    e->max = max;
    e->kind = kind;
    return e;
}

/// Set (events) / Release (semaphore, returns the previous count).
int32_t csrt_event_set(csrt_Event* e, int32_t n) {
    if (!e) csrt_throw_null_reference();
    pthread_mutex_lock(e->mutex);
    int32_t previous = e->count;
    if (e->kind == EVENT_SEMAPHORE) {
        if (n < 1) { pthread_mutex_unlock(e->mutex); throw_msg(&mt_argument_out_of_range, "Non-negative number required. (Parameter 'releaseCount')"); }
        if (e->max > 0 && e->count + n > e->max) {
            pthread_mutex_unlock(e->mutex);
            throw_msg(&mt_invalid_operation, "Adding the specified count to the semaphore would cause it to exceed its maximum count.");
        }
        e->count += n;
    } else if (e->kind == EVENT_COUNTDOWN) {
        if (n < 1 || n > e->count) {
            pthread_mutex_unlock(e->mutex);
            throw_msg(&mt_invalid_operation, "Invalid attempt made to decrement the event's count below zero.");
        }
        e->count -= n;
        previous = e->count == 0;  // Signal() returns true when the count reached zero
    } else {
        e->count = 1;
    }
    pthread_cond_broadcast(e->cond);
    pthread_mutex_unlock(e->mutex);
    return previous;
}

void csrt_event_reset(csrt_Event* e) {
    if (!e) csrt_throw_null_reference();
    pthread_mutex_lock(e->mutex);
    e->count = e->kind == EVENT_COUNTDOWN ? e->initial : 0;
    pthread_mutex_unlock(e->mutex);
}

void csrt_countdown_add(csrt_Event* e, int32_t n) {
    if (!e) csrt_throw_null_reference();
    pthread_mutex_lock(e->mutex);
    if (e->count == 0) { pthread_mutex_unlock(e->mutex); throw_msg(&mt_invalid_operation, "The event is already signaled and cannot be incremented."); }
    e->count += n;
    pthread_mutex_unlock(e->mutex);
}

bool csrt_event_is_set(csrt_Event* e) {
    if (!e) csrt_throw_null_reference();
    pthread_mutex_lock(e->mutex);
    bool set = e->kind == EVENT_COUNTDOWN ? e->count == 0 : e->count > 0;
    pthread_mutex_unlock(e->mutex);
    return set;
}

int32_t csrt_event_count(csrt_Event* e) { if (!e) csrt_throw_null_reference(); return e->count; }

/// Wait(timeout ms, -1 = infinite) / WaitOne: true when signalled.
bool csrt_event_wait(csrt_Event* e, int32_t ms) {
    if (!e) csrt_throw_null_reference();
    struct timespec deadline;
    if (ms > 0) deadline_after(&deadline, ms);
    pthread_mutex_lock(e->mutex);
    for (;;) {
        bool ready = e->kind == EVENT_COUNTDOWN ? e->count == 0 : e->count > 0;
        if (ready) {
            if (e->kind == EVENT_AUTO) e->count = 0;
            else if (e->kind == EVENT_SEMAPHORE) e->count--;
            pthread_mutex_unlock(e->mutex);
            return true;
        }
        if (ms == 0 || (ms > 0 && pthread_cond_timedwait(e->cond, e->mutex, &deadline) == ETIMEDOUT)) {
            bool now = e->kind == EVENT_COUNTDOWN ? e->count == 0 : e->count > 0;
            if (!now) { pthread_mutex_unlock(e->mutex); return false; }
            continue;
        }
        if (ms < 0) pthread_cond_wait(e->cond, e->mutex);
    }
}

// ---------------------------------------------------------------------------------------------
// Worker pool + Parallel.For / ForEach / Invoke
// ---------------------------------------------------------------------------------------------
// A parallel loop is a shared job: workers (and the calling thread) grab chunks of the index
// range with an atomic counter until it's exhausted. `body(context, i)` is a compiler-generated
// thunk that invokes the user's delegate for index i. The first exception thrown by an
// iteration stops the loop and is rethrown on the calling thread (.NET wraps it in an
// AggregateException; see docs/13-threading.md).
typedef struct Job {
    void (*body)(void* context, int64_t index);
    void* context;
    int64_t end, chunk;
    _Atomic int64_t next;
    atomic_int active;        // participants still running
    atomic_bool stop;
    _Atomic(void*) exception; // first exception thrown by an iteration on a pool worker
    pthread_mutex_t mutex;
    pthread_cond_t done;
} Job;

static pthread_mutex_t s_pool_lock = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t s_pool_cond = PTHREAD_COND_INITIALIZER;
static Job* s_pool_job;           // the job workers should help with (one at a time)
static uint64_t s_pool_generation;
static int s_pool_size;           // started workers

int32_t csrt_processor_count(void) {
    long n = sysconf(_SC_NPROCESSORS_ONLN);
    return n > 0 ? (int32_t)n : 1;
}

/// Runs chunks until the range is exhausted. An exception propagates to the caller.
static void run_chunks(Job* job) {
    while (!atomic_load(&job->stop)) {
        int64_t start = atomic_fetch_add(&job->next, job->chunk);
        if (start >= job->end) break;
        int64_t stop = start + job->chunk < job->end ? start + job->chunk : job->end;
        for (int64_t i = start; i < stop; i++) job->body(job->context, i);
    }
}

static void job_leave(Job* job) {
    pthread_mutex_lock(&job->mutex);
    if (atomic_fetch_sub(&job->active, 1) == 1) pthread_cond_broadcast(&job->done);
    pthread_mutex_unlock(&job->mutex);
}

void* csrt_trap_call(void (*fn)(void*), void* arg);

static void run_chunks_trampoline(void* job) { run_chunks(job); }

/// A pool worker's share of a job. An exception is captured (finally blocks have run) and handed
/// to the calling thread, which rethrows it once the loop has drained.
static void run_worker_share(Job* job) {
    void* exception = csrt_trap_call(run_chunks_trampoline, job);
    if (exception) {
        void* expected = NULL;
        atomic_compare_exchange_strong(&job->exception, &expected, exception);
        atomic_store(&job->stop, true);
    }
}

static void* pool_worker(void* arg) {
    csrt_GcThread* gt = arg;
    csrt_gc_thread_attach(gt);
    tl_current = gt->managed;
    uint64_t seen = 0;
    for (;;) {
        pthread_mutex_lock(&s_pool_lock);
        while (s_pool_generation == seen) pthread_cond_wait(&s_pool_cond, &s_pool_lock);
        seen = s_pool_generation;
        Job* job = s_pool_job;
        if (job) atomic_fetch_add(&job->active, 1);
        pthread_mutex_unlock(&s_pool_lock);
        if (!job) continue;
        run_worker_share(job);
        job_leave(job);
    }
    return NULL;
}

static void ensure_pool(int workers) {
    pthread_mutex_lock(&s_pool_lock);
    while (s_pool_size < workers) {
        csrt_Thread* t = thread_alloc();
        t->background = true;
        t->pool = true;
        atomic_store(&t->state, THREAD_RUNNING);
        csrt_GcThread* gt = csrt_gc_thread_prepare(t);
        pthread_attr_t attr;
        pthread_attr_init(&attr);
        pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_DETACHED);
        pthread_attr_setstacksize(&attr, (size_t)8 * 1024 * 1024);
        pthread_t tid;
        int rc = gt ? pthread_create(&tid, &attr, pool_worker, gt) : ENOMEM;
        pthread_attr_destroy(&attr);
        if (rc != 0) { if (gt) csrt_gc_thread_abandon(gt); break; }
        s_pool_size++;
    }
    pthread_mutex_unlock(&s_pool_lock);
}

// ---------------------------------------------------------------------------------------------
// Background managed threads for the task scheduler (runtime/async.c): thread-pool workers and the
// timer thread. They're `background` (never keep the process alive) and registered with the GC.
// ---------------------------------------------------------------------------------------------
typedef struct { csrt_GcThread* gt; void (*loop)(void); } LoopStart;

static void* loop_thread_main(void* arg) {
    LoopStart start = *(LoopStart*)arg;
    free(arg);
    csrt_gc_thread_attach(start.gt);
    tl_current = start.gt->managed;
    start.loop();  // never returns
    return NULL;
}

bool csrt_start_background_thread(void (*loop)(void)) {
    csrt_Thread* t = thread_alloc();
    t->background = true;
    atomic_store(&t->state, THREAD_RUNNING);
    csrt_GcThread* gt = csrt_gc_thread_prepare(t);
    LoopStart* start = malloc(sizeof *start);
    if (!gt || !start) { if (gt) csrt_gc_thread_abandon(gt); free(start); return false; }
    start->gt = gt;
    start->loop = loop;
    pthread_attr_t attr;
    pthread_attr_init(&attr);
    pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_DETACHED);
    pthread_attr_setstacksize(&attr, (size_t)8 * 1024 * 1024);
    pthread_t tid;
    int rc = pthread_create(&tid, &attr, loop_thread_main, start);
    pthread_attr_destroy(&attr);
    if (rc != 0) { csrt_gc_thread_abandon(gt); free(start); return false; }
    return true;
}

static pthread_mutex_t s_parallel_lock = PTHREAD_MUTEX_INITIALIZER;  // one parallel job at a time

/// Retires a job: no new helpers, wait for running ones. Runs on normal exit AND when an iteration
/// on the calling thread throws (cleanup attribute + -fexceptions), so the stack-allocated job
/// never outlives this frame.
static void job_retire(Job** slot) {
    Job* job = *slot;
    if (!job) return;
    atomic_store(&job->stop, true);
    pthread_mutex_lock(&s_pool_lock);
    s_pool_job = NULL;
    pthread_mutex_unlock(&s_pool_lock);
    job_leave(job);
    pthread_mutex_lock(&job->mutex);
    while (atomic_load(&job->active) > 0) pthread_cond_wait(&job->done, &job->mutex);
    pthread_mutex_unlock(&job->mutex);
    pthread_mutex_destroy(&job->mutex);
    pthread_cond_destroy(&job->done);
    pthread_mutex_unlock(&s_parallel_lock);
}

void csrt_parallel_for(int64_t from, int64_t to, void (*body)(void*, int64_t), void* context) {
    if (to <= from) return;
    int64_t count = to - from;
    int procs = csrt_processor_count();
    // Nested parallel loops (or loops on pool threads) just run inline.
    if (procs == 1 || count == 1 || (tl_current && tl_current->pool) || pthread_mutex_trylock(&s_parallel_lock) != 0) {
        for (int64_t i = from; i < to; i++) body(context, i);
        return;
    }
    ensure_pool(procs - 1);
    Job job;
    memset(&job, 0, sizeof job);
    job.body = body;
    job.context = context;
    job.end = to;
    // ~8 chunks per participant balances load without hammering the counter.
    job.chunk = count / ((int64_t)procs * 8);
    if (job.chunk < 1) job.chunk = 1;
    atomic_store(&job.next, from);
    atomic_store(&job.active, 1);  // the calling thread
    pthread_mutex_init(&job.mutex, NULL);
    pthread_cond_init(&job.done, NULL);
    void* exception = NULL;
    {
        __attribute__((cleanup(job_retire))) Job* active = &job;
        pthread_mutex_lock(&s_pool_lock);
        s_pool_job = &job;
        s_pool_generation++;
        pthread_cond_broadcast(&s_pool_cond);
        pthread_mutex_unlock(&s_pool_lock);
        run_chunks(&job);
    }
    exception = atomic_load(&job.exception);
    if (exception) csrt_throw_object(exception);
}

/// Parallel.Invoke(params Action[]): actions run as iterations of a parallel loop.
static void invoke_action(void* context, int64_t i) { invoke_void(((Delegate**)((csrt_Array*)context)->data)[i], false, NULL); }

void csrt_parallel_invoke(csrt_Array* actions) {
    if (!actions) throw_msg(&mt_argument_null, "Value cannot be null. (Parameter 'actions')");
    for (int64_t i = 0; i < actions->length; i++)
        if (!((Delegate**)actions->data)[i]) throw_msg(&mt_argument_null, "One of the actions was null. (Parameter 'actions')");
    csrt_parallel_for(0, actions->length, invoke_action, actions);
}

// ---------------------------------------------------------------------------------------------
// Console output under threads: .NET's Console.Out is synchronized, so a WriteLine never
// interleaves with another thread's. The compiler brackets each Console call with these.
// ---------------------------------------------------------------------------------------------
void csrt_console_lock(void) { if (csrt_gc_is_multithreaded()) flockfile(stdout); }
void csrt_console_unlock(void) { if (csrt_gc_is_multithreaded()) funlockfile(stdout); }
