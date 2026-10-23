// WASI (wasm32-wasip1) support: what wasi-libc lacks for a single-threaded program.
//
// WASI preview 1 has no threads, and wasi-libc for wasm32-wasip1 declares the pthread API without
// implementing it. csrt uses pthreads for locks, condition variables and one-time initialization;
// with one thread these reduce to bookkeeping:
//  * a mutex is a lock count (trylock reports EBUSY while it is held, as Monitor.TryEnter expects);
//  * waiting on a condition variable with a deadline sleeps until the deadline (nothing else can
//    signal it); waiting without one can never end, which is reported as a deadlock instead of hanging;
//  * creating a thread fails. Thread.Start throws PlatformNotSupportedException before getting here
//    (threads.c), as .NET does in single-threaded WebAssembly builds; the task scheduler runs work
//    items on the main thread (async.c).
// See docs/adr/0011-wasm32.md.
#ifdef __wasi__
#define _GNU_SOURCE
#include "csrt.h"

#include <errno.h>
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>

static int* lock_count(pthread_mutex_t* m) { return &m->__u.__i[0]; }

int pthread_mutex_init(pthread_mutex_t* m, const pthread_mutexattr_t* attr) { (void)attr; *lock_count(m) = 0; return 0; }
int pthread_mutex_destroy(pthread_mutex_t* m) { (void)m; return 0; }
int pthread_mutex_lock(pthread_mutex_t* m) { ++*lock_count(m); return 0; }
int pthread_mutex_unlock(pthread_mutex_t* m) { if (*lock_count(m) > 0) --*lock_count(m); return 0; }

int pthread_mutex_trylock(pthread_mutex_t* m) {
    if (*lock_count(m) > 0) return EBUSY;
    *lock_count(m) = 1;
    return 0;
}

static void sleep_until(clockid_t clock, const struct timespec* deadline) {
    while (clock_nanosleep(clock, TIMER_ABSTIME, deadline, NULL) == EINTR) {}
}

int pthread_mutex_timedlock(pthread_mutex_t* m, const struct timespec* deadline) {
    if (pthread_mutex_trylock(m) == 0) return 0;
    sleep_until(CLOCK_REALTIME, deadline);  // held by this very thread: nobody else can release it
    return ETIMEDOUT;
}

// A condition variable remembers its clock (pthread_condattr_setclock) in its first word:
// 0 (also PTHREAD_COND_INITIALIZER) is CLOCK_REALTIME, 1 is CLOCK_MONOTONIC.
int pthread_condattr_init(pthread_condattr_t* a) { a->__attr = 0; return 0; }
int pthread_condattr_destroy(pthread_condattr_t* a) { (void)a; return 0; }
int pthread_condattr_setclock(pthread_condattr_t* a, clockid_t clock) { a->__attr = clock == CLOCK_MONOTONIC; return 0; }

int pthread_cond_init(pthread_cond_t* c, const pthread_condattr_t* a) {
    c->__u.__i[0] = a && a->__attr == 1;
    return 0;
}
int pthread_cond_destroy(pthread_cond_t* c) { (void)c; return 0; }
int pthread_cond_signal(pthread_cond_t* c) { (void)c; return 0; }
int pthread_cond_broadcast(pthread_cond_t* c) { (void)c; return 0; }

int pthread_cond_timedwait(pthread_cond_t* c, pthread_mutex_t* m, const struct timespec* deadline) {
    (void)m;
    sleep_until(c->__u.__i[0] ? CLOCK_MONOTONIC : CLOCK_REALTIME, deadline);
    return ETIMEDOUT;
}

int pthread_cond_wait(pthread_cond_t* c, pthread_mutex_t* m) {
    (void)c; (void)m;
    fflush(stdout);
    fputs("csharpc: deadlock: the program waits for a signal from another thread, but WebAssembly (WASI preview 1) "
          "runs a single thread\n", stderr);
    csrt_fail_unhandled();
}

int pthread_once(pthread_once_t* once, void (*init)(void)) {
    if (*once == 0) {
        *once = 1;
        init();
    }
    return 0;
}

static struct __pthread* const s_main_thread = (struct __pthread*)1;
pthread_t pthread_self(void) { return s_main_thread; }
int (pthread_equal)(pthread_t a, pthread_t b) { return a == b; }

int pthread_attr_init(pthread_attr_t* a) { (void)a; return 0; }
int pthread_attr_destroy(pthread_attr_t* a) { (void)a; return 0; }
int pthread_attr_setdetachstate(pthread_attr_t* a, int state) { (void)a; (void)state; return 0; }
int pthread_attr_setstacksize(pthread_attr_t* a, size_t size) { (void)a; (void)size; return 0; }

int pthread_create(pthread_t* thread, const pthread_attr_t* attr, void* (*start)(void*), void* arg) {
    (void)thread; (void)attr; (void)start; (void)arg;
    return ENOTSUP;
}
#endif
