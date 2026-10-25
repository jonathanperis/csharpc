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
#ifdef _WIN32
#include "csrt_pthread.h"
#endif

static const csrt_MethodTable mt_argument = { NULL, "System.ArgumentException", 0, 0 };
static const csrt_MethodTable mt_argument_null = { NULL, "System.ArgumentNullException", 0, 0 };
static const csrt_MethodTable mt_range = { NULL, "System.ArgumentOutOfRangeException", 0, 0 };
static const csrt_MethodTable mt_format = { NULL, "System.FormatException", 0, 0 };
static const csrt_MethodTable mt_index = { NULL, "System.IndexOutOfRangeException", 0, 0 };
static const csrt_MethodTable mt_overflow = { NULL, "System.OverflowException", 0, 0 };

static csrt_String* ascii(const char* text) {
    int32_t n = (int32_t)strlen(text);
    csrt_String* s = csrt_string_alloc_zeroed((int64_t)n);
    s->length = n;
    for (int32_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    return s;
}

static csrt_String* from_chars(const uint16_t* chars, int32_t n) {
    csrt_String* s = csrt_string_alloc_zeroed((int64_t)n);
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
    if (*length == INT64_MIN) { *length = a->length - index; return; }  // whole array (Reverse(array))
    if (index < 0) csrt_throw(&mt_range, ascii("Non-negative number required. (Parameter 'index')"));
    if (*length < 0) csrt_throw(&mt_range, ascii("Non-negative number required. (Parameter 'length')"));
    if (a->length - index < *length)
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
    // .NET: IndexOutOfRangeException, not an ArgumentException.
    if (index < 0 || length < 0 || index + length > a->length)
        csrt_throw(&mt_index, ascii("Index was outside the bounds of the array."));
    memset(a->data + index * elem, 0, (size_t)(length * elem));
}

void csrt_array_copy(csrt_Array* src, int64_t si, csrt_Array* dst, int64_t di, int64_t length, int64_t elem) {
    need_array(src, "sourceArray");
    need_array(dst, "destinationArray");
    if (length < 0) csrt_throw(&mt_range, ascii("Non-negative number required. (Parameter 'length')"));
    // .NET checks the source window first, then the destination (and blames the right array).
    if (si < 0) csrt_throw(&mt_range, ascii("Number was less than the array's lower bound in the first dimension. (Parameter 'sourceIndex')"));
    if (di < 0) csrt_throw(&mt_range, ascii("Number was less than the array's lower bound in the first dimension. (Parameter 'destinationIndex')"));
    if (si > src->length - length)
        csrt_throw(&mt_argument, ascii("Source array was not long enough. Check the source index, length, and the array's lower bounds. (Parameter 'sourceArray')"));
    if (di > dst->length - length)
        csrt_throw(&mt_argument, ascii("Destination array was not long enough. Check the destination index, length, and the array's lower bounds. (Parameter 'destinationArray')"));
    void* dest = dst->data + di * elem;
    size_t bytes = (size_t)(length * elem);
    // Reference arrays: dirty cards before the copy so a suspend cannot hide old→young edges.
    if (elem == (int64_t)sizeof(void*)) csrt_gc_cards_dirty(dest, bytes);
    memmove(dest, src->data + si * elem, bytes);
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
int32_t csrt_string_compare_culture(csrt_String* a, csrt_String* b, bool ignore_case);
static int cmp_string(const void* a, const void* b) {  // Comparer<string>.Default: culture-sensitive
    int32_t c = csrt_string_compare_culture(*(csrt_String* const*)a, *(csrt_String* const*)b, false);
    return (c > 0) - (c < 0);
}

typedef int (*CmpFn)(const void*, const void*);
static CmpFn comparer(int32_t kind) {
    static const CmpFn fns[] = { NULL, cmp_int8_t, cmp_int16_t, cmp_int32_t, cmp_int64_t, cmp_uint8_t, cmp_uint16_t,
                                 cmp_uint32_t, cmp_uint64_t, cmp_float, cmp_double, cmp_string };
    return fns[kind];
}

static int32_t kind_cmp(void* ctx, const void* a, const void* b);
void csrt_array_introsort(csrt_Array* a, int64_t index, int64_t length, int64_t elem,
                          int32_t (*cmp)(void*, const void*, const void*), void* ctx);

void csrt_array_sort(csrt_Array* a, int32_t kind) {
    need_array(a, "array");
    csrt_array_introsort(a, 0, a->length, elem_size(kind), kind_cmp, (void*)comparer(kind));
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
    return false;  // wasm32: throwing returns (CSRT_THROWS)
}

// ---------------------------------------------------------------------------------------------
// System.Environment
// ---------------------------------------------------------------------------------------------
int32_t csrt_processor_count(void);

int32_t csrt_environment_processor_count(void) {
#ifdef __wasi__
    long n = sysconf(_SC_NPROCESSORS_ONLN);
    return n > 0 ? (int32_t)n : 1;
#else
    return csrt_processor_count();  // threads.c: the process's CPU affinity, as .NET
#endif
}

/// Environment.GetCommandLineArgs(): [executable path, args...] (read from /proc/self/cmdline;
/// on macOS from the process's argv).
#if defined(_WIN32) || defined(__wasi__)
// csrt.c (Windows: from the UTF-16 command line; WASI: argv)
#elif defined(__APPLE__)
#include <crt_externs.h>
csrt_Array* csrt_environment_command_line_args(void) {
    int argc = *_NSGetArgc();
    char** argv = *_NSGetArgv();
    csrt_Array* arr = csrt_new_array(sizeof(void*), argc);
    for (int i = 0; i < argc; i++) ((csrt_String**)arr->data)[i] = ascii(argv[i]);
    return arr;
}
#else
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
#endif

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
// Culture-sensitive string comparison is C# (corelib Globalization/CultureStrings.cs, over the port of
// .NET's CompareInfo and ICU). Array.Sort of strings calls it through the corelib's RuntimeExport;
// this weak definition only satisfies the linker in programs that never sort strings.
// ---------------------------------------------------------------------------------------------
__attribute__((weak)) int32_t csrt_string_compare_culture(csrt_String* a, csrt_String* b, bool ignore_case) {
    (void)ignore_case;
    return csrt_string_compare_ordinal(a, b);
}

#ifdef __wasi__
// ---------------------------------------------------------------------------------------------
// WASI has no ICU (nothing loads dynamically), so programs run in invariant globalization mode. Instead
// of the ordinal comparisons of .NET's invariant mode, culture comparisons there approximate ICU's root
// collation for Latin text (CompareInfo.cs): three levels, base letter (case/accent-insensitive) <
// accent < case (lowercase first); ASCII order whitespace < punctuation < symbols < digits < letters;
// control characters are ignorable; other characters sort after Latin letters by code point.
// ---------------------------------------------------------------------------------------------
static const char k_ascii_order[] = "\t\n\v\f\r _-,;:!?.'\"()[]{}@*/\\&#%`^+<=>|~$0123456789abcdefghijklmnopqrstuvwxyz";

typedef struct { uint32_t primary; uint8_t secondary, tertiary; } Collation;

static Collation collate(uint16_t c) {
    Collation k = { 0, 0, 0 };
    if (c < 0x20 && c != '\t' && c != '\n' && c != '\v' && c != '\f' && c != '\r') return k;  // ignorable
    if (c == 0x7F) return k;
    uint16_t base = c;
    uint8_t accent = 0;
    // Latin-1 letters: fold to base letter + accent level
    static const char latin1_base[] = "AAAAAAACEEEEIIIIDNOOOOO*OUUUUYTsaaaaaaaceeeeiiiidnooooo/ouuuuyty";
    if (c >= 0xC0 && c <= 0xFF && latin1_base[c - 0xC0] != '*' && latin1_base[c - 0xC0] != '/') {
        base = (uint8_t)latin1_base[c - 0xC0];
        accent = (uint8_t)(1 + (c & 0x1F));
        if (c == 0xDF) { base = 's'; accent = 30; }   // ß ~ ss
    }
    bool upper = base >= 'A' && base <= 'Z';
    uint16_t lower = upper ? (uint16_t)(base + 32) : base;
    if (lower < 128) {
        const char* p = strchr(k_ascii_order, (char)lower);
        if (p && lower != 0) {
            k.primary = (uint32_t)(p - k_ascii_order) + 1;
            k.secondary = accent;
            k.tertiary = upper ? 1 : 0;
            return k;
        }
    }
    k.primary = 0x1000u + c;
    return k;
}

static int compare_level(csrt_String* a, csrt_String* b, int level, bool ignore_case) {
    int32_t i = 0, j = 0;
    for (;;) {
        Collation x = { 0, 0, 0 }, y = { 0, 0, 0 };
        while (i < a->length && (x = collate(a->chars[i])).primary == 0) i++;
        while (j < b->length && (y = collate(b->chars[j])).primary == 0) j++;
        bool ea = i >= a->length, eb = j >= b->length;
        if (ea || eb) return ea && eb ? 0 : ea ? -1 : 1;
        uint32_t vx = level == 1 ? x.primary : level == 2 ? x.secondary : (ignore_case ? 0 : x.tertiary);
        uint32_t vy = level == 1 ? y.primary : level == 2 ? y.secondary : (ignore_case ? 0 : y.tertiary);
        if (vx != vy) return vx < vy ? -1 : 1;
        i++; j++;
    }
}

int32_t csrt_root_collation_compare(csrt_String* a, csrt_String* b, int32_t ignore_case) {
    if (a == b) return 0;
    if (!a) return -1;
    if (!b) return 1;
    for (int level = 1; level <= 3; level++) {
        int c = compare_level(a, b, level, ignore_case != 0);
        if (c) return c;
    }
    return 0;
}
#endif

int32_t csrt_string_compare_ordinal_ignore_case(csrt_String* a, csrt_String* b) {
    if (a == b) return 0;
    if (!a) return -1;
    if (!b) return 1;
    int32_t n = a->length < b->length ? a->length : b->length;
    for (int32_t i = 0; i < n; i++) {
        uint16_t x = a->chars[i], y = b->chars[i];
        if (x >= 'a' && x <= 'z') x = (uint16_t)(x - 32);
        if (y >= 'a' && y <= 'z') y = (uint16_t)(y - 32);
        if (x != y) return (int32_t)x - (int32_t)y;
    }
    return a->length - b->length;
}

bool csrt_string_is_null_or_white_space(csrt_String* s) {
    if (!s) return true;
    for (int32_t i = 0; i < s->length; i++) if (!is_ws(s->chars[i]) && s->chars[i] != 0x85 && !(s->chars[i] >= 0x2000 && s->chars[i] <= 0x200A) && s->chars[i] != 0x3000) return false;
    return true;
}

// static object.Equals(a, b) lives in src/runtime/records.c (csrt_object_equals_static).


// ---------------------------------------------------------------------------------------------
// Array.Sort: a port of .NET's ArraySortHelper<T>.IntrospectiveSort, so results (including the
// order of elements that compare equal) match the CLR exactly. Works on elements of any size;
// cmp(ctx, a, b) receives element addresses.
// ---------------------------------------------------------------------------------------------
typedef int32_t (*CtxCmp)(void* ctx, const void* a, const void* b);
typedef struct { uint8_t* base; int64_t elem; CtxCmp cmp; void* ctx; uint8_t* tmp; uint8_t* tmp2; } Sorter;

#define AT(s, i) ((s)->base + (int64_t)(i) * (s)->elem)

static void sw_swap(Sorter* s, int64_t i, int64_t j) {
    if (i == j) return;
    memcpy(s->tmp, AT(s, i), (size_t)s->elem);
    memmove(AT(s, i), AT(s, j), (size_t)s->elem);
    memcpy(AT(s, j), s->tmp, (size_t)s->elem);
}
static void swap_if_greater(Sorter* s, int64_t i, int64_t j) {
    if (s->cmp(s->ctx, AT(s, i), AT(s, j)) > 0) sw_swap(s, i, j);
}
static void insertion_sort(Sorter* s, int64_t lo, int64_t n) {
    for (int64_t i = 0; i < n - 1; i++) {
        memcpy(s->tmp2, AT(s, lo + i + 1), (size_t)s->elem);
        int64_t j = i;
        while (j >= 0 && s->cmp(s->ctx, s->tmp2, AT(s, lo + j)) < 0) {
            memcpy(AT(s, lo + j + 1), AT(s, lo + j), (size_t)s->elem);
            j--;
        }
        memcpy(AT(s, lo + j + 1), s->tmp2, (size_t)s->elem);
    }
}
static void down_heap(Sorter* s, int64_t lo, int64_t i, int64_t n) {
    memcpy(s->tmp2, AT(s, lo + i - 1), (size_t)s->elem);
    while (i <= n >> 1) {
        int64_t child = 2 * i;
        if (child < n && s->cmp(s->ctx, AT(s, lo + child - 1), AT(s, lo + child)) < 0) child++;
        if (!(s->cmp(s->ctx, s->tmp2, AT(s, lo + child - 1)) < 0)) break;
        memcpy(AT(s, lo + i - 1), AT(s, lo + child - 1), (size_t)s->elem);
        i = child;
    }
    memcpy(AT(s, lo + i - 1), s->tmp2, (size_t)s->elem);
}
static void heap_sort(Sorter* s, int64_t lo, int64_t n) {
    for (int64_t i = n >> 1; i >= 1; i--) down_heap(s, lo, i, n);
    for (int64_t i = n; i > 1; i--) { sw_swap(s, lo, lo + i - 1); down_heap(s, lo, 1, i - 1); }
}
static int64_t pick_pivot_and_partition(Sorter* s, int64_t lo, int64_t n) {
    int64_t hi = n - 1, middle = hi >> 1;
    swap_if_greater(s, lo, lo + middle);
    swap_if_greater(s, lo, lo + hi);
    swap_if_greater(s, lo + middle, lo + hi);
    uint8_t* pivot = malloc((size_t)s->elem);
    memcpy(pivot, AT(s, lo + middle), (size_t)s->elem);
    sw_swap(s, lo + middle, lo + hi - 1);
    int64_t left = 0, right = hi - 1;
    while (left < right) {
        while (s->cmp(s->ctx, AT(s, lo + ++left), pivot) < 0) {}
        while (s->cmp(s->ctx, pivot, AT(s, lo + --right)) < 0) {}
        if (left >= right) break;
        sw_swap(s, lo + left, lo + right);
    }
    if (left != hi - 1) sw_swap(s, lo + left, lo + hi - 1);
    free(pivot);
    return left;
}
static void intro_sort(Sorter* s, int64_t lo, int64_t n, int depth) {
    while (n > 1) {
        if (n <= 16) {
            if (n == 2) { swap_if_greater(s, lo, lo + 1); return; }
            if (n == 3) { swap_if_greater(s, lo, lo + 1); swap_if_greater(s, lo, lo + 2); swap_if_greater(s, lo + 1, lo + 2); return; }
            insertion_sort(s, lo, n);
            return;
        }
        if (depth == 0) { heap_sort(s, lo, n); return; }
        depth--;
        int64_t p = pick_pivot_and_partition(s, lo, n);
        intro_sort(s, lo + p + 1, n - (p + 1), depth);
        n = p;
    }
}

void csrt_array_introsort(csrt_Array* a, int64_t index, int64_t length, int64_t elem, CtxCmp cmp, void* ctx) {
    need_array(a, "array");
    if (length < 0) { index = 0; length = a->length; }
    if (index < 0 || index + length > a->length)
        csrt_throw(&mt_argument, ascii("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection."));
    if (length < 2) return;
    Sorter s = { a->data, elem, cmp, ctx, malloc((size_t)elem), malloc((size_t)elem) };
    int depth = 2 * ((63 - __builtin_clzll((uint64_t)length)) + 1);
    intro_sort(&s, index, length, depth);
    free(s.tmp);
    free(s.tmp2);
}

static int32_t kind_cmp(void* ctx, const void* a, const void* b) { return ((CmpFn)ctx)(a, b); }
