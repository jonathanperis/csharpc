// csrt datetime: the clocks behind DateTime.UtcNow/Now and System.Diagnostics.Stopwatch, and the
// method tables of boxed TimeSpan/DateTime. TimeSpan and DateTime are their tick count (int64,
// 100 ns units); their arithmetic, calendar and formatting are C# (corelib DateTimeOps.cs, ADR 0008).
#define _DEFAULT_SOURCE  // struct tm::tm_gmtoff
#include "csrt.h"

#include <time.h>

#define TICKS_PER_SECOND 10000000LL
#define UNIX_EPOCH_TICKS 621355968000000000LL

// DateTimeOps.TimeSpanToString / DateTimeToString (C# exports).
csrt_String* csrt_timespan_to_string(int64_t ticks);
csrt_String* csrt_datetime_to_string(int64_t ticks);

int64_t csrt_datetime_utc_now(void) {
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);
    return UNIX_EPOCH_TICKS + (int64_t)ts.tv_sec * TICKS_PER_SECOND + ts.tv_nsec / 100;
}

int64_t csrt_datetime_now(void) {
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);
    struct tm local;
    time_t secs = ts.tv_sec;
    localtime_r(&secs, &local);
    return UNIX_EPOCH_TICKS + ((int64_t)ts.tv_sec + local.tm_gmtoff) * TICKS_PER_SECOND + ts.tv_nsec / 100;
}

// Boxed TimeSpan / DateTime (payload @ 8): ToString / Equals / GetHashCode.
static csrt_String* box_ts_to_string(void* o) { return csrt_timespan_to_string(*(int64_t*)((char*)o + 8)); }
static csrt_String* box_dt_to_string(void* o) { return csrt_datetime_to_string(*(int64_t*)((char*)o + 8)); }
static bool box_ticks_equals(void* self, void* other) {
    return other && ((csrt_Object*)other)->mt == ((csrt_Object*)self)->mt &&
           *(int64_t*)((char*)self + 8) == *(int64_t*)((char*)other + 8);
}
static int32_t box_ticks_hash(void* self) { int64_t t = *(int64_t*)((char*)self + 8); return (int32_t)t ^ (int32_t)(t >> 32); }

const csrt_BuiltinMethodTable csrt_Boxed_TimeSpan_MT = {
    { &csrt_Object_MT.header, "System.TimeSpan", 16, 0 },
    { (void*)box_ts_to_string, (void*)box_ticks_equals, (void*)box_ticks_hash },
};
const csrt_BuiltinMethodTable csrt_Boxed_DateTime_MT = {
    { &csrt_Object_MT.header, "System.DateTime", 16, 0 },
    { (void*)box_dt_to_string, (void*)box_ticks_equals, (void*)box_ticks_hash },
};

// ---------------------------------------------------------------------------------------------
// Stopwatch: { mt, i64 started (ns timestamp), i64 elapsed (ns), i8 running }
// Stopwatch.Frequency is 1_000_000_000 on Linux, so its ticks are nanoseconds.
// ---------------------------------------------------------------------------------------------
typedef struct { const csrt_MethodTable* mt; int64_t started; int64_t elapsed; bool running; } Stopwatch;

static csrt_String* sw_to_string(void* o);
const csrt_BuiltinMethodTable csrt_Stopwatch_MT = {
    { &csrt_Object_MT.header, "System.Diagnostics.Stopwatch", sizeof(Stopwatch), 0 },
    { (void*)sw_to_string, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

int64_t csrt_stopwatch_timestamp(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000000000LL + ts.tv_nsec;
}

Stopwatch* csrt_stopwatch_new(bool start) {
    Stopwatch* sw = csrt_alloc(&csrt_Stopwatch_MT.header, sizeof(Stopwatch));
    if (start) { sw->started = csrt_stopwatch_timestamp(); sw->running = true; }
    return sw;
}

static Stopwatch* need(Stopwatch* sw) { if (!sw) csrt_throw_null_reference(); return sw; }

void csrt_stopwatch_start(Stopwatch* sw) {
    if (!need(sw)->running) { sw->started = csrt_stopwatch_timestamp(); sw->running = true; }
}
void csrt_stopwatch_stop(Stopwatch* sw) {
    if (need(sw)->running) { sw->elapsed += csrt_stopwatch_timestamp() - sw->started; sw->running = false; }
}
void csrt_stopwatch_reset(Stopwatch* sw) { need(sw)->elapsed = 0; sw->running = false; }
void csrt_stopwatch_restart(Stopwatch* sw) { need(sw)->elapsed = 0; sw->started = csrt_stopwatch_timestamp(); sw->running = true; }
bool csrt_stopwatch_is_running(Stopwatch* sw) { return need(sw)->running; }

/// Elapsed time in Stopwatch ticks (ns).
int64_t csrt_stopwatch_elapsed_raw(Stopwatch* sw) {
    need(sw);
    return sw->elapsed + (sw->running ? csrt_stopwatch_timestamp() - sw->started : 0);
}
int64_t csrt_stopwatch_elapsed_timespan(Stopwatch* sw) { return csrt_stopwatch_elapsed_raw(sw) / 100; }
int64_t csrt_stopwatch_elapsed_ms(Stopwatch* sw) { return csrt_stopwatch_elapsed_raw(sw) / 1000000; }

// .NET 9+: Stopwatch.ToString() == Elapsed.ToString()
static csrt_String* sw_to_string(void* o) { return csrt_timespan_to_string(csrt_stopwatch_elapsed_timespan(o)); }
