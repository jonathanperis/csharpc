// POSIX threads and clocks as csrt uses them. Unix and WASI: the system's <pthread.h> (wasi.c fills
// in wasi-libc's gaps). Windows: the subset csrt needs, over SRW locks, condition variables and
// one-time initialization (win32.c), instead of MinGW's winpthreads. Linking winpthreads statically
// costs every executable ~40 KB: pthread_self, clock_gettime and nanosleep all pull its thread.o,
// which drags in the rest of the library and strsafe. The csrt_ names keep these from ever
// colliding with winpthreads or with the declarations in MinGW's <time.h> and <sched.h>.
//
// Only what csrt uses is declared, so new uses fail to compile on Windows instead of silently
// changing behaviour: threads are always detached (no pthread_join), condition variables wait on
// CLOCK_REALTIME deadlines (no pthread_condattr_setclock; the timer thread waits with relative
// timeouts), and pthread_mutex_timedlock is threads.c's trylock-polling fallback, as on macOS.
#ifndef CSRT_PTHREAD_H
#define CSRT_PTHREAD_H

#ifndef _WIN32
#include <pthread.h>
#else
#include <stddef.h>
#include <stdint.h>
#include <sys/types.h>  // as <pthread.h> provides it (mode_t, ...)
#include <time.h>  // struct timespec, clockid_t, CLOCK_REALTIME / CLOCK_MONOTONIC

// Layout-compatible with SRWLOCK, CONDITION_VARIABLE and INIT_ONCE (one pointer each, zero = the
// static initializer), so this header needs no <windows.h>.
typedef struct { void* ptr; } pthread_mutex_t;
typedef struct { void* ptr; } pthread_cond_t;
typedef struct { void* ptr; } pthread_once_t;
typedef uintptr_t pthread_t;  // the thread id (GetCurrentThreadId), unique while the thread runs
typedef struct { size_t stack_size; } pthread_attr_t;

#define PTHREAD_MUTEX_INITIALIZER { 0 }
#define PTHREAD_COND_INITIALIZER { 0 }
#define PTHREAD_ONCE_INIT { 0 }
#define PTHREAD_CREATE_DETACHED 1

#define pthread_mutex_init csrt_pthread_mutex_init
#define pthread_mutex_destroy csrt_pthread_mutex_destroy
#define pthread_mutex_lock csrt_pthread_mutex_lock
#define pthread_mutex_trylock csrt_pthread_mutex_trylock
#define pthread_mutex_unlock csrt_pthread_mutex_unlock
#define pthread_cond_init csrt_pthread_cond_init
#define pthread_cond_destroy csrt_pthread_cond_destroy
#define pthread_cond_wait csrt_pthread_cond_wait
#define pthread_cond_timedwait csrt_pthread_cond_timedwait
#define pthread_cond_timedwait_relative_np csrt_pthread_cond_timedwait_relative_np
#define pthread_cond_signal csrt_pthread_cond_signal
#define pthread_cond_broadcast csrt_pthread_cond_broadcast
#define pthread_once csrt_pthread_once
#define pthread_self csrt_pthread_self
#define pthread_equal(a, b) ((a) == (b))
#define pthread_attr_init csrt_pthread_attr_init
#define pthread_attr_destroy csrt_pthread_attr_destroy
#define pthread_attr_setdetachstate csrt_pthread_attr_setdetachstate
#define pthread_attr_setstacksize csrt_pthread_attr_setstacksize
#define pthread_create csrt_pthread_create
#define pthread_num_processors_np csrt_pthread_num_processors_np
#define sched_yield csrt_sched_yield
#define nanosleep csrt_nanosleep
#define clock_gettime csrt_clock_gettime

int pthread_mutex_init(pthread_mutex_t* mutex, const void* attr);
int pthread_mutex_destroy(pthread_mutex_t* mutex);
int pthread_mutex_lock(pthread_mutex_t* mutex);
int pthread_mutex_trylock(pthread_mutex_t* mutex);
int pthread_mutex_unlock(pthread_mutex_t* mutex);
int pthread_cond_init(pthread_cond_t* cond, const void* attr);
int pthread_cond_destroy(pthread_cond_t* cond);
int pthread_cond_wait(pthread_cond_t* cond, pthread_mutex_t* mutex);
int pthread_cond_timedwait(pthread_cond_t* cond, pthread_mutex_t* mutex, const struct timespec* deadline);
int pthread_cond_timedwait_relative_np(pthread_cond_t* cond, pthread_mutex_t* mutex, const struct timespec* timeout);
int pthread_cond_signal(pthread_cond_t* cond);
int pthread_cond_broadcast(pthread_cond_t* cond);
int pthread_once(pthread_once_t* once, void (*init)(void));
pthread_t pthread_self(void);
int pthread_attr_init(pthread_attr_t* attr);
int pthread_attr_destroy(pthread_attr_t* attr);
int pthread_attr_setdetachstate(pthread_attr_t* attr, int state);
int pthread_attr_setstacksize(pthread_attr_t* attr, size_t size);
int pthread_create(pthread_t* thread, const pthread_attr_t* attr, void* (*start)(void*), void* arg);
int pthread_num_processors_np(void);
int sched_yield(void);
int nanosleep(const struct timespec* request, struct timespec* remaining);
int clock_gettime(clockid_t clock, struct timespec* ts);
#endif

#endif
