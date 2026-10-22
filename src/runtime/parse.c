// csrt parse: Enum.Parse / Enum.TryParse over the compiler-emitted name/value tables. Number parsing
// itself is C# (src/Csharpc/corelib/Number.Parsing.cs); the numeric case calls its export.
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <string.h>

/// Number.ParseInt64Styled (C#): 1 on success, 0 on format error, 2 on overflow.
int32_t csrt_parse_i64_styled(csrt_String* s, int32_t styles, int32_t bits, bool is_signed, int64_t* result);

static const csrt_MethodTable mt_argument_null = { NULL, "System.ArgumentNullException", 0, 0 };

static csrt_String* make(const char* text) {
    int32_t n = (int32_t)strlen(text);
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)n));
    s->length = n;
    for (int32_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    return s;
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
