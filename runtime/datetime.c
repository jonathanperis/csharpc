// csrt datetime: System.TimeSpan, System.DateTime and System.Diagnostics.Stopwatch.
//
// TimeSpan and DateTime are represented by their tick count (int64, 100 ns units) — DateTime's
// Kind bits are not modelled (always Unspecified/Local-agnostic). Calendar math is the CLR's own
// algorithm (proleptic Gregorian, year 1..9999); formatting follows the invariant culture.
#define _DEFAULT_SOURCE  // struct tm::tm_gmtoff
#include "csrt.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#define TICKS_PER_MS 10000LL
#define TICKS_PER_SECOND 10000000LL
#define TICKS_PER_MINUTE 600000000LL
#define TICKS_PER_HOUR 36000000000LL
#define TICKS_PER_DAY 864000000000LL
#define MAX_TICKS 3155378975999999999LL   // DateTime.MaxValue
#define UNIX_EPOCH_TICKS 621355968000000000LL

static const csrt_MethodTable mt_overflow = { NULL, "System.OverflowException", 0, 0 };
static const csrt_MethodTable mt_range = { NULL, "System.ArgumentOutOfRangeException", 0, 0 };
static const csrt_MethodTable mt_format = { NULL, "System.FormatException", 0, 0 };
static const csrt_MethodTable mt_argument = { NULL, "System.ArgumentException", 0, 0 };

typedef struct { char* p; size_t n, cap; } Buf;
static void put(Buf* b, char c) {
    if (b->n + 1 >= b->cap) { b->cap = b->cap ? b->cap * 2 : 64; b->p = realloc(b->p, b->cap); }
    b->p[b->n++] = c;
}
static void puts_(Buf* b, const char* s) { while (*s) put(b, *s++); }
static void putnum(Buf* b, long long v, int width) { char t[32]; snprintf(t, sizeof t, "%0*lld", width, v); puts_(b, t); }

static csrt_String* buf_to_string(Buf* b) {
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * b->n));
    s->length = (int32_t)b->n;
    for (size_t i = 0; i < b->n; i++) s->chars[i] = (uint8_t)b->p[i];
    free(b->p);
    return s;
}
static csrt_String* ascii(const char* text) { Buf b = {0}; puts_(&b, text); return buf_to_string(&b); }

_Noreturn static void timespan_overflow(void) {
    csrt_throw(&mt_overflow, ascii("TimeSpan overflowed because the duration is too long."));
}
_Noreturn static void datetime_range(void) {
    csrt_throw(&mt_range, ascii("The added or subtracted value results in an un-representable DateTime. (Parameter 'value')"));
}

static void spec_to_c(csrt_String* fmt, char* out, size_t cap) {
    size_t n = 0;
    if (fmt) for (int32_t i = 0; i < fmt->length && n + 1 < cap; i++) out[n++] = fmt->chars[i] < 128 ? (char)fmt->chars[i] : '?';
    out[n] = 0;
}

// ---------------------------------------------------------------------------------------------
// TimeSpan
// ---------------------------------------------------------------------------------------------
int64_t csrt_timespan_add(int64_t a, int64_t b) {
    int64_t r;
    if (__builtin_add_overflow(a, b, &r)) timespan_overflow();
    return r;
}
int64_t csrt_timespan_sub(int64_t a, int64_t b) {
    int64_t r;
    if (__builtin_sub_overflow(a, b, &r)) timespan_overflow();
    return r;
}
int64_t csrt_timespan_negate(int64_t a) {
    if (a == INT64_MIN) csrt_throw(&mt_overflow, ascii("Negating the minimum value of a twos complement number is invalid."));
    return -a;
}
int64_t csrt_timespan_duration(int64_t a) {
    if (a == INT64_MIN) csrt_throw(&mt_overflow, ascii("The duration cannot be returned for TimeSpan.MinValue because the absolute value of TimeSpan.MinValue exceeds the value of TimeSpan.MaxValue."));
    return a < 0 ? -a : a;
}

static int64_t from_double_ticks(double ticks) {
    if (ticks > 9223372036854775807.0 || ticks < -9223372036854775808.0 || isnan(ticks)) timespan_overflow();
    if (ticks >= 9223372036854775807.0) return INT64_MAX;
    return (int64_t)ticks;
}

/// TimeSpan.FromDays/Hours/... (double): exact ticks, truncated (.NET 7+ semantics).
int64_t csrt_timespan_from_double(double value, int64_t ticks_per_unit) {
    if (isnan(value)) csrt_throw(&mt_argument, ascii("TimeSpan does not accept floating point Not-a-Number values."));
    return from_double_ticks(value * (double)ticks_per_unit);
}

/// TimeSpan.FromX integer overloads / constructors: sum of unit counts, checked.
int64_t csrt_timespan_from_units(int64_t days, int64_t hours, int64_t minutes, int64_t seconds, int64_t ms, int64_t us) {
    __int128 t = (__int128)days * TICKS_PER_DAY + (__int128)hours * TICKS_PER_HOUR + (__int128)minutes * TICKS_PER_MINUTE +
                 (__int128)seconds * TICKS_PER_SECOND + (__int128)ms * TICKS_PER_MS + (__int128)us * 10;
    if (t > INT64_MAX || t < INT64_MIN)
        csrt_throw(&mt_range, ascii("TimeSpan overflowed because the duration is too long."));
    return (int64_t)t;
}

int64_t csrt_timespan_multiply(int64_t ticks, double factor) {
    if (isnan(factor)) csrt_throw(&mt_argument, ascii("TimeSpan does not accept floating point Not-a-Number values. (Parameter 'factor')"));
    return from_double_ticks(nearbyint((double)ticks * factor));
}
int64_t csrt_timespan_divide(int64_t ticks, double divisor) {
    if (isnan(divisor)) csrt_throw(&mt_argument, ascii("TimeSpan does not accept floating point Not-a-Number values. (Parameter 'divisor')"));
    return from_double_ticks(nearbyint((double)ticks / divisor));
}

static void append_fraction(Buf* b, int64_t frac, int digits, bool trim) {
    char t[16];
    snprintf(t, sizeof t, "%07lld", (long long)frac);
    int n = digits;
    if (trim) while (n > 0 && t[n - 1] == '0') n--;
    for (int i = 0; i < n; i++) put(b, t[i]);
}

static void timespan_standard(Buf* b, int64_t ticks, char f) {
    bool neg = ticks < 0;
    // magnitude via unsigned to survive MinValue
    uint64_t mag = neg ? (uint64_t)0 - (uint64_t)ticks : (uint64_t)ticks;
    uint64_t days = mag / TICKS_PER_DAY, rem = mag % TICKS_PER_DAY;
    int hours = (int)(rem / TICKS_PER_HOUR), minutes = (int)(rem / TICKS_PER_MINUTE % 60), seconds = (int)(rem / TICKS_PER_SECOND % 60);
    int64_t frac = (int64_t)(rem % TICKS_PER_SECOND);
    if (neg) put(b, '-');
    if (f == 'g') {
        if (days) { putnum(b, (long long)days, 1); put(b, ':'); }
        putnum(b, hours, 1); put(b, ':'); putnum(b, minutes, 2); put(b, ':'); putnum(b, seconds, 2);
        if (frac) { put(b, '.'); append_fraction(b, frac, 7, true); }
    } else if (f == 'G') {
        putnum(b, (long long)days, 1); put(b, ':'); putnum(b, hours, 2); put(b, ':'); putnum(b, minutes, 2); put(b, ':');
        putnum(b, seconds, 2); put(b, '.'); append_fraction(b, frac, 7, false);
    } else {  // 'c' constant format
        if (days) { putnum(b, (long long)days, 1); put(b, '.'); }
        putnum(b, hours, 2); put(b, ':'); putnum(b, minutes, 2); put(b, ':'); putnum(b, seconds, 2);
        if (frac) { put(b, '.'); append_fraction(b, frac, 7, false); }
    }
}

csrt_String* csrt_timespan_to_string(int64_t ticks) { Buf b = {0}; timespan_standard(&b, ticks, 'c'); return buf_to_string(&b); }

static int run_length(const char* f, int i) { int n = 1; while (f[i + n] == f[i]) n++; return n; }

csrt_String* csrt_timespan_format(int64_t ticks, csrt_String* fmt) {
    char f[128]; spec_to_c(fmt, f, sizeof f);
    Buf b = {0};
    if (!f[0] || ((f[0] == 'c' || f[0] == 't' || f[0] == 'T' || f[0] == 'g' || f[0] == 'G') && !f[1])) {
        timespan_standard(&b, ticks, f[0] == 'g' ? 'g' : f[0] == 'G' ? 'G' : 'c');
        return buf_to_string(&b);
    }
    uint64_t mag = ticks < 0 ? (uint64_t)0 - (uint64_t)ticks : (uint64_t)ticks;
    uint64_t days = mag / TICKS_PER_DAY, rem = mag % TICKS_PER_DAY;
    int hours = (int)(rem / TICKS_PER_HOUR), minutes = (int)(rem / TICKS_PER_MINUTE % 60), seconds = (int)(rem / TICKS_PER_SECOND % 60);
    int64_t frac = (int64_t)(rem % TICKS_PER_SECOND);
    for (int i = 0; f[i];) {
        char c = f[i];
        int n = run_length(f, i);
        switch (c) {
            case 'd': putnum(&b, (long long)days, n); i += n; break;
            case 'h': putnum(&b, hours, n > 1 ? 2 : 1); i += n; break;
            case 'm': putnum(&b, minutes, n > 1 ? 2 : 1); i += n; break;
            case 's': putnum(&b, seconds, n > 1 ? 2 : 1); i += n; break;
            case 'f': append_fraction(&b, frac, n > 7 ? 7 : n, false); i += n; break;
            case 'F': append_fraction(&b, frac, n > 7 ? 7 : n, true); i += n; break;
            case '\\': if (f[i + 1]) put(&b, f[i + 1]); i += 2; break;
            case '\'': case '"': {
                int j = i + 1;
                while (f[j] && f[j] != c) put(&b, f[j++]);
                i = f[j] ? j + 1 : j;
                break;
            }
            case '%': i++; break;
            default:
                free(b.p);
                csrt_throw(&mt_format, ascii("Input string was not in a correct format."));
        }
    }
    return buf_to_string(&b);
}

// ---------------------------------------------------------------------------------------------
// DateTime (the CLR's calendar algorithms)
// ---------------------------------------------------------------------------------------------
static const int days_to_month_365[13] = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };
static const int days_to_month_366[13] = { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 };
static const char* month_names[12] = { "January", "February", "March", "April", "May", "June", "July", "August",
                                       "September", "October", "November", "December" };
static const char* day_names[7] = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

bool csrt_datetime_is_leap_year(int32_t year) {
    if (year < 1 || year > 9999) csrt_throw(&mt_range, ascii("Year must be between 1 and 9999. (Parameter 'year')"));
    return (year & 3) == 0 && ((year % 100) != 0 || (year % 400) == 0);
}

int32_t csrt_datetime_days_in_month(int32_t year, int32_t month) {
    if (month < 1 || month > 12) csrt_throw(&mt_range, ascii("Month must be between one and twelve. (Parameter 'month')"));
    const int* days = csrt_datetime_is_leap_year(year) ? days_to_month_366 : days_to_month_365;
    return days[month] - days[month - 1];
}

static int64_t date_to_ticks(int y, int m, int d) {
    if (y < 1 || y > 9999 || m < 1 || m > 12)
        csrt_throw(&mt_range, ascii("Year, Month, and Day parameters describe an un-representable DateTime."));
    const int* days = ((y & 3) == 0 && ((y % 100) != 0 || (y % 400) == 0)) ? days_to_month_366 : days_to_month_365;
    if (d < 1 || d > days[m] - days[m - 1])
        csrt_throw(&mt_range, ascii("Year, Month, and Day parameters describe an un-representable DateTime."));
    int64_t y1 = y - 1;
    int64_t n = y1 * 365 + y1 / 4 - y1 / 100 + y1 / 400 + days[m - 1] + d - 1;
    return n * TICKS_PER_DAY;
}

int64_t csrt_datetime_new(int32_t y, int32_t mo, int32_t d, int32_t h, int32_t mi, int32_t s, int32_t ms) {
    int64_t ticks = date_to_ticks(y, mo, d);
    if (h < 0 || h >= 24 || mi < 0 || mi >= 60 || s < 0 || s >= 60)
        csrt_throw(&mt_range, ascii("Hour, Minute, and Second parameters describe an un-representable DateTime."));
    if (ms < 0 || ms >= 1000)
        csrt_throw(&mt_range, ascii("Valid values are between 0 and 999, inclusive. (Parameter 'millisecond')"));
    return ticks + h * TICKS_PER_HOUR + mi * TICKS_PER_MINUTE + s * TICKS_PER_SECOND + ms * TICKS_PER_MS;
}

int64_t csrt_datetime_from_ticks(int64_t ticks) {
    if (ticks < 0 || ticks > MAX_TICKS)
        csrt_throw(&mt_range, ascii("Ticks must be between DateTime.MinValue.Ticks and DateTime.MaxValue.Ticks. (Parameter 'ticks')"));
    return ticks;
}

static void date_parts(int64_t ticks, int* year, int* month, int* day, int* day_of_year) {
    int n = (int)(ticks / TICKS_PER_DAY);
    int y400 = n / 146097; n -= y400 * 146097;
    int y100 = n / 36524; if (y100 == 4) y100 = 3; n -= y100 * 36524;
    int y4 = n / 1461; n -= y4 * 1461;
    int y1 = n / 365; if (y1 == 4) y1 = 3;
    *year = y400 * 400 + y100 * 100 + y4 * 4 + y1 + 1;
    n -= y1 * 365;
    *day_of_year = n + 1;
    bool leap = y1 == 3 && (y4 != 24 || y100 == 3);
    const int* days = leap ? days_to_month_366 : days_to_month_365;
    int m = (n >> 5) + 1;
    while (n >= days[m]) m++;
    *month = m;
    *day = n - days[m - 1] + 1;
}

/// part: 0 year, 1 month, 2 day, 3 day-of-year, 4 day-of-week, 5 hour, 6 minute, 7 second, 8 millisecond,
/// 9 microsecond, 10 nanosecond
int32_t csrt_datetime_part(int64_t ticks, int32_t part) {
    int y, m, d, doy;
    switch (part) {
        case 0: case 1: case 2: case 3:
            date_parts(ticks, &y, &m, &d, &doy);
            return part == 0 ? y : part == 1 ? m : part == 2 ? d : doy;
        case 4: return (int32_t)((ticks / TICKS_PER_DAY + 1) % 7);
        case 5: return (int32_t)(ticks / TICKS_PER_HOUR % 24);
        case 6: return (int32_t)(ticks / TICKS_PER_MINUTE % 60);
        case 7: return (int32_t)(ticks / TICKS_PER_SECOND % 60);
        case 8: return (int32_t)(ticks / TICKS_PER_MS % 1000);
        case 9: return (int32_t)(ticks / 10 % 1000);
        case 10: return (int32_t)(ticks % 10 * 100);
    }
    return 0;
}

int64_t csrt_datetime_add_ticks(int64_t ticks, int64_t value) {
    int64_t r;
    if (__builtin_add_overflow(ticks, value, &r) || r < 0 || r > MAX_TICKS) datetime_range();
    return r;
}

/// AddDays/AddHours/...(double): integral and fractional parts separately (.NET 7+).
int64_t csrt_datetime_add_units(int64_t ticks, double value, int64_t ticks_per_unit) {
    double max_units = (double)MAX_TICKS / (double)ticks_per_unit;
    if (isnan(value) || fabs(value) > max_units) csrt_throw(&mt_range, ascii("Value to add was out of range. (Parameter 'value')"));
    double integral = trunc(value);
    double fractional = value - integral;
    int64_t delta = (int64_t)integral * ticks_per_unit + (int64_t)(fractional * (double)ticks_per_unit);
    return csrt_datetime_add_ticks(ticks, delta);
}

int64_t csrt_datetime_add_months(int64_t ticks, int32_t months) {
    if (months < -120000 || months > 120000) csrt_throw(&mt_range, ascii("Months value must be between +/-120000. (Parameter 'months')"));
    int y, m, d, doy;
    date_parts(ticks, &y, &m, &d, &doy);
    int i = m - 1 + months;
    if (i >= 0) { m = i % 12 + 1; y += i / 12; }
    else { m = 12 + (i + 1) % 12; y += (i - 11) / 12; }
    if (y < 1 || y > 9999) datetime_range();
    int dim = csrt_datetime_days_in_month(y, m);
    if (d > dim) d = dim;
    return date_to_ticks(y, m, d) + ticks % TICKS_PER_DAY;
}

int64_t csrt_datetime_add_years(int64_t ticks, int32_t years) {
    if (years < -10000 || years > 10000) csrt_throw(&mt_range, ascii("Years value must be between +/-10000. (Parameter 'years')"));
    return csrt_datetime_add_months(ticks, years * 12);
}

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

static void datetime_custom(Buf* b, int64_t ticks, const char* f) {
    int y, mo, d, doy;
    date_parts(ticks, &y, &mo, &d, &doy);
    int h = (int)(ticks / TICKS_PER_HOUR % 24), mi = (int)(ticks / TICKS_PER_MINUTE % 60), s = (int)(ticks / TICKS_PER_SECOND % 60);
    int64_t frac = ticks % TICKS_PER_SECOND;
    int dow = (int)((ticks / TICKS_PER_DAY + 1) % 7);
    for (int i = 0; f[i];) {
        char c = f[i];
        int n = run_length(f, i);
        switch (c) {
            case 'y':
                if (n <= 2) putnum(b, y % 100, n);
                else putnum(b, y, n);
                i += n; break;
            case 'M':
                if (n <= 2) putnum(b, mo, n);
                else if (n == 3) { char t[4]; memcpy(t, month_names[mo - 1], 3); t[3] = 0; puts_(b, t); }
                else puts_(b, month_names[mo - 1]);
                i += n; break;
            case 'd':
                if (n <= 2) putnum(b, d, n);
                else if (n == 3) { char t[4]; memcpy(t, day_names[dow], 3); t[3] = 0; puts_(b, t); }
                else puts_(b, day_names[dow]);
                i += n; break;
            case 'H': putnum(b, h, n > 1 ? 2 : 1); i += n; break;
            case 'h': putnum(b, h % 12 == 0 ? 12 : h % 12, n > 1 ? 2 : 1); i += n; break;
            case 'm': putnum(b, mi, n > 1 ? 2 : 1); i += n; break;
            case 's': putnum(b, s, n > 1 ? 2 : 1); i += n; break;
            case 'f': append_fraction(b, frac, n > 7 ? 7 : n, false); i += n; break;
            case 'F': {
                size_t before = b->n;
                append_fraction(b, frac, n > 7 ? 7 : n, true);
                if (b->n == before && before > 0 && b->p[before - 1] == '.') b->n--;  // drop a dangling '.'
                i += n; break;
            }
            case 't': if (n == 1) put(b, h < 12 ? 'A' : 'P'); else puts_(b, h < 12 ? "AM" : "PM"); i += n; break;
            case 'K': case 'z': i += n; break;  // Kind/offset not modelled (Unspecified)
            case '\\': if (f[i + 1]) put(b, f[i + 1]); i += 2; break;
            case '\'': case '"': {
                int j = i + 1;
                while (f[j] && f[j] != c) put(b, f[j++]);
                i = f[j] ? j + 1 : j;
                break;
            }
            case '%': i++; break;
            default: put(b, c); i++; break;
        }
    }
}

static const char* standard_pattern(char f) {
    switch (f) {
        case 'd': return "MM/dd/yyyy";
        case 'D': return "dddd, dd MMMM yyyy";
        case 'f': return "dddd, dd MMMM yyyy HH:mm";
        case 'F': return "dddd, dd MMMM yyyy HH:mm:ss";
        case 'g': return "MM/dd/yyyy HH:mm";
        case 'G': return "MM/dd/yyyy HH:mm:ss";
        case 'M': case 'm': return "MMMM dd";
        case 'Y': case 'y': return "yyyy MMMM";
        case 't': return "HH:mm";
        case 'T': return "HH:mm:ss";
        case 's': return "yyyy'-'MM'-'dd'T'HH':'mm':'ss";
        case 'u': return "yyyy'-'MM'-'dd HH':'mm':'ss'Z'";
        case 'o': case 'O': return "yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffff";
        case 'R': case 'r': return "ddd, dd MMM yyyy HH':'mm':'ss 'GMT'";
    }
    return NULL;
}

csrt_String* csrt_datetime_to_string(int64_t ticks) {
    Buf b = {0};
    datetime_custom(&b, ticks, "MM/dd/yyyy HH:mm:ss");
    return buf_to_string(&b);
}

csrt_String* csrt_datetime_format(int64_t ticks, csrt_String* fmt) {
    char f[128]; spec_to_c(fmt, f, sizeof f);
    Buf b = {0};
    if (!f[0]) datetime_custom(&b, ticks, "MM/dd/yyyy HH:mm:ss");
    else if (!f[1]) {
        const char* p = standard_pattern(f[0]);
        if (!p) { free(b.p); csrt_throw(&mt_format, ascii("Input string was not in a correct format.")); }
        datetime_custom(&b, ticks, p);
    } else datetime_custom(&b, ticks, f);
    return buf_to_string(&b);
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
