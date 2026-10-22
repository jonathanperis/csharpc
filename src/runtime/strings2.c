// csrt strings2: more System.String / Char / Math / BitConverter members (conformance2 corpus).
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <math.h>
#include <stdio.h>
#include <string.h>

static csrt_String* from_chars2(const uint16_t* p, int32_t n) {
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)n));
    s->length = n;
    if (n) memcpy(s->chars, p, 2 * (size_t)n);
    return s;
}

static bool in_set(uint16_t c, const uint16_t* set, int32_t n) {
    if (n == 0) return c == ' ' || (c >= 9 && c <= 13) || c == 0x85 || c == 0xA0 || c == 0x1680 || (c >= 0x2000 && c <= 0x200A) || c == 0x2028 || c == 0x2029 || c == 0x202F || c == 0x205F || c == 0x3000;
    for (int32_t i = 0; i < n; i++) if (set[i] == c) return true;
    return false;
}

/// Trim / TrimStart / TrimEnd with an explicit character set (n == 0 means white space). mode: 1 start, 2 end, 3 both.
csrt_String* csrt_string_trim_set(csrt_String* s, const uint16_t* set, int32_t n, int32_t mode) {
    if (!s) csrt_throw_null_reference();
    int32_t a = 0, b = s->length;
    if (mode & 1) while (a < b && in_set(s->chars[a], set, n)) a++;
    if (mode & 2) while (b > a && in_set(s->chars[b - 1], set, n)) b--;
    if (a == 0 && b == s->length) return s;
    return from_chars2(s->chars + a, b - a);
}

/// Split(char separator, int count, StringSplitOptions): at most `count` pieces; the last one keeps the rest.
csrt_Array* csrt_string_split_char_count(csrt_String* s, uint16_t sep, int32_t count, int32_t options) {
    if (!s) csrt_throw_null_reference();
    if (count < 0) {
        static const csrt_MethodTable mt = { NULL, "System.ArgumentOutOfRangeException", 0, 0 };
        csrt_throw(&mt, from_chars2((const uint16_t*)u"Count cannot be less than zero. (Parameter 'count')", 51));
    }
    csrt_String* pieces[4096];
    int32_t n = 0, start = 0;
    bool remove = options & 1, trim = options & 2;
    if (count == 0) return csrt_new_array(sizeof(void*), 0);
    for (int32_t i = 0; i <= s->length && n < 4096; i++) {
        bool at_end = i == s->length;
        if (!at_end && s->chars[i] != sep) continue;
        if (!at_end && n == count - 1) continue;  // last allowed piece swallows the remainder
        int32_t a = start, b = i;
        if (trim) { while (a < b && in_set(s->chars[a], NULL, 0)) a++; while (b > a && in_set(s->chars[b - 1], NULL, 0)) b--; }
        if (!(remove && a == b)) pieces[n++] = from_chars2(s->chars + a, b - a);
        start = i + 1;
    }
    csrt_Array* r = csrt_new_array(sizeof(void*), n);
    memcpy(r->data, pieces, sizeof(void*) * (size_t)n);
    return r;
}

static uint16_t fold(uint16_t c) { return (c >= 'a' && c <= 'z') ? (uint16_t)(c - 32) : (c >= 0xE0 && c <= 0xFE && c != 0xF7) ? (uint16_t)(c - 32) : c; }

/// IndexOf(value, startIndex, comparison): ordinal; the *IgnoreCase comparisons (1, 3, 5) fold case.
int32_t csrt_string_index_of_cmp(csrt_String* s, csrt_String* value, int32_t start, int32_t comparison) {
    if (!s) csrt_throw_null_reference();
    bool ic = comparison & 1;
    for (int32_t i = start; i + value->length <= s->length; i++) {
        int32_t k = 0;
        for (; k < value->length; k++) {
            uint16_t x = s->chars[i + k], y = value->chars[k];
            if (ic ? fold(x) != fold(y) : x != y) break;
        }
        if (k == value->length) return i;
    }
    return -1;
}

int32_t csrt_string_last_index_of_cmp(csrt_String* s, csrt_String* value, int32_t comparison) {
    if (!s) csrt_throw_null_reference();
    bool ic = comparison & 1;
    for (int32_t i = s->length - value->length; i >= 0; i--) {
        int32_t k = 0;
        for (; k < value->length; k++) {
            uint16_t x = s->chars[i + k], y = value->chars[k];
            if (ic ? fold(x) != fold(y) : x != y) break;
        }
        if (k == value->length) return i;
    }
    return -1;
}

bool csrt_string_starts_with_cmp(csrt_String* s, csrt_String* v, int32_t comparison) {
    if (!s) csrt_throw_null_reference();
    if (v->length > s->length) return false;
    bool ic = comparison & 1;
    for (int32_t k = 0; k < v->length; k++) if (ic ? fold(s->chars[k]) != fold(v->chars[k]) : s->chars[k] != v->chars[k]) return false;
    return true;
}

bool csrt_string_ends_with_cmp(csrt_String* s, csrt_String* v, int32_t comparison) {
    if (!s) csrt_throw_null_reference();
    if (v->length > s->length) return false;
    int32_t off = s->length - v->length;
    bool ic = comparison & 1;
    for (int32_t k = 0; k < v->length; k++) if (ic ? fold(s->chars[off + k]) != fold(v->chars[k]) : s->chars[off + k] != v->chars[k]) return false;
    return true;
}

// ---- char ------------------------------------------------------------------------------------
bool csrt_char_is_surrogate(uint16_t c) { return c >= 0xD800 && c <= 0xDFFF; }
bool csrt_char_is_high_surrogate(uint16_t c) { return c >= 0xD800 && c <= 0xDBFF; }
bool csrt_char_is_low_surrogate(uint16_t c) { return c >= 0xDC00 && c <= 0xDFFF; }
bool csrt_char_is_symbol(uint16_t c) {
    return c == '$' || c == '+' || c == '<' || c == '=' || c == '>' || c == '^' || c == '`' || c == '|' || c == '~' ||
           (c >= 0xA2 && c <= 0xA6) || c == 0xA8 || c == 0xA9 || c == 0xAC || (c >= 0xAE && c <= 0xB1) || c == 0xB4 ||
           c == 0xB8 || c == 0xD7 || c == 0xF7 || (c >= 0x2190 && c <= 0x23FF) || (c >= 0x20A0 && c <= 0x20C0) || (c >= 0x2200 && c <= 0x22FF);
}
bool csrt_char_is_control(uint16_t c) { return c < 0x20 || (c >= 0x7F && c <= 0x9F); }
bool csrt_char_is_separator(uint16_t c) { return c == ' ' || c == 0xA0 || c == 0x1680 || (c >= 0x2000 && c <= 0x200A) || c == 0x2028 || c == 0x2029 || c == 0x202F || c == 0x205F || c == 0x3000; }

// ---- Math / BitConverter ------------------------------------------------------------------------
double csrt_math_ieee_remainder(double x, double y) { return remainder(x, y); }

bool csrt_string_equals(csrt_String* a, csrt_String* b);

/// string.Equals(object): only another string can be equal.
bool csrt_string_equals_object(csrt_String* s, void* o) {
    if (!s) csrt_throw_null_reference();
    if (!o || ((csrt_Object*)o)->mt != &csrt_String_MT.header) return false;
    return csrt_string_equals(s, (csrt_String*)o);
}

/// double/float GetHashCode, as .NET: ±0 hash alike, all NaNs hash alike.
int32_t csrt_hash_double(double v) {
    int64_t bits; memcpy(&bits, &v, 8);
    if (v == 0 || v != v) bits &= 0x7FF0000000000000LL;
    return (int32_t)bits ^ (int32_t)(bits >> 32);
}
int32_t csrt_hash_float(float v) {
    int32_t bits; memcpy(&bits, &v, 4);
    if (v == 0 || v != v) bits &= 0x7F800000;
    return bits;
}

// ---- Environment.ExitCode ----------------------------------------------------------------------
static int32_t s_exit_code;
void csrt_environment_set_exit_code(int32_t code) { s_exit_code = code; }
int32_t csrt_environment_get_exit_code(void) { return s_exit_code; }

/// object[] { a, b, c } with the first n used (AppendFormat and friends).
csrt_Array* csrt_object_array3(int32_t n, void* a, void* b, void* c) {
    csrt_Array* r = csrt_new_array(sizeof(void*), n);
    void* items[3] = { a, b, c };
    memcpy(r->data, items, sizeof(void*) * (size_t)n);
    return r;
}

csrt_String* csrt_string_trim_set_array(csrt_String* s, csrt_Array* chars, int32_t mode) {
    return csrt_string_trim_set(s, chars ? (const uint16_t*)chars->data : NULL, chars ? (int32_t)chars->length : 0, mode);
}
int32_t csrt_string_length_checked(csrt_String* s) { if (!s) csrt_throw_null_reference(); return s->length; }
uint16_t csrt_string_char_at_unchecked(csrt_String* s, int32_t i) { return i < s->length ? s->chars[i] : 0; }

// ---- interface implementations for boxed primitives (registered with their slot tables) ---------
csrt_String* csrt_format_object(void* obj, csrt_String* fmt);
static const csrt_MethodTable s_argument_mt = { NULL, "System.ArgumentException", 0, 0 };

#define PAYLOAD(o) ((const char*)(o) + 8)
#define CMP(a, b) ((a) < (b) ? -1 : (a) > (b) ? 1 : 0)

/// IComparable.CompareTo(object) on a boxed primitive: null sorts first; other types throw like .NET.
int32_t csrt_boxed_compare_object(void* self, void* other) {
    if (!other) return 1;
    const csrt_MethodTable* mt = ((csrt_Object*)self)->mt;
    if (((csrt_Object*)other)->mt != mt) {
        char msg[96];
        const char* name = mt->name; const char* dot = strrchr(name, '.');
        snprintf(msg, sizeof msg, "Object must be of type %s.", dot ? dot + 1 : name);
        csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * strlen(msg)));
        s->length = (int32_t)strlen(msg);
        for (int32_t i = 0; i < s->length; i++) s->chars[i] = (uint8_t)msg[i];
        csrt_throw(&s_argument_mt, s);
    }
    const char *a = PAYLOAD(self), *b = PAYLOAD(other);
    if (mt == &csrt_Boxed_Int32_MT.header) return CMP(*(const int32_t*)a, *(const int32_t*)b);
    if (mt == &csrt_Boxed_Int64_MT.header) return CMP(*(const int64_t*)a, *(const int64_t*)b);
    if (mt == &csrt_Boxed_UInt32_MT.header) return CMP(*(const uint32_t*)a, *(const uint32_t*)b);
    if (mt == &csrt_Boxed_UInt64_MT.header) return CMP(*(const uint64_t*)a, *(const uint64_t*)b);
    if (mt == &csrt_Boxed_Int16_MT.header) return CMP(*(const int16_t*)a, *(const int16_t*)b);
    if (mt == &csrt_Boxed_UInt16_MT.header || mt == &csrt_Boxed_Char_MT.header) return CMP(*(const uint16_t*)a, *(const uint16_t*)b);
    if (mt == &csrt_Boxed_Byte_MT.header || mt == &csrt_Boxed_Boolean_MT.header) return CMP(*(const uint8_t*)a, *(const uint8_t*)b);
    if (mt == &csrt_Boxed_SByte_MT.header) return CMP(*(const int8_t*)a, *(const int8_t*)b);
    if (mt == &csrt_Boxed_Double_MT.header) {
        double x = *(const double*)a, y = *(const double*)b;
        if (x != x) return y != y ? 0 : -1;  // NaN sorts first, as in .NET
        if (y != y) return 1;
        return CMP(x, y);
    }
    if (mt == &csrt_Boxed_Single_MT.header) {
        float x = *(const float*)a, y = *(const float*)b;
        if (x != x) return y != y ? 0 : -1;
        if (y != y) return 1;
        return CMP(x, y);
    }
    return 0;
}

/// IFormattable.ToString(format, provider) on a boxed primitive (invariant culture).
csrt_String* csrt_boxed_format(void* self, csrt_String* format, void* provider) {
    (void)provider;
    return csrt_format_object(self, format);
}
