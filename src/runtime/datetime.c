// csrt datetime: the clocks behind DateTime.UtcNow/Now and System.Diagnostics.Stopwatch, and the
// method tables of boxed TimeSpan/DateTime. TimeSpan is its tick count (int64, 100 ns units);
// DateTime is .NET's _dateData (ticks in bits 0-61, Kind in bits 62-63). Their arithmetic, calendar,
// time zones and formatting are C# (corelib DateTimeOps.cs, ADR 0008).
#define _DEFAULT_SOURCE  // clock_gettime
#include "csrt.h"

#include <time.h>

#include "csrt_pthread.h"  // clock_gettime on Windows
#ifdef _WIN32
#include <windows.h>
#endif

#define TICKS_PER_SECOND 10000000LL
#define UNIX_EPOCH_TICKS 621355968000000000LL

// DateTimeOps.TimeSpanToString / DateTimeToString / FormatWith (C# exports).
csrt_String* csrt_timespan_to_string(int64_t ticks);
csrt_String* csrt_datetime_to_string(int64_t date_data);
csrt_String* csrt_timespan_format_with(int64_t ticks, csrt_String* format, void* provider);
csrt_String* csrt_datetime_format_with(int64_t date_data, csrt_String* format, void* provider);

int64_t csrt_datetime_utc_now(void) {
#ifdef _WIN32
    // .NET on Windows: GetSystemTimePreciseAsFileTime (100 ns units since 1601).
    FILETIME ft;
    GetSystemTimePreciseAsFileTime(&ft);
    return ((int64_t)ft.dwHighDateTime << 32 | ft.dwLowDateTime) + 504911232000000000LL;
#else
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);
    return UNIX_EPOCH_TICKS + (int64_t)ts.tv_sec * TICKS_PER_SECOND + ts.tv_nsec / 100;
#endif
}

// Boxed TimeSpan / DateTime (payload @ 8): ToString / Equals / GetHashCode.
static csrt_String* box_ts_to_string(void* o) { return csrt_timespan_to_string(*(int64_t*)((char*)o + 8)); }
static csrt_String* box_dt_to_string(void* o) { return csrt_datetime_to_string(*(int64_t*)((char*)o + 8)); }
static bool box_ticks_equals(void* self, void* other) {
    return other && ((csrt_Object*)other)->mt == ((csrt_Object*)self)->mt &&
           *(int64_t*)((char*)self + 8) == *(int64_t*)((char*)other + 8);
}
static int32_t box_ticks_hash(void* self) { int64_t t = *(int64_t*)((char*)self + 8); return (int32_t)t ^ (int32_t)(t >> 32); }
// DateTime equality and hashing ignore the Kind bits, as in .NET.
#define DATETIME_TICKS_MASK 0x3FFFFFFFFFFFFFFFLL
static bool box_dt_equals(void* self, void* other) {
    return other && ((csrt_Object*)other)->mt == ((csrt_Object*)self)->mt &&
           (*(int64_t*)((char*)self + 8) & DATETIME_TICKS_MASK) == (*(int64_t*)((char*)other + 8) & DATETIME_TICKS_MASK);
}
static int32_t box_dt_hash(void* self) { int64_t t = *(int64_t*)((char*)self + 8) & DATETIME_TICKS_MASK; return (int32_t)t ^ (int32_t)(t >> 32); }

const csrt_BuiltinMethodTable csrt_Boxed_TimeSpan_MT = {
    { &csrt_Object_MT.header, "System.TimeSpan", 16, 0 },
    { (void*)box_ts_to_string, (void*)box_ticks_equals, (void*)box_ticks_hash },
};
const csrt_BuiltinMethodTable csrt_Boxed_DateTime_MT = {
    { &csrt_Object_MT.header, "System.DateTime", 16, 0 },
    { (void*)box_dt_to_string, (void*)box_dt_equals, (void*)box_dt_hash },
};

// ---------------------------------------------------------------------------------------------
// Stopwatch: { mt, i64 started (timestamp), i64 elapsed (Stopwatch ticks), i8 running }
// Stopwatch.Frequency is 1_000_000_000 on Linux and macOS, so its ticks are nanoseconds; on Windows
// they are QueryPerformanceCounter units, as in .NET (Frequency = QueryPerformanceFrequency).
// ---------------------------------------------------------------------------------------------
typedef struct { const csrt_MethodTable* mt; int64_t started; int64_t elapsed; bool running; } Stopwatch;

static csrt_String* sw_to_string(void* o);
const csrt_BuiltinMethodTable csrt_Stopwatch_MT = {
    { &csrt_Object_MT.header, "System.Diagnostics.Stopwatch", sizeof(Stopwatch), 0 },
    { (void*)sw_to_string, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

#ifdef _WIN32
int64_t csrt_stopwatch_frequency(void) {
    LARGE_INTEGER f;
    QueryPerformanceFrequency(&f);
    return f.QuadPart;
}

int64_t csrt_stopwatch_timestamp(void) {
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    return c.QuadPart;
}

/// Stopwatch ticks -> TimeSpan ticks (Stopwatch.GetElapsedTime, Elapsed).
int64_t csrt_stopwatch_to_timespan(int64_t ticks) {
    int64_t f = csrt_stopwatch_frequency();
    return f == TICKS_PER_SECOND ? ticks : (int64_t)((double)ticks * ((double)TICKS_PER_SECOND / (double)f));
}
#define TO_TIMESPAN(raw) csrt_stopwatch_to_timespan(raw)
#define TO_MS(raw) ((raw) * 1000 / csrt_stopwatch_frequency())
#else
int64_t csrt_stopwatch_timestamp(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000000000LL + ts.tv_nsec;
}
#define TO_TIMESPAN(raw) ((raw) / 100)
#define TO_MS(raw) ((raw) / 1000000)
#endif

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

/// Elapsed time in Stopwatch ticks.
int64_t csrt_stopwatch_elapsed_raw(Stopwatch* sw) {
    need(sw);
    return sw->elapsed + (sw->running ? csrt_stopwatch_timestamp() - sw->started : 0);
}
int64_t csrt_stopwatch_elapsed_timespan(Stopwatch* sw) { return TO_TIMESPAN(csrt_stopwatch_elapsed_raw(sw)); }
int64_t csrt_stopwatch_elapsed_ms(Stopwatch* sw) { return TO_MS(csrt_stopwatch_elapsed_raw(sw)); }

// .NET 9+: Stopwatch.ToString() == Elapsed.ToString()
static csrt_String* sw_to_string(void* o) { return csrt_timespan_to_string(csrt_stopwatch_elapsed_timespan(o)); }

// Composite formatting of a boxed DateTime / TimeSpan (Number.FormatObjectWith): NULL for any other
// object. Testing the method table here rather than with `is DateTime` in C# keeps the culture date
// formatter out of programs that never box a DateTime: the C# formatters are exported (and compiled)
// only when the program references these method tables, which boxing one does.
csrt_String* csrt_format_boxed_ticks(void* obj, csrt_String* format, void* provider) {
    const void* mt = ((csrt_Object*)obj)->mt;
    int64_t payload = *(int64_t*)((char*)obj + 8);
    if (mt == &csrt_Boxed_DateTime_MT) return csrt_datetime_format_with(payload, format, provider);
    if (mt == &csrt_Boxed_TimeSpan_MT) return csrt_timespan_format_with(payload, format, provider);
    return NULL;
}
