// csrt bclmisc: System.Array helpers, System.Convert, Environment extras, StringBuilder
// Insert/Remove/Replace, more string.Split overloads. Element kinds (sorting/searching) are
// assigned by the compiler (BclValueTypes.ElementKind):
//   1 sbyte 2 short 3 int 4 long 5 byte/bool 6 ushort/char 7 uint 8 ulong 9 float 10 double
//   11 string (ordinal) 12 reference (virtual Equals) 0 other value type (bytewise)
#define _DEFAULT_SOURCE
#include "csrt.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

static const csrt_MethodTable mt_argument = { NULL, "System.ArgumentException", 0, 0 };
static const csrt_MethodTable mt_argument_null = { NULL, "System.ArgumentNullException", 0, 0 };
static const csrt_MethodTable mt_range = { NULL, "System.ArgumentOutOfRangeException", 0, 0 };
static const csrt_MethodTable mt_format = { NULL, "System.FormatException", 0, 0 };
static const csrt_MethodTable mt_overflow = { NULL, "System.OverflowException", 0, 0 };

static csrt_String* ascii(const char* text) {
    int32_t n = (int32_t)strlen(text);
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)n));
    s->length = n;
    for (int32_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    return s;
}

static csrt_String* from_chars(const uint16_t* chars, int32_t n) {
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)n));
    s->length = n;
    if (n) memcpy(s->chars, chars, 2 * (size_t)n);
    return s;
}

static void need_array(csrt_Array* a, const char* param) {
    if (!a) {
        char msg[96];
        snprintf(msg, sizeof msg, "Value cannot be null. (Parameter '%s')", param);
        csrt_throw(&mt_argument_null, ascii(msg));
    }
}

// ---------------------------------------------------------------------------------------------
// System.Array
// ---------------------------------------------------------------------------------------------
static void range_check(csrt_Array* a, int64_t index, int64_t* length) {
    if (*length < 0) *length = a->length - index;
    if (index < 0 || *length < 0 || index + *length > a->length)
        csrt_throw(&mt_argument, ascii("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection."));
}

void csrt_array_reverse(csrt_Array* a, int64_t index, int64_t length, int64_t elem) {
    need_array(a, "array");
    range_check(a, index, &length);
    uint8_t tmp[64];
    uint8_t* base = a->data + index * elem;
    for (int64_t i = 0, j = length - 1; i < j; i++, j--) {
        uint8_t* x = base + i * elem;
        uint8_t* y = base + j * elem;
        for (int64_t off = 0; off < elem; off += (int64_t)sizeof tmp) {
            size_t n = (size_t)(elem - off < (int64_t)sizeof tmp ? elem - off : (int64_t)sizeof tmp);
            memcpy(tmp, x + off, n); memcpy(x + off, y + off, n); memcpy(y + off, tmp, n);
        }
    }
}

void csrt_array_clear(csrt_Array* a, int64_t index, int64_t length, int64_t elem) {
    need_array(a, "array");
    if (length < 0) { index = 0; length = a->length; }
    if (index < 0 || length < 0 || index + length > a->length)
        csrt_throw(&mt_range, ascii("Index was out of range. Must be non-negative and less than the size of the collection."));
    memset(a->data + index * elem, 0, (size_t)(length * elem));
}

void csrt_array_copy(csrt_Array* src, int64_t si, csrt_Array* dst, int64_t di, int64_t length, int64_t elem) {
    need_array(src, "sourceArray");
    need_array(dst, "destinationArray");
    if (length < 0) csrt_throw(&mt_range, ascii("Non-negative number required. (Parameter 'length')"));
    if (si < 0 || di < 0 || si + length > src->length || di + length > dst->length)
        csrt_throw(&mt_argument, ascii("Source array was not long enough. Check the source index, length, and the array's lower bounds. (Parameter 'sourceArray')"));
    memmove(dst->data + di * elem, src->data + si * elem, (size_t)(length * elem));
}

void csrt_array_fill(csrt_Array* a, const void* value, int64_t elem) {
    need_array(a, "array");
    for (int64_t i = 0; i < a->length; i++) memcpy(a->data + i * elem, value, (size_t)elem);
}

csrt_Array* csrt_array_resize(csrt_Array* a, int64_t length, int64_t elem) {
    if (length < 0) csrt_throw(&mt_range, ascii("Non-negative number required. (Parameter 'newSize')"));
    csrt_Array* r = csrt_new_array(elem, length);
    if (a) memcpy(r->data, a->data, (size_t)((a->length < length ? a->length : length) * elem));
    return r;
}

typedef bool (*EqualsFn)(void*, void*);

static bool element_equals(const uint8_t* p, const void* value, int64_t elem, int32_t kind) {
    switch (kind) {
        case 9: { float x, y; memcpy(&x, p, 4); memcpy(&y, value, 4); return x == y || (isnan(x) && isnan(y)); }
        case 10: { double x, y; memcpy(&x, p, 8); memcpy(&y, value, 8); return x == y || (isnan(x) && isnan(y)); }
        case 11: return csrt_string_equals(*(csrt_String**)p, *(csrt_String**)value);
        case 12: {
            void* x = *(void**)p;
            void* y = *(void**)value;
            if (!x) return y == NULL;
            const void* const* vtable = (const void* const*)(((csrt_Object*)x)->mt + 1);
            return ((EqualsFn)vtable[CSRT_SLOT_EQUALS])(x, y);
        }
        default: return memcmp(p, value, (size_t)elem) == 0;
    }
}

int64_t csrt_array_index_of(csrt_Array* a, const void* value, int64_t elem, int32_t kind) {
    need_array(a, "array");
    for (int64_t i = 0; i < a->length; i++)
        if (element_equals(a->data + i * elem, value, elem, kind)) return i;
    return -1;
}

static int64_t elem_size(int32_t kind) {
    static const int64_t sizes[] = { 0, 1, 2, 4, 8, 1, 2, 4, 8, 4, 8, 8 };
    return sizes[kind];
}

#define CMP(T) static int cmp_##T(const void* a, const void* b) { T x = *(const T*)a, y = *(const T*)b; return (x > y) - (x < y); }
CMP(int8_t) CMP(int16_t) CMP(int32_t) CMP(int64_t) CMP(uint8_t) CMP(uint16_t) CMP(uint32_t) CMP(uint64_t)
// float/double: NaN sorts first (double.CompareTo)
static int cmp_float(const void* a, const void* b) {
    float x = *(const float*)a, y = *(const float*)b;
    if (isnan(x)) return isnan(y) ? 0 : -1;
    if (isnan(y)) return 1;
    return (x > y) - (x < y);
}
static int cmp_double(const void* a, const void* b) {
    double x = *(const double*)a, y = *(const double*)b;
    if (isnan(x)) return isnan(y) ? 0 : -1;
    if (isnan(y)) return 1;
    return (x > y) - (x < y);
}
int32_t csrt_string_compare_ordinal(csrt_String* a, csrt_String* b);
static int cmp_string(const void* a, const void* b) {
    int32_t c = csrt_string_compare_ordinal(*(csrt_String* const*)a, *(csrt_String* const*)b);
    return (c > 0) - (c < 0);
}

typedef int (*CmpFn)(const void*, const void*);
static CmpFn comparer(int32_t kind) {
    static const CmpFn fns[] = { NULL, cmp_int8_t, cmp_int16_t, cmp_int32_t, cmp_int64_t, cmp_uint8_t, cmp_uint16_t,
                                 cmp_uint32_t, cmp_uint64_t, cmp_float, cmp_double, cmp_string };
    return fns[kind];
}

void csrt_array_sort(csrt_Array* a, int32_t kind) {
    need_array(a, "array");
    qsort(a->data, (size_t)a->length, (size_t)elem_size(kind), comparer(kind));
}

/// Array.BinarySearch: index, or the bitwise complement of the insertion point.
int32_t csrt_array_binary_search(csrt_Array* a, const void* value, int32_t kind) {
    need_array(a, "array");
    CmpFn cmp = comparer(kind);
    int64_t size = elem_size(kind), lo = 0, hi = a->length - 1;
    while (lo <= hi) {
        int64_t mid = lo + ((hi - lo) >> 1);
        int c = cmp(a->data + mid * size, value);
        if (c == 0) return (int32_t)mid;
        if (c < 0) lo = mid + 1; else hi = mid - 1;
    }
    return (int32_t)~lo;
}

// ---------------------------------------------------------------------------------------------
// System.Convert / bool.Parse
// ---------------------------------------------------------------------------------------------
static bool equals_ignore_case(csrt_String* s, int32_t start, int32_t end, const char* word) {
    int32_t n = (int32_t)strlen(word);
    if (end - start != n) return false;
    for (int32_t i = 0; i < n; i++) {
        uint16_t c = s->chars[start + i];
        if (c >= 'A' && c <= 'Z') c = (uint16_t)(c + 32);
        if (c != (uint16_t)word[i]) return false;
    }
    return true;
}

bool csrt_bool_parse(csrt_String* s) {
    if (!s) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 'value')"));
    int32_t a = 0, b = s->length;
    while (a < b && (s->chars[a] == ' ' || s->chars[a] == '\t')) a++;
    while (b > a && (s->chars[b - 1] == ' ' || s->chars[b - 1] == '\t' || s->chars[b - 1] == 0)) b--;
    if (equals_ignore_case(s, a, b, "true")) return true;
    if (equals_ignore_case(s, a, b, "false")) return false;
    char msg[160];
    snprintf(msg, sizeof msg, "String '");
    size_t o = strlen(msg);
    for (int32_t i = 0; i < s->length && o < sizeof msg - 40; i++) msg[o++] = s->chars[i] < 128 ? (char)s->chars[i] : '?';
    msg[o] = 0;
    strcat(msg, "' was not recognized as a valid Boolean.");
    csrt_throw(&mt_format, ascii(msg));
}

/// Convert.ToInt32/64(string, fromBase) for bases 2, 8, 10, 16 (two's complement for 2/8/16).
int64_t csrt_convert_parse_base(csrt_String* s, int32_t base, int32_t bits) {
    if (base != 2 && base != 8 && base != 10 && base != 16)
        csrt_throw(&mt_argument, ascii("Invalid Base."));
    if (!s) return 0;
    int32_t i = 0, n = s->length;
    bool negative = false;
    if (base == 10 && i < n && s->chars[i] == '-') { negative = true; i++; }
    if (base == 16 && i + 1 < n && s->chars[i] == '0' && (s->chars[i + 1] == 'x' || s->chars[i + 1] == 'X')) i += 2;
    if (i >= n) csrt_throw(&mt_format, ascii("Could not find any recognizable digits."));
    unsigned __int128 v = 0;
    for (; i < n; i++) {
        uint16_t c = s->chars[i];
        int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : 99;
        if (d >= base) csrt_throw(&mt_format, ascii("Additional non-parsable characters are at the end of the string."));
        v = v * (unsigned)base + (unsigned)d;
        if (v > ((unsigned __int128)1 << 64)) csrt_throw(&mt_overflow, ascii(bits == 32 ? "Value was either too large or too small for an Int32." : "Value was either too large or too small for an Int64."));
    }
    if (base == 10) {
        __int128 sv = negative ? -(__int128)v : (__int128)v;
        __int128 lo = bits == 32 ? INT32_MIN : INT64_MIN, hi = bits == 32 ? INT32_MAX : INT64_MAX;
        if (sv < lo || sv > hi) csrt_throw(&mt_overflow, ascii(bits == 32 ? "Value was either too large or too small for an Int32." : "Value was either too large or too small for an Int64."));
        return (int64_t)sv;
    }
    unsigned __int128 limit = bits == 32 ? ((unsigned __int128)1 << 32) - 1 : (((unsigned __int128)1 << 64) - 1);
    if (v > limit) csrt_throw(&mt_overflow, ascii(bits == 32 ? "Value was either too large or too small for an Int32." : "Value was either too large or too small for an Int64."));
    return bits == 32 ? (int64_t)(int32_t)(uint32_t)v : (int64_t)(uint64_t)v;
}

/// Convert.ToString(value, toBase): bases 2/8/16 print the two's complement bit pattern.
csrt_String* csrt_convert_to_base(int64_t value, int32_t base, int32_t bits) {
    if (base != 2 && base != 8 && base != 10 && base != 16) csrt_throw(&mt_argument, ascii("Invalid Base."));
    char buf[72];
    if (base == 10) { snprintf(buf, sizeof buf, "%lld", (long long)value); return ascii(buf); }
    uint64_t v = bits == 32 ? (uint64_t)(uint32_t)value : (uint64_t)value;
    char tmp[72];
    int n = 0;
    do { tmp[n++] = "0123456789abcdef"[v % (unsigned)base]; v /= (unsigned)base; } while (v);
    for (int i = 0; i < n; i++) buf[i] = tmp[n - 1 - i];
    buf[n] = 0;
    return ascii(buf);
}

/// Convert.ToInt32(double): round half to even, OverflowException outside int range.
int32_t csrt_convert_double_to_i32(double v) {
    double r = nearbyint(v);
    if (!(r >= -2147483648.0 && r <= 2147483647.0)) csrt_throw(&mt_overflow, ascii("Value was either too large or too small for an Int32."));
    return (int32_t)r;
}

// ---------------------------------------------------------------------------------------------
// System.Environment
// ---------------------------------------------------------------------------------------------
int32_t csrt_environment_processor_count(void) {
    long n = sysconf(_SC_NPROCESSORS_ONLN);
    return n > 0 ? (int32_t)n : 1;
}

/// Environment.GetCommandLineArgs(): [executable path, args...] (read from /proc/self/cmdline).
csrt_Array* csrt_environment_command_line_args(void) {
    FILE* f = fopen("/proc/self/cmdline", "rb");
    char buf[65536];
    size_t n = f ? fread(buf, 1, sizeof buf - 1, f) : 0;
    if (f) fclose(f);
    buf[n] = 0;
    int64_t count = 0;
    for (size_t i = 0; i < n; i++) if (buf[i] == 0) count++;
    if (n && buf[n - 1] != 0) count++;
    csrt_Array* arr = csrt_new_array(sizeof(void*), count);
    csrt_String** items = (csrt_String**)arr->data;
    size_t start = 0;
    for (int64_t k = 0; k < count; k++) {
        size_t len = strlen(buf + start);
        items[k] = ascii(buf + start);  // ASCII/Latin-1 paths; UTF-8 args are rare here
        start += len + 1;
    }
    return arr;
}

// ---------------------------------------------------------------------------------------------
// string.Split overloads
// ---------------------------------------------------------------------------------------------
static bool is_ws(uint16_t c) { return c == ' ' || (c >= 9 && c <= 13) || c == 0xA0; }

static csrt_Array* split_core(csrt_String* s, csrt_String* sep_string, csrt_Array* sep_chars, int32_t options) {
    if (!s) csrt_throw_null_reference();
    int32_t cap = 8, count = 0;
    int32_t* bounds = malloc(sizeof(int32_t) * 2 * (size_t)cap);
    int32_t start = 0;
    int32_t sep_len = sep_string ? sep_string->length : 1;
    for (int32_t i = 0; i <= s->length;) {
        bool at_sep = false;
        if (i < s->length) {
            if (sep_string) {
                at_sep = sep_string->length > 0 && i + sep_string->length <= s->length &&
                         memcmp(s->chars + i, sep_string->chars, 2 * (size_t)sep_string->length) == 0;
            } else if (sep_chars && sep_chars->length > 0) {
                const uint16_t* cs = (const uint16_t*)sep_chars->data;
                for (int64_t k = 0; k < sep_chars->length; k++) if (cs[k] == s->chars[i]) { at_sep = true; break; }
            } else {
                at_sep = is_ws(s->chars[i]);  // null/empty separator: whitespace
            }
        }
        if (i < s->length && !at_sep) { i++; continue; }
        int32_t a = start, b = i;
        if (options & 2) { while (a < b && is_ws(s->chars[a])) a++; while (b > a && is_ws(s->chars[b - 1])) b--; }
        if (!((options & 1) && a == b)) {
            if (count == cap) { cap *= 2; bounds = realloc(bounds, sizeof(int32_t) * 2 * (size_t)cap); }
            bounds[2 * count] = a;
            bounds[2 * count + 1] = b;
            count++;
        }
        if (i >= s->length) break;
        i += sep_string ? sep_len : 1;
        start = i;
        if (i == s->length) {  // trailing separator: one more (empty) piece
            int32_t a2 = i, b2 = i;
            if (!(options & 1)) {
                if (count == cap) { cap *= 2; bounds = realloc(bounds, sizeof(int32_t) * 2 * (size_t)cap); }
                bounds[2 * count] = a2; bounds[2 * count + 1] = b2; count++;
            }
            break;
        }
    }
    csrt_Array* result = csrt_new_array(sizeof(void*), count);
    for (int32_t k = 0; k < count; k++)
        ((csrt_String**)result->data)[k] = from_chars(s->chars + bounds[2 * k], bounds[2 * k + 1] - bounds[2 * k]);
    free(bounds);
    return result;
}

csrt_Array* csrt_string_split_string(csrt_String* s, csrt_String* separator, int32_t options) {
    if (separator && separator->length == 0) {  // empty separator: no split
        csrt_Array* r = csrt_new_array(sizeof(void*), 1);
        ((csrt_String**)r->data)[0] = s;
        return r;
    }
    return split_core(s, separator, NULL, options);
}

csrt_Array* csrt_string_split_chars(csrt_String* s, csrt_Array* separators, int32_t options) {
    return split_core(s, NULL, separators, options);
}

// ---------------------------------------------------------------------------------------------
// StringBuilder extras (layout shared with strings.c: { mt, char[] buffer, i32 length })
// ---------------------------------------------------------------------------------------------
typedef struct { const csrt_MethodTable* mt; csrt_Array* buffer; int32_t length; } StringBuilder;
StringBuilder* csrt_sb_append_string(StringBuilder* sb, csrt_String* s);

static void sb_reserve(StringBuilder* sb, int64_t extra) {
    if (!sb) csrt_throw_null_reference();
    int64_t need = sb->length + extra;
    if (need <= sb->buffer->length) return;
    int64_t cap = sb->buffer->length * 2;
    if (cap < need) cap = need;
    csrt_Array* grown = csrt_new_array(sizeof(uint16_t), cap);
    memcpy(grown->data, sb->buffer->data, sizeof(uint16_t) * (size_t)sb->length);
    sb->buffer = grown;
}

StringBuilder* csrt_sb_insert(StringBuilder* sb, int32_t index, csrt_String* s) {
    if (!sb) csrt_throw_null_reference();
    if (index < 0 || index > sb->length) csrt_throw(&mt_range, ascii("Index was out of range. Must be non-negative and less than or equal to the size of the collection. (Parameter 'index')"));
    if (!s || !s->length) return sb;
    sb_reserve(sb, s->length);
    uint16_t* chars = (uint16_t*)sb->buffer->data;
    memmove(chars + index + s->length, chars + index, 2 * (size_t)(sb->length - index));
    memcpy(chars + index, s->chars, 2 * (size_t)s->length);
    sb->length += s->length;
    return sb;
}

StringBuilder* csrt_sb_remove(StringBuilder* sb, int32_t start, int32_t length) {
    if (!sb) csrt_throw_null_reference();
    if (length < 0 || start < 0 || start + length > sb->length)
        csrt_throw(&mt_range, ascii("Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'startIndex')"));
    uint16_t* chars = (uint16_t*)sb->buffer->data;
    memmove(chars + start, chars + start + length, 2 * (size_t)(sb->length - start - length));
    sb->length -= length;
    return sb;
}

csrt_String* csrt_string_replace(csrt_String* s, csrt_String* from, csrt_String* to);

StringBuilder* csrt_sb_replace(StringBuilder* sb, csrt_String* from, csrt_String* to) {
    if (!sb) csrt_throw_null_reference();
    csrt_String* current = from_chars((const uint16_t*)sb->buffer->data, sb->length);
    csrt_String* replaced = csrt_string_replace(current, from, to);
    sb->length = 0;
    return csrt_sb_append_string(sb, replaced);
}

uint16_t csrt_sb_get_char(StringBuilder* sb, int32_t index) {
    if (!sb) csrt_throw_null_reference();
    if (index < 0 || index >= sb->length) csrt_throw(&mt_range, ascii("Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')"));
    return ((uint16_t*)sb->buffer->data)[index];
}
