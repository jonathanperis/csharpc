// csrt on Windows: C library functions the UCRT lacks but LLVM-generated code may call, and the
// pthread subset declared in csrt_pthread.h (in place of winpthreads; see there for why).
#include "csrt.h"
#include "csrt_pthread.h"

#ifdef _WIN32
#include <errno.h>
#include <math.h>
#include <process.h>
#include <stdlib.h>
#include <windows.h>

// C23 roundeven: LLVM lowers llvm.roundeven (Math.Round's default MidpointRounding.ToEven) to this
// libcall when the CPU has no SSE4.1 round instruction. RuntimeLto keeps these external.
double roundeven(double x) {
    double r = round(x);  // halfway cases away from zero
    if (fabs(x - trunc(x)) == 0.5) r = 2.0 * round(x / 2.0);
    return r;
}

float roundevenf(float x) {
    float r = roundf(x);
    if (fabsf(x - truncf(x)) == 0.5f) r = 2.0f * roundf(x / 2.0f);
    return r;
}

// ---------------------------------------------------------------------------------------------
// Locks, condition variables, one-time initialization
// ---------------------------------------------------------------------------------------------
_Static_assert(sizeof(pthread_mutex_t) == sizeof(SRWLOCK), "pthread_mutex_t must be an SRWLOCK");
_Static_assert(sizeof(pthread_cond_t) == sizeof(CONDITION_VARIABLE), "pthread_cond_t must be a CONDITION_VARIABLE");
_Static_assert(sizeof(pthread_once_t) == sizeof(INIT_ONCE), "pthread_once_t must be an INIT_ONCE");

int pthread_mutex_init(pthread_mutex_t* mutex, const void* attr) { (void)attr; InitializeSRWLock((PSRWLOCK)mutex); return 0; }
int pthread_mutex_destroy(pthread_mutex_t* mutex) { (void)mutex; return 0; }
int pthread_mutex_lock(pthread_mutex_t* mutex) { AcquireSRWLockExclusive((PSRWLOCK)mutex); return 0; }
int pthread_mutex_trylock(pthread_mutex_t* mutex) { return TryAcquireSRWLockExclusive((PSRWLOCK)mutex) ? 0 : EBUSY; }
int pthread_mutex_unlock(pthread_mutex_t* mutex) { ReleaseSRWLockExclusive((PSRWLOCK)mutex); return 0; }

int pthread_cond_init(pthread_cond_t* cond, const void* attr) { (void)attr; InitializeConditionVariable((PCONDITION_VARIABLE)cond); return 0; }
int pthread_cond_destroy(pthread_cond_t* cond) { (void)cond; return 0; }
int pthread_cond_signal(pthread_cond_t* cond) { WakeConditionVariable((PCONDITION_VARIABLE)cond); return 0; }
int pthread_cond_broadcast(pthread_cond_t* cond) { WakeAllConditionVariable((PCONDITION_VARIABLE)cond); return 0; }

static int cond_wait_ms(pthread_cond_t* cond, pthread_mutex_t* mutex, DWORD ms) {
    if (SleepConditionVariableSRW((PCONDITION_VARIABLE)cond, (PSRWLOCK)mutex, ms, 0)) return 0;
    return GetLastError() == ERROR_TIMEOUT ? ETIMEDOUT : EINVAL;
}

/// Milliseconds, rounded up so a wait never ends before its deadline; at most INFINITE - 1 (the
/// caller re-checks its deadline after a timeout, so waits beyond ~49 days just take two rounds).
static DWORD timeout_ms(int64_t ns) {
    int64_t ms = (ns + 999999) / 1000000;
    return ms >= (int64_t)INFINITE ? INFINITE - 1 : (DWORD)ms;
}

int pthread_cond_wait(pthread_cond_t* cond, pthread_mutex_t* mutex) { return cond_wait_ms(cond, mutex, INFINITE); }

/// `deadline` is CLOCK_REALTIME, as pthread_cond_timedwait without pthread_condattr_setclock.
int pthread_cond_timedwait(pthread_cond_t* cond, pthread_mutex_t* mutex, const struct timespec* deadline) {
    struct timespec now;
    clock_gettime(CLOCK_REALTIME, &now);
    int64_t ns = ((int64_t)deadline->tv_sec - now.tv_sec) * 1000000000 + (deadline->tv_nsec - now.tv_nsec);
    return ns <= 0 ? ETIMEDOUT : cond_wait_ms(cond, mutex, timeout_ms(ns));
}

int pthread_cond_timedwait_relative_np(pthread_cond_t* cond, pthread_mutex_t* mutex, const struct timespec* timeout) {
    return cond_wait_ms(cond, mutex, timeout_ms((int64_t)timeout->tv_sec * 1000000000 + timeout->tv_nsec));
}

static BOOL CALLBACK run_once(PINIT_ONCE once, PVOID init, PVOID* context) {
    (void)once; (void)context;
    ((void (*)(void))init)();
    return TRUE;
}

int pthread_once(pthread_once_t* once, void (*init)(void)) {
    InitOnceExecuteOnce((PINIT_ONCE)once, run_once, (PVOID)init, NULL);
    return 0;
}

// ---------------------------------------------------------------------------------------------
// Threads (always detached)
// ---------------------------------------------------------------------------------------------
pthread_t pthread_self(void) { return GetCurrentThreadId(); }

int pthread_attr_init(pthread_attr_t* attr) { attr->stack_size = 0; return 0; }
int pthread_attr_destroy(pthread_attr_t* attr) { (void)attr; return 0; }
int pthread_attr_setdetachstate(pthread_attr_t* attr, int state) { (void)attr; return state == PTHREAD_CREATE_DETACHED ? 0 : EINVAL; }
int pthread_attr_setstacksize(pthread_attr_t* attr, size_t size) { attr->stack_size = size; return 0; }

typedef struct { void* (*start)(void*); void* arg; } ThreadStart;

static unsigned __stdcall thread_start(void* p) {
    ThreadStart s = *(ThreadStart*)p;
    free(p);
    s.start(s.arg);
    return 0;
}

int pthread_create(pthread_t* thread, const pthread_attr_t* attr, void* (*start)(void*), void* arg) {
    ThreadStart* s = malloc(sizeof *s);
    if (!s) return EAGAIN;
    s->start = start;
    s->arg = arg;
    // _beginthreadex (not CreateThread) so the UCRT sets up its per-thread state; the stack size
    // is reserved, not committed up front.
    unsigned id;
    uintptr_t handle = _beginthreadex(NULL, attr ? (unsigned)attr->stack_size : 0, thread_start, s, STACK_SIZE_PARAM_IS_A_RESERVATION, &id);
    if (!handle) { free(s); return EAGAIN; }
    CloseHandle((HANDLE)handle);
    *thread = id;
    return 0;
}

/// Processors the process may run on (its affinity mask), as winpthreads counted them.
int pthread_num_processors_np(void) {
    DWORD_PTR process, system;
    int n = GetProcessAffinityMask(GetCurrentProcess(), &process, &system) ? __builtin_popcountll((unsigned long long)process) : 0;
    return n > 0 ? n : 1;
}

int sched_yield(void) { Sleep(0); return 0; }

// ---------------------------------------------------------------------------------------------
// Clocks
// ---------------------------------------------------------------------------------------------
/// Sleep is not interrupted (no signals), so `remaining` is always zero.
int nanosleep(const struct timespec* request, struct timespec* remaining) {
    int64_t ms = (int64_t)request->tv_sec * 1000 + (request->tv_nsec + 999999) / 1000000;
    while (ms > 0) {
        DWORD chunk = ms >= (int64_t)INFINITE ? INFINITE - 1 : (DWORD)ms;
        Sleep(chunk);
        ms -= chunk;
    }
    if (remaining) remaining->tv_sec = 0, remaining->tv_nsec = 0;
    return 0;
}

/// CLOCK_REALTIME: GetSystemTimePreciseAsFileTime; CLOCK_MONOTONIC: QueryPerformanceCounter.
int clock_gettime(clockid_t clock, struct timespec* ts) {
    if (clock == CLOCK_REALTIME) {
        FILETIME ft;
        GetSystemTimePreciseAsFileTime(&ft);
        int64_t t = (int64_t)((uint64_t)ft.dwHighDateTime << 32 | ft.dwLowDateTime) - 116444736000000000LL;  // 100 ns since 1970
        ts->tv_sec = (time_t)(t / 10000000);
        ts->tv_nsec = (long)(t % 10000000) * 100;
        return 0;
    }
    if (clock == CLOCK_MONOTONIC) {
        LARGE_INTEGER c, f;
        QueryPerformanceCounter(&c);
        QueryPerformanceFrequency(&f);
        ts->tv_sec = (time_t)(c.QuadPart / f.QuadPart);
        ts->tv_nsec = (long)(c.QuadPart % f.QuadPart * 1000000000 / f.QuadPart);
        return 0;
    }
    errno = EINVAL;
    return -1;
}
#else
typedef int csrt_win32_unused;  // ISO C: a translation unit must not be empty
#endif
