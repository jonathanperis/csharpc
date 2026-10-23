// csrt strings: the string primitives the C runtime itself needs (ordinal comparison for
// string.Compare(..., StringComparison.Ordinal) in bclmisc.c, hashing for records and dictionaries).
// The System.String / System.Char / StringBuilder library surface is C# in the corelib
// (src/Csharpc/corelib/StringOps.cs, StringBuilder.cs; ADR 0008).
#include "csrt.h"

#include <string.h>

int32_t csrt_string_compare_ordinal(csrt_String* a, csrt_String* b) {
    if (a == b) return 0;
    if (!a) return -1;
    if (!b) return 1;
    int32_t n = a->length < b->length ? a->length : b->length;
    for (int32_t i = 0; i < n; i++)
        if (a->chars[i] != b->chars[i]) return (int32_t)a->chars[i] - (int32_t)b->chars[i];
    return a->length - b->length;
}

/// Four chars per multiply (the old char-at-a-time FNV-1a was most of a Dictionary<string, T>
/// lookup). Stable per process; .NET randomizes string hashes, so values differ by design and no
/// program output can depend on them.
int32_t csrt_string_get_hash_code(csrt_String* s) {
    if (!s) csrt_throw_null_reference();
    const uint64_t k = 0x9E3779B97F4A7C15ull;
    const uint16_t* p = s->chars;
    int32_t n = s->length;
    uint64_t h = k ^ (uint64_t)(uint32_t)n;
#pragma clang loop unroll(disable) // keys are mostly short: unrolling only adds set-up work
    for (; n >= 4; n -= 4, p += 4) {
        uint64_t v;
        memcpy(&v, p, sizeof v);
        h = (h ^ v) * k;
        h ^= h >> 29;
    }
    uint64_t tail = 0;
    uint32_t two;
    switch (n) {
    case 3: memcpy(&two, p, sizeof two); tail = two | (uint64_t)p[2] << 32; break;
    case 2: memcpy(&two, p, sizeof two); tail = two; break;
    case 1: tail = p[0]; break;
    default: break;
    }
    h = (h ^ tail) * k;
    h ^= h >> 32;
    h *= 0xD6E8FEB86659FD93ull;
    h ^= h >> 32;
    return (int32_t)h;
}
