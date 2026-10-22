// csrt strings: the string primitives the C runtime itself needs (ordinal comparison for
// string.Compare(..., StringComparison.Ordinal) in bclmisc.c, hashing for records and dictionaries).
// The System.String / System.Char / StringBuilder library surface is C# in the corelib
// (src/Csharpc/corelib/StringOps.cs, StringBuilder.cs; ADR 0008).
#include "csrt.h"

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
    if (!s) csrt_throw_null_reference();
    uint32_t h = 2166136261u;  // FNV-1a (stable per process; .NET randomizes, so values differ by design)
    for (int32_t i = 0; i < s->length; i++) { h ^= s->chars[i]; h *= 16777619u; }
    return (int32_t)h;
}
