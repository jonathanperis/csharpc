// csrt strings: the System.String / System.Char / System.Text.StringBuilder / number-parsing
// surface csharpc maps BCL calls onto (see src/Csharpc/CodeGen/Intrinsics.cs).
//
// Semantics follow .NET with InvariantGlobalization: ordinal comparisons and invariant casing
// (ASCII + Latin-1 here; full Unicode casing tables are future work).
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <errno.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// ---------------------------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------------------------
static csrt_String* new_string(int32_t length) {
    // Not zeroed: every caller writes all `length` chars (and the GC never scans strings).
    csrt_String* s = csrt_alloc_noclear(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + sizeof(uint16_t) * (size_t)length));
    s->length = length;
    return s;
}

static csrt_String* from_chars(const uint16_t* chars, int32_t length) {
    csrt_String* s = new_string(length);
    if (length) memcpy(s->chars, chars, sizeof(uint16_t) * (size_t)length);
    return s;
}

static const csrt_MethodTable mt_argument_out_of_range = { NULL, "System.ArgumentOutOfRangeException", 0, 0 };
static const csrt_MethodTable mt_argument_null = { NULL, "System.ArgumentNullException", 0, 0 };
static const csrt_MethodTable mt_argument = { NULL, "System.ArgumentException", 0, 0 };
static const csrt_MethodTable mt_format = { NULL, "System.FormatException", 0, 0 };
static const csrt_MethodTable mt_overflow = { NULL, "System.OverflowException", 0, 0 };

static csrt_String* ascii(const char* text) {
    int32_t n = (int32_t)strlen(text);
    csrt_String* s = new_string(n);
    for (int32_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    return s;
}

_Noreturn static void out_of_range(const char* message) { csrt_throw(&mt_argument_out_of_range, ascii(message)); }

static void require(const csrt_String* s) { if (!s) csrt_throw_null_reference(); }

static bool is_white_space(uint16_t c) {
    return c == ' ' || (c >= 0x09 && c <= 0x0D) || c == 0x85 || c == 0xA0 || c == 0x1680 ||
           (c >= 0x2000 && c <= 0x200A) || c == 0x2028 || c == 0x2029 || c == 0x202F || c == 0x205F || c == 0x3000;
}

// Invariant simple case mapping: Latin-1, Latin Extended-A, Greek, Cyrillic, Armenian.
static uint16_t to_upper(uint16_t c) {
    if ((c >= 'a' && c <= 'z') || (c >= 0xE0 && c <= 0xFE && c != 0xF7)) return (uint16_t)(c - 32);
    if (c == 0xFF) return 0x178;
    if (c < 0x100) return c;
    if (c >= 0x100 && c <= 0x17F) {
        if (c == 0x131) return 'I';
        if (c == 0x17F) return 'S';
        if ((c >= 0x139 && c <= 0x148) || (c >= 0x179 && c <= 0x17E)) return (c & 1) ? c : (uint16_t)(c - 1);
        if (c == 0x138 || c == 0x149 || c == 0x130) return c;
        return (c & 1) ? (uint16_t)(c - 1) : c;
    }
    if (c >= 0x3B1 && c <= 0x3C9 && c != 0x3C2) return (uint16_t)(c - 0x20);
    if (c == 0x3C2) return 0x3A3;
    if (c >= 0x3AC && c <= 0x3AF) return c == 0x3AC ? 0x386 : (uint16_t)(c - 0x25);
    if (c >= 0x430 && c <= 0x44F) return (uint16_t)(c - 0x20);
    if (c >= 0x450 && c <= 0x45F) return (uint16_t)(c - 0x50);
    if (c >= 0x561 && c <= 0x586) return (uint16_t)(c - 0x30);
    return c;
}

static uint16_t to_lower(uint16_t c) {
    if ((c >= 'A' && c <= 'Z') || (c >= 0xC0 && c <= 0xDE && c != 0xD7)) return (uint16_t)(c + 32);
    if (c == 0x178) return 0xFF;
    if (c < 0x100) return c;
    if (c >= 0x100 && c <= 0x17F) {
        if (c == 0x130) return 'i';
        if ((c >= 0x139 && c <= 0x148) || (c >= 0x179 && c <= 0x17E)) return (c & 1) ? (uint16_t)(c + 1) : c;
        if (c == 0x138 || c == 0x149 || c == 0x131 || c == 0x17F) return c;
        return (c & 1) ? c : (uint16_t)(c + 1);
    }
    if (c >= 0x391 && c <= 0x3A9 && c != 0x3A2) return (uint16_t)(c + 0x20);
    if (c == 0x386) return 0x3AC;
    if (c >= 0x388 && c <= 0x38A) return (uint16_t)(c + 0x25);
    if (c >= 0x410 && c <= 0x42F) return (uint16_t)(c + 0x20);
    if (c >= 0x400 && c <= 0x40F) return (uint16_t)(c + 0x50);
    if (c >= 0x531 && c <= 0x556) return (uint16_t)(c + 0x30);
    return c;
}

// Unicode decimal digits (Nd) outside ASCII: zero code points of the common scripts.
static int unicode_digit_value(uint16_t c) {
    static const uint16_t zeros[] = { 0x660, 0x6F0, 0x7C0, 0x966, 0x9E6, 0xA66, 0xAE6, 0xB66, 0xBE6, 0xC66, 0xCE6, 0xD66,
                                      0xDE6, 0xE50, 0xED0, 0xF20, 0x1040, 0x1090, 0x17E0, 0x1810, 0x1946, 0x19D0, 0xFF10 };
    for (size_t i = 0; i < sizeof zeros / sizeof *zeros; i++)
        if (c >= zeros[i] && c <= zeros[i] + 9) return c - zeros[i];
    return -1;
}

// ---------------------------------------------------------------------------------------------
// System.Char
// ---------------------------------------------------------------------------------------------
// Latin-1 character classes (generated from Unicode categories, as .NET's Latin-1 fast path):
// 1 = letter (L*), 2 = decimal digit (Nd), 4 = white space, 8 = upper (Lu), 16 = lower (Ll), 32 = punctuation (P*)
enum { CC_LETTER = 1, CC_DIGIT = 2, CC_SPACE = 4, CC_UPPER = 8, CC_LOWER = 16, CC_PUNCT = 32 };
static const uint8_t s_latin1[256] = {
    0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 4, 4, 4, 4, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    4, 32, 32, 32, 0, 32, 32, 32, 32, 32, 32, 0, 32, 32, 32, 32,
    2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 32, 32, 0, 0, 0, 32,
    32, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
    9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 32, 32, 32, 0, 32,
    0, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17,
    17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 32, 0, 32, 0, 0,
    0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    4, 32, 0, 0, 0, 0, 0, 32, 0, 0, 1, 32, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 17, 32, 32, 0, 0, 1, 32, 0, 0, 0, 32,
    9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
    9, 9, 9, 9, 9, 9, 9, 0, 9, 9, 9, 9, 9, 9, 9, 17,
    17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17,
    17, 17, 17, 17, 17, 17, 17, 0, 17, 17, 17, 17, 17, 17, 17, 17
};

static bool is_letter_slow(uint16_t c) {
    return (c >= 0x100 && c <= 0x24F) || (c >= 0x370 && c <= 0x3FF && c != 0x37E && c != 0x387) || (c >= 0x400 && c <= 0x4FF) ||
           (c >= 0x3040 && c <= 0x30FF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xAC00 && c <= 0xD7A3);
}

bool csrt_char_is_digit(uint16_t c) { return c < 256 ? (s_latin1[c] & CC_DIGIT) != 0 : unicode_digit_value(c) >= 0; }
bool csrt_char_is_upper(uint16_t c) { return c < 256 ? (s_latin1[c] & CC_UPPER) != 0 : to_lower(c) != c; }
bool csrt_char_is_lower(uint16_t c) { return c < 256 ? (s_latin1[c] & CC_LOWER) != 0 : to_upper(c) != c; }
bool csrt_char_is_letter(uint16_t c) { return c < 256 ? (s_latin1[c] & CC_LETTER) != 0 : is_letter_slow(c); }
bool csrt_char_is_letter_or_digit(uint16_t c) { return c < 256 ? (s_latin1[c] & (CC_LETTER | CC_DIGIT)) != 0 : is_letter_slow(c) || unicode_digit_value(c) >= 0; }
bool csrt_char_is_white_space(uint16_t c) { return c < 256 ? (s_latin1[c] & CC_SPACE) != 0 : is_white_space(c); }
bool csrt_char_is_punctuation(uint16_t c) { return c < 256 ? (s_latin1[c] & CC_PUNCT) != 0 : false; }
uint16_t csrt_char_to_upper(uint16_t c) { return to_upper(c); }
uint16_t csrt_char_to_lower(uint16_t c) { return to_lower(c); }

// ---------------------------------------------------------------------------------------------
// System.String
// ---------------------------------------------------------------------------------------------
csrt_String* csrt_string_new_repeat(uint16_t c, int32_t count) {
    if (count < 0) out_of_range("Non-negative number required. (Parameter 'count')");
    csrt_String* s = new_string(count);
    for (int32_t i = 0; i < count; i++) s->chars[i] = c;
    return s;
}

csrt_String* csrt_string_new_from_chars(csrt_Array* chars) {
    if (!chars) return new_string(0);
    return from_chars((const uint16_t*)chars->data, (int32_t)chars->length);
}

csrt_Array* csrt_string_to_char_array(csrt_String* s) {
    require(s);
    csrt_Array* a = csrt_new_array_primitive(sizeof(uint16_t), s->length);
    memcpy(a->data, s->chars, sizeof(uint16_t) * (size_t)s->length);
    return a;
}

csrt_String* csrt_string_substring(csrt_String* s, int32_t start, int32_t length) {
    require(s);
    if (start < 0 || start > s->length) out_of_range("startIndex cannot be larger than length of string. (Parameter 'startIndex')");
    if (length < 0 || start > s->length - length) out_of_range("Index and length must refer to a location within the string. (Parameter 'length')");
    return from_chars(s->chars + start, length);
}

/// string.Insert(startIndex, value)
csrt_String* csrt_string_insert(csrt_String* s, int32_t start, csrt_String* value) {
    require(s);
    if (!value) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 'value')"));
    if (start < 0 || start > s->length) out_of_range("Index was out of range. Must be non-negative and less than or equal to the size of the collection. (Parameter 'startIndex')");
    csrt_String* r = new_string(s->length + value->length);
    memcpy(r->chars, s->chars, sizeof(uint16_t) * (size_t)start);
    memcpy(r->chars + start, value->chars, sizeof(uint16_t) * (size_t)value->length);
    memcpy(r->chars + start + value->length, s->chars + start, sizeof(uint16_t) * (size_t)(s->length - start));
    return r;
}

/// string.Remove(start[, count]); count < 0 means "to the end" (the one-argument overload).
csrt_String* csrt_string_remove(csrt_String* s, int32_t start, int32_t count) {
    require(s);
    if (count < 0) {
        if (start < 0) out_of_range("StartIndex cannot be less than zero. (Parameter 'startIndex')");
        if (start >= s->length) out_of_range("startIndex must be less than length of string. (Parameter 'startIndex')");
        return from_chars(s->chars, start);
    }
    if (start < 0) out_of_range("StartIndex cannot be less than zero. (Parameter 'startIndex')");
    if (start > s->length - count) out_of_range("Index and count must refer to a location within the string. (Parameter 'count')");
    csrt_String* r = new_string(s->length - count);
    memcpy(r->chars, s->chars, sizeof(uint16_t) * (size_t)start);
    memcpy(r->chars + start, s->chars + start + count, sizeof(uint16_t) * (size_t)(s->length - start - count));
    return r;
}

csrt_String* csrt_string_substring_from(csrt_String* s, int32_t start) {
    require(s);
    return csrt_string_substring(s, start, s->length - start);
}

int32_t csrt_string_index_of_char(csrt_String* s, uint16_t c, int32_t start) {
    require(s);
    for (int32_t i = start; i < s->length; i++) if (s->chars[i] == c) return i;
    return -1;
}

int32_t csrt_string_last_index_of_char(csrt_String* s, uint16_t c) {
    require(s);
    for (int32_t i = s->length - 1; i >= 0; i--) if (s->chars[i] == c) return i;
    return -1;
}

int32_t csrt_string_index_of(csrt_String* s, csrt_String* value, int32_t start) {
    require(s);
    if (!value) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 'value')"));
    if (value->length == 0) return start <= s->length ? start : -1;
    // Scan for the first char, then compare the rest.
    const uint16_t first = value->chars[0];
    const int32_t rest = value->length - 1;
    const int32_t last = s->length - value->length;
    for (int32_t i = start; i <= last; i++) {
        if (s->chars[i] != first) continue;
        int32_t k = 0;
        while (k < rest && s->chars[i + 1 + k] == value->chars[1 + k]) k++;
        if (k == rest) return i;
    }
    return -1;
}

bool csrt_string_contains(csrt_String* s, csrt_String* value) { return csrt_string_index_of(s, value, 0) >= 0; }
bool csrt_string_contains_char(csrt_String* s, uint16_t c) { return csrt_string_index_of_char(s, c, 0) >= 0; }

bool csrt_string_starts_with(csrt_String* s, csrt_String* value) {
    require(s);
    if (!value) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 'value')"));
    return value->length <= s->length && memcmp(s->chars, value->chars, sizeof(uint16_t) * (size_t)value->length) == 0;
}

bool csrt_string_ends_with(csrt_String* s, csrt_String* value) {
    require(s);
    if (!value) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 'value')"));
    return value->length <= s->length &&
           memcmp(s->chars + s->length - value->length, value->chars, sizeof(uint16_t) * (size_t)value->length) == 0;
}

csrt_String* csrt_string_to_upper(csrt_String* s) {
    require(s);
    csrt_String* r = new_string(s->length);
    for (int32_t i = 0; i < s->length; i++) r->chars[i] = to_upper(s->chars[i]);
    return r;
}

csrt_String* csrt_string_to_lower(csrt_String* s) {
    require(s);
    csrt_String* r = new_string(s->length);
    for (int32_t i = 0; i < s->length; i++) r->chars[i] = to_lower(s->chars[i]);
    return r;
}

// mode: 1 = start, 2 = end, 3 = both
csrt_String* csrt_string_trim(csrt_String* s, int32_t mode) {
    require(s);
    int32_t start = 0, end = s->length;
    if (mode & 1) while (start < end && is_white_space(s->chars[start])) start++;
    if (mode & 2) while (end > start && is_white_space(s->chars[end - 1])) end--;
    if (start == 0 && end == s->length) return s;
    return from_chars(s->chars + start, end - start);
}

csrt_String* csrt_string_replace_char(csrt_String* s, uint16_t from, uint16_t to) {
    require(s);
    csrt_String* r = from_chars(s->chars, s->length);
    for (int32_t i = 0; i < r->length; i++) if (r->chars[i] == from) r->chars[i] = to;
    return r;
}

csrt_String* csrt_string_replace(csrt_String* s, csrt_String* from, csrt_String* to) {
    require(s);
    if (!from) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 'oldValue')"));
    if (from->length == 0) csrt_throw(&mt_argument, ascii("String cannot be of zero length. (Parameter 'oldValue')"));
    int32_t to_len = to ? to->length : 0;
    int32_t count = 0;
    for (int32_t i = csrt_string_index_of(s, from, 0); i >= 0; i = csrt_string_index_of(s, from, i + from->length)) count++;
    if (count == 0) return s;
    csrt_String* r = new_string(s->length + count * (to_len - from->length));
    int32_t src = 0, dst = 0;
    for (int32_t i = csrt_string_index_of(s, from, 0); i >= 0; i = csrt_string_index_of(s, from, src)) {
        memcpy(r->chars + dst, s->chars + src, sizeof(uint16_t) * (size_t)(i - src));
        dst += i - src;
        if (to_len) memcpy(r->chars + dst, to->chars, sizeof(uint16_t) * (size_t)to_len);
        dst += to_len;
        src = i + from->length;
    }
    memcpy(r->chars + dst, s->chars + src, sizeof(uint16_t) * (size_t)(s->length - src));
    return r;
}

csrt_String* csrt_string_pad(csrt_String* s, int32_t width, uint16_t c, bool left) {
    require(s);
    if (width < 0) out_of_range("Non-negative number required. (Parameter 'totalWidth')");
    if (width <= s->length) return s;
    csrt_String* r = new_string(width);
    int32_t pad = width - s->length;
    for (int32_t i = 0; i < pad; i++) r->chars[left ? i : s->length + i] = c;
    memcpy(r->chars + (left ? pad : 0), s->chars, sizeof(uint16_t) * (size_t)s->length);
    return r;
}

// StringSplitOptions: RemoveEmptyEntries = 1, TrimEntries = 2
csrt_Array* csrt_string_split_char(csrt_String* s, uint16_t sep, int32_t options) {
    require(s);
    int32_t pieces = 1;
    for (int32_t i = 0; i < s->length; i++) if (s->chars[i] == sep) pieces++;
    csrt_String** tmp = malloc(sizeof(csrt_String*) * (size_t)pieces);
    int32_t n = 0, start = 0;
    for (int32_t i = 0; i <= s->length; i++) {
        if (i < s->length && s->chars[i] != sep) continue;
        int32_t a = start, b = i;
        if (options & 2) {
            while (a < b && is_white_space(s->chars[a])) a++;
            while (b > a && is_white_space(s->chars[b - 1])) b--;
        }
        if (!((options & 1) && a == b)) tmp[n++] = from_chars(s->chars + a, b - a);
        start = i + 1;
    }
    // Allocate the result after the pieces: tmp is malloc'd (not scanned by the GC), so keep the
    // pieces reachable from the stack via this loop's locals until they're stored in the array.
    csrt_Array* result = csrt_new_array(sizeof(void*), n);
    memcpy(result->data, tmp, sizeof(void*) * (size_t)n);
    free(tmp);
    return result;
}

csrt_String* csrt_string_join(csrt_String* separator, csrt_Array* values) {
    if (!values) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 'value')"));
    csrt_String** items = (csrt_String**)values->data;
    int32_t sep_len = separator ? separator->length : 0;
    int64_t total = 0;
    for (int64_t i = 0; i < values->length; i++) total += (items[i] ? items[i]->length : 0) + (i ? sep_len : 0);
    csrt_String* r = new_string((int32_t)total);
    int32_t at = 0;
    for (int64_t i = 0; i < values->length; i++) {
        if (i && sep_len) { memcpy(r->chars + at, separator->chars, sizeof(uint16_t) * (size_t)sep_len); at += sep_len; }
        if (items[i]) { memcpy(r->chars + at, items[i]->chars, sizeof(uint16_t) * (size_t)items[i]->length); at += items[i]->length; }
    }
    return r;
}

int32_t csrt_string_compare_ordinal(csrt_String* a, csrt_String* b) {
    if (a == b) return 0;
    if (!a) return -1;
    if (!b) return 1;
    int32_t n = a->length < b->length ? a->length : b->length;
    for (int32_t i = 0; i < n; i++)
        if (a->chars[i] != b->chars[i]) return (int32_t)a->chars[i] - (int32_t)b->chars[i];
    return a->length - b->length;
}

int32_t csrt_string_get_hash_code(csrt_String* s) {
    require(s);
    uint32_t h = 2166136261u;  // FNV-1a (stable per process; .NET randomizes, so values differ by design)
    for (int32_t i = 0; i < s->length; i++) { h ^= s->chars[i]; h *= 16777619u; }
    return (int32_t)h;
}

// ---------------------------------------------------------------------------------------------
// Parsing (invariant culture)
// ---------------------------------------------------------------------------------------------
static bool to_ascii(csrt_String* s, char* buf, size_t cap) {
    if (!s || (size_t)s->length >= cap) return false;
    for (int32_t i = 0; i < s->length; i++) {
        if (s->chars[i] > 127) return false;
        buf[i] = (char)s->chars[i];
    }
    buf[s->length] = 0;
    return true;
}

static bool parse_i64(csrt_String* s, int64_t min, int64_t max, int64_t* out, bool* overflow) {
    char buf[64];
    *overflow = false;
    if (!to_ascii(s, buf, sizeof buf)) return false;
    char* p = buf;
    while (*p == ' ' || (*p >= 9 && *p <= 13)) p++;
    if (!*p) return false;
    char* end;
    errno = 0;
    long long v = strtoll(p, &end, 10);
    if (end == p) return false;
    while (*end == ' ' || (*end >= 9 && *end <= 13)) end++;
    if (*end) return false;
    if (errno == ERANGE || v < min || v > max) { *overflow = true; return false; }
    *out = v;
    return true;
}

int32_t csrt_int32_parse(csrt_String* s) {
    if (!s) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 's')"));
    int64_t v; bool overflow;
    if (parse_i64(s, INT32_MIN, INT32_MAX, &v, &overflow)) return (int32_t)v;
    if (overflow) csrt_throw(&mt_overflow, ascii("Value was either too large or too small for an Int32."));
    csrt_throw(&mt_format, csrt_string_concat(csrt_string_concat(ascii("The input string '"), s), ascii("' was not in a correct format.")));
}

int64_t csrt_int64_parse(csrt_String* s) {
    if (!s) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 's')"));
    int64_t v; bool overflow;
    if (parse_i64(s, INT64_MIN, INT64_MAX, &v, &overflow)) return v;
    if (overflow) csrt_throw(&mt_overflow, ascii("Value was either too large or too small for an Int64."));
    csrt_throw(&mt_format, csrt_string_concat(csrt_string_concat(ascii("The input string '"), s), ascii("' was not in a correct format.")));
}

bool csrt_int32_try_parse(csrt_String* s, int32_t* result) {
    int64_t v; bool overflow;
    bool ok = parse_i64(s, INT32_MIN, INT32_MAX, &v, &overflow);
    *result = ok ? (int32_t)v : 0;
    return ok;
}

bool csrt_int64_try_parse(csrt_String* s, int64_t* result) {
    int64_t v; bool overflow;
    bool ok = parse_i64(s, INT64_MIN, INT64_MAX, &v, &overflow);
    *result = ok ? v : 0;
    return ok;
}

static bool parse_f64(csrt_String* s, double* out) {
    char buf[128];
    if (!to_ascii(s, buf, sizeof buf)) return false;
    char* p = buf;
    while (*p == ' ') p++;
    if (!*p) return false;
    char* end;
    double v = strtod(p, &end);
    if (end == p) return false;
    while (*end == ' ') end++;
    if (*end) return false;
    *out = v;
    return true;
}

double csrt_double_parse(csrt_String* s) {
    if (!s) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 's')"));
    double v;
    if (parse_f64(s, &v)) return v;
    csrt_throw(&mt_format, csrt_string_concat(csrt_string_concat(ascii("The input string '"), s), ascii("' was not in a correct format.")));
}

bool csrt_double_try_parse(csrt_String* s, double* result) {
    double v = 0;
    bool ok = parse_f64(s, &v);
    *result = ok ? v : 0;
    return ok;
}

// ---------------------------------------------------------------------------------------------
// System.Text.StringBuilder: { mt, ptr buffer (a GC char[] array), i32 length }
// ---------------------------------------------------------------------------------------------
typedef struct {
    const csrt_MethodTable* mt;
    csrt_Array* buffer;
    int32_t length;
} StringBuilder;

csrt_String* csrt_sb_to_string(StringBuilder* sb);

const csrt_BuiltinMethodTable csrt_StringBuilder_MT = {
    { &csrt_Object_MT.header, "System.Text.StringBuilder", sizeof(StringBuilder), 0 },
    { (void*)csrt_sb_to_string, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

StringBuilder* csrt_sb_new(int32_t capacity) {
    StringBuilder* sb = csrt_alloc(&csrt_StringBuilder_MT.header, sizeof(StringBuilder));
    // Primitive + uncleared: the GC never scans it, and chars past `length` are never read.
    sb->buffer = csrt_new_array_primitive_noclear(sizeof(uint16_t), capacity > 16 ? capacity : 16);
    return sb;
}

static void sb_reserve(StringBuilder* sb, int64_t extra) {
    if (!sb) csrt_throw_null_reference();
    int64_t need = sb->length + extra;
    if (need <= sb->buffer->length) return;
    int64_t cap = sb->buffer->length * 2;
    if (cap < need) cap = need;
    csrt_Array* grown = csrt_new_array_primitive_noclear(sizeof(uint16_t), cap);
    memcpy(grown->data, sb->buffer->data, sizeof(uint16_t) * (size_t)sb->length);
    sb->buffer = grown;
}

StringBuilder* csrt_sb_append_string(StringBuilder* sb, csrt_String* s) {
    if (!s) { sb_reserve(sb, 0); return sb; }
    sb_reserve(sb, s->length);
    memcpy((uint16_t*)sb->buffer->data + sb->length, s->chars, sizeof(uint16_t) * (size_t)s->length);
    sb->length += s->length;
    return sb;
}

StringBuilder* csrt_sb_append_char(StringBuilder* sb, uint16_t c, int32_t repeat) {
    if (repeat < 0) out_of_range("Count cannot be less than zero. (Parameter 'repeatCount')");
    sb_reserve(sb, repeat);
    for (int32_t i = 0; i < repeat; i++) ((uint16_t*)sb->buffer->data)[sb->length++] = c;
    return sb;
}

static StringBuilder* sb_append_ascii(StringBuilder* sb, const char* begin, const char* end) {
    int32_t n = (int32_t)(end - begin);
    sb_reserve(sb, n);
    uint16_t* dst = (uint16_t*)sb->buffer->data + sb->length;
    for (int32_t i = 0; i < n; i++) dst[i] = (uint8_t)begin[i];
    sb->length += n;
    return sb;
}

// Append(int/long/uint/ulong/bool) format straight into the buffer — no temporary string.
StringBuilder* csrt_sb_append_i64(StringBuilder* sb, int64_t v) { char b[24]; return sb_append_ascii(sb, csrt_itoa_i64(v, b + 24), b + 24); }
StringBuilder* csrt_sb_append_u64(StringBuilder* sb, uint64_t v) { char b[24]; return sb_append_ascii(sb, csrt_itoa_u64(v, b + 24), b + 24); }
StringBuilder* csrt_sb_append_bool(StringBuilder* sb, bool v) {
    static const char t[] = "True", f[] = "False";
    return v ? sb_append_ascii(sb, t, t + 4) : sb_append_ascii(sb, f, f + 5);
}

StringBuilder* csrt_sb_new_from_string(csrt_String* s) {
    StringBuilder* sb = csrt_sb_new(s ? s->length : 16);
    return csrt_sb_append_string(sb, s);
}

csrt_String* csrt_sb_to_string(StringBuilder* sb) {
    if (!sb) csrt_throw_null_reference();
    return from_chars((const uint16_t*)sb->buffer->data, sb->length);
}

int32_t csrt_sb_length(StringBuilder* sb) {
    if (!sb) csrt_throw_null_reference();
    return sb->length;
}

StringBuilder* csrt_sb_clear(StringBuilder* sb) {
    if (!sb) csrt_throw_null_reference();
    sb->length = 0;
    return sb;
}

// ---------------------------------------------------------------------------------------------
// IndexOfAny / LastIndexOfAny / Trim(chars)
// ---------------------------------------------------------------------------------------------
static bool any_of(uint16_t c, csrt_Array* set) {
    const uint16_t* chars = (const uint16_t*)set->data;
    for (int64_t i = 0; i < set->length; i++) if (chars[i] == c) return true;
    return false;
}

int32_t csrt_string_index_of_any(csrt_String* s, csrt_Array* set, int32_t start) {
    require(s);
    if (!set) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 'anyOf')"));
    for (int32_t i = start; i < s->length; i++) if (any_of(s->chars[i], set)) return i;
    return -1;
}

int32_t csrt_string_last_index_of_any(csrt_String* s, csrt_Array* set) {
    require(s);
    if (!set) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 'anyOf')"));
    for (int32_t i = s->length - 1; i >= 0; i--) if (any_of(s->chars[i], set)) return i;
    return -1;
}

/// Trim(params char[]) / TrimStart / TrimEnd with a character set; null or empty set = whitespace.
csrt_String* csrt_string_trim_chars(csrt_String* s, csrt_Array* set, int32_t mode) {
    require(s);
    if (!set || set->length == 0) return csrt_string_trim(s, mode);
    int32_t start = 0, end = s->length;
    if (mode & 1) while (start < end && any_of(s->chars[start], set)) start++;
    if (mode & 2) while (end > start && any_of(s->chars[end - 1], set)) end--;
    if (start == 0 && end == s->length) return s;
    return from_chars(s->chars + start, end - start);
}

csrt_String* csrt_string_trim_char(csrt_String* s, uint16_t c, int32_t mode) {
    require(s);
    int32_t start = 0, end = s->length;
    if (mode & 1) while (start < end && s->chars[start] == c) start++;
    if (mode & 2) while (end > start && s->chars[end - 1] == c) end--;
    if (start == 0 && end == s->length) return s;
    return from_chars(s->chars + start, end - start);
}
