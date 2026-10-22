// csrt parse: NumberStyles-aware integer/float parsing (int.Parse(s, NumberStyles.HexNumber), thousands
// separators, ...) for the invariant culture.
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <stdlib.h>
#include <stdio.h>
#include <string.h>

enum {
    AllowLeadingWhite = 1, AllowTrailingWhite = 2, AllowLeadingSign = 4, AllowTrailingSign = 8,
    AllowParentheses = 16, AllowDecimalPoint = 32, AllowThousands = 64, AllowExponent = 128,
    AllowCurrencySymbol = 256, AllowHexSpecifier = 512,
};

/// Copies the string to ASCII, applying whitespace/thousands rules. Returns false on non-ASCII.
static bool prepare(csrt_String* s, int32_t styles, char* out, size_t cap) {
    if (!s) return false;
    int32_t start = 0, end = s->length;
    if (styles & AllowLeadingWhite) while (start < end && (s->chars[start] == ' ' || (s->chars[start] >= 9 && s->chars[start] <= 13))) start++;
    if (styles & AllowTrailingWhite) while (end > start && (s->chars[end - 1] == ' ' || (s->chars[end - 1] >= 9 && s->chars[end - 1] <= 13))) end--;
    size_t n = 0;
    for (int32_t i = start; i < end; i++) {
        uint16_t c = s->chars[i];
        if (c > 127) return false;
        if (c == ',' && (styles & AllowThousands)) continue;
        if (n + 1 >= cap) return false;
        out[n++] = (char)c;
    }
    out[n] = 0;
    return n > 0;
}

/// Returns 1 on success, 0 on format error, 2 on overflow. Hex parses as two's complement of `bits`.
int32_t csrt_parse_i64_styled(csrt_String* s, int32_t styles, int32_t bits, bool is_signed, int64_t* result) {
    char buf[128];
    *result = 0;
    if (!prepare(s, styles, buf, sizeof buf)) return 0;
    if (styles & AllowHexSpecifier) {
        uint64_t v = 0;
        for (char* p = buf; *p; p++) {
            int d = (*p >= '0' && *p <= '9') ? *p - '0' : (*p >= 'a' && *p <= 'f') ? *p - 'a' + 10 : (*p >= 'A' && *p <= 'F') ? *p - 'A' + 10 : -1;
            if (d < 0) return 0;
            if (v >> (bits - 4)) return 2;
            v = (v << 4) | (uint64_t)d;
        }
        // e.g. int.Parse("FFFFFFFF", HexNumber) == -1: reinterpret within the type's width
        if (is_signed && bits < 64 && (v >> (bits - 1)) & 1) *result = (int64_t)(v | (~0ULL << bits));
        else *result = (int64_t)v;
        return 1;
    }
    char* p = buf;
    bool negative = false;
    if ((styles & AllowLeadingSign) && (*p == '-' || *p == '+')) { negative = *p == '-'; p++; }
    if (!*p) return 0;
    uint64_t v = 0;
    for (; *p; p++) {
        if (*p < '0' || *p > '9') {
            // "123.000" is accepted with AllowDecimalPoint when the fraction is all zeros
            if (*p == '.' && (styles & AllowDecimalPoint)) { for (p++; *p; p++) if (*p != '0') return *p >= '0' && *p <= '9' ? 2 : 0; break; }
            return 0;
        }
        if (v > (UINT64_MAX - 9) / 10) return 2;
        v = v * 10 + (uint64_t)(*p - '0');
    }
    if (is_signed) {
        uint64_t limit = bits == 64 ? (uint64_t)INT64_MAX : ((1ULL << (bits - 1)) - 1);
        if (v > limit + (negative ? 1 : 0)) return 2;
        *result = negative ? (int64_t)(0 - v) : (int64_t)v;
    } else {
        if (negative && v != 0) return 2;
        uint64_t limit = bits == 64 ? UINT64_MAX : ((1ULL << bits) - 1);
        if (v > limit) return 2;
        *result = (int64_t)v;
    }
    return 1;
}

int32_t csrt_parse_f64_styled(csrt_String* s, int32_t styles, double* result) {
    char buf[256];
    *result = 0;
    if (!prepare(s, styles | AllowLeadingWhite | AllowTrailingWhite, buf, sizeof buf)) return 0;
    if (!strcmp(buf, "NaN")) { *result = __builtin_nan(""); return 1; }
    if (!strcmp(buf, "Infinity") || !strcmp(buf, "\xe2\x88\x9e")) { *result = __builtin_inf(); return 1; }
    if (!strcmp(buf, "-Infinity")) { *result = -__builtin_inf(); return 1; }
    char* end;
    double v = strtod(buf, &end);
    if (end == buf || *end) return 0;
    *result = v;
    return 1;
}

static const csrt_MethodTable mt_format = { NULL, "System.FormatException", 0, 0 };
static const csrt_MethodTable mt_overflow = { NULL, "System.OverflowException", 0, 0 };
static const csrt_MethodTable mt_argument_null = { NULL, "System.ArgumentNullException", 0, 0 };

static csrt_String* make(const char* text) {
    int32_t n = (int32_t)strlen(text);
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)n));
    s->length = n;
    for (int32_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    return s;
}

_Noreturn static void format_error(csrt_String* s) {
    char head[] = "The input string '";
    char tail[] = "' was not in a correct format.";
    int32_t n = (int32_t)(sizeof head - 1 + (size_t)s->length + sizeof tail - 1);
    csrt_String* msg = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)n));
    msg->length = n;
    int32_t k = 0;
    for (const char* p = head; *p; p++) msg->chars[k++] = (uint8_t)*p;
    for (int32_t i = 0; i < s->length; i++) msg->chars[k++] = s->chars[i];
    for (const char* p = tail; *p; p++) msg->chars[k++] = (uint8_t)*p;
    csrt_throw(&mt_format, msg);
}

/// int/long/... .Parse(s, NumberStyles[, provider]): `type_name` is "Int32", "Int64", ...
int64_t csrt_parse_i64_styled_or_throw(csrt_String* s, int32_t styles, int32_t bits, bool is_signed, const char* type_name) {
    if (!s) csrt_throw(&mt_argument_null, make("Value cannot be null. (Parameter 's')"));
    int64_t v;
    switch (csrt_parse_i64_styled(s, styles, bits, is_signed, &v)) {
        case 1: return v;
        case 2: {
            char msg[96];
            const char* article = type_name[0] == 'I' || type_name[0] == 'S' ? "an" : "a"; // an Int32, an SByte, a UInt32, a Byte
            snprintf(msg, sizeof msg, "Value was either too large or too small for %s %s.", article, type_name);
            csrt_throw(&mt_overflow, make(msg));
        }
        default: format_error(s);
    }
}

double csrt_parse_f64_styled_or_throw(csrt_String* s, int32_t styles) {
    if (!s) csrt_throw(&mt_argument_null, make("Value cannot be null. (Parameter 's')"));
    double v;
    if (!csrt_parse_f64_styled(s, styles, &v)) format_error(s);
    return v;
}

static uint16_t fold(uint16_t c) { return (c >= 'A' && c <= 'Z') ? (uint16_t)(c + 32) : c; }

static bool equals_name(const uint16_t* s, int32_t n, const csrt_String* name, bool ignore_case) {
    if (n != name->length) return false;
    for (int32_t i = 0; i < n; i++)
        if (ignore_case ? fold(s[i]) != fold(name->chars[i]) : s[i] != name->chars[i]) return false;
    return true;
}

/// Enum.TryParse: a member name, a comma-separated list of names (OR-ed, for [Flags]), or a number.
/// Returns 1 on success, 0 on failure. `values`/`names` come from ModuleCodeGen.EnumTable.
int32_t csrt_enum_parse(csrt_String* s, bool ignore_case, const uint64_t* values, csrt_String* const* names, int32_t count,
                        int32_t bits, bool is_signed, int64_t* result) {
    *result = 0;
    if (!s) return 0;
    int32_t start = 0, end = s->length;
    while (start < end && s->chars[start] == ' ') start++;
    while (end > start && s->chars[end - 1] == ' ') end--;
    if (start == end) return 0;
    uint16_t first = s->chars[start];
    if ((first >= '0' && first <= '9') || first == '-' || first == '+') {
        csrt_String* tmp = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)(end - start)));
        tmp->length = end - start;
        memcpy(tmp->chars, s->chars + start, 2 * (size_t)(end - start));
        return csrt_parse_i64_styled(tmp, 7 /* Integer */, bits, is_signed, result) == 1;
    }
    uint64_t acc = 0;
    int32_t i = start;
    while (i < end) {
        int32_t j = i;
        while (j < end && s->chars[j] != ',') j++;
        int32_t a = i, b = j;
        while (a < b && s->chars[a] == ' ') a++;
        while (b > a && s->chars[b - 1] == ' ') b--;
        bool found = false;
        for (int32_t k = 0; k < count && !found; k++)
            if (equals_name(s->chars + a, b - a, names[k], ignore_case)) { acc |= values[k]; found = true; }
        if (!found) return 0;
        i = j + 1;
    }
    *result = (int64_t)acc;
    return 1;
}

static const csrt_MethodTable mt_argument = { NULL, "System.ArgumentException", 0, 0 };

/// Enum.Parse: like csrt_enum_parse but throws .NET's ArgumentException on failure.
int64_t csrt_enum_parse_or_throw(csrt_String* s, bool ignore_case, const uint64_t* values, csrt_String* const* names, int32_t count,
                                 int32_t bits, bool is_signed) {
    if (!s) csrt_throw(&mt_argument_null, make("Value cannot be null. (Parameter 'value')"));
    int64_t v;
    if (csrt_enum_parse(s, ignore_case, values, names, count, bits, is_signed, &v)) return v;
    char head[] = "Requested value '";
    char tail[] = "' was not found.";
    int32_t n = (int32_t)(sizeof head - 1 + (size_t)s->length + sizeof tail - 1);
    csrt_String* msg = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)n));
    msg->length = n;
    int32_t k = 0;
    for (const char* p = head; *p; p++) msg->chars[k++] = (uint8_t)*p;
    for (int32_t i = 0; i < s->length; i++) msg->chars[k++] = s->chars[i];
    for (const char* p = tail; *p; p++) msg->chars[k++] = (uint8_t)*p;
    csrt_throw(&mt_argument, msg);
}

/// Int128/UInt128.ToString(): decimal digits of the 128-bit value (lo, hi halves).
csrt_String* csrt_format_i128(uint64_t lo, uint64_t hi, bool is_signed) {
    unsigned __int128 v = ((unsigned __int128)hi << 64) | lo;
    bool negative = is_signed && (hi >> 63);
    if (negative) v = (unsigned __int128)0 - v;
    char buf[48];
    int n = 0;
    do { buf[n++] = (char)('0' + (int)(v % 10)); v /= 10; } while (v);
    if (negative) buf[n++] = '-';
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)n));
    s->length = n;
    for (int i = 0; i < n; i++) s->chars[i] = (uint8_t)buf[n - 1 - i];
    return s;
}
