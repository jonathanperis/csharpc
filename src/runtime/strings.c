// csrt strings: the string primitives the C runtime itself needs (ordinal comparison for
// string.Compare(..., StringComparison.Ordinal) in bclmisc.c, hashing for records and dictionaries).
// The System.String / System.Char / StringBuilder library surface is C# in the corelib
// (src/Csharpc/corelib/StringOps.cs, StringBuilder.cs; ADR 0008).
#include "csrt.h"

#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
int rand_s(unsigned int* value);
#elif defined(__wasi__)
#include <wasi/api.h>
#elif defined(__APPLE__) || defined(__linux__)
#include <sys/random.h>
#else
#include <unistd.h>
#endif

int32_t csrt_string_compare_ordinal(csrt_String* a, csrt_String* b) {
    if (a == b) return 0;
    if (!a) return -1;
    if (!b) return 1;
    int32_t n = a->length < b->length ? a->length : b->length;
    for (int32_t i = 0; i < n; i++)
        if (a->chars[i] != b->chars[i]) return (int32_t)a->chars[i] - (int32_t)b->chars[i];
    return a->length - b->length;
}

/// Bytes from the OS random number generator. A failing generator stops the program: carrying on with a predictable value would hand
/// an attacker the seed of every string hash (hash flooding).
void csrt_os_random(void* buffer, size_t count) {
    uint8_t* bytes = buffer;
#ifdef _WIN32
    for (size_t i = 0; i < count; i++) {
        unsigned int value;
        if (rand_s(&value) != 0) abort();
        bytes[i] = (uint8_t)value;
    }
#elif defined(__wasi__)
    // WASI preview 1 random_get (wasi-libc's headers declare no getentropy)
    if (__wasi_random_get(bytes, count) != 0) abort();
#else
    while (count > 0) {
        size_t n = count > 256 ? 256 : count; // getentropy's limit
        if (getentropy(bytes, n) != 0) abort();
        bytes += n;
        count -= n;
    }
#endif
}

// ---- Marvin, as in .NET: string.GetHashCode is Marvin seeded with a per-process random value --------------------------------------------
// (Hash tables keyed by program input would otherwise be open to hash flooding. Dictionary<string, T> and HashSet<string> start with the
// corelib's fast non-randomized comparer, as in .NET, and switch to this hash after many collisions.) The seed is drawn once, in csrt_init.
static uint32_t s_marvin_p0, s_marvin_p1;

void csrt_string_hash_init(void) {
    uint32_t seed[2] = {0, 0};
    while (seed[0] == 0 && seed[1] == 0) csrt_os_random(seed, sizeof seed);
    s_marvin_p0 = seed[0];
    s_marvin_p1 = seed[1];
}

static inline uint32_t rotl32(uint32_t v, int n) { return (v << n) | (v >> (32 - n)); }

static inline void marvin_block(uint32_t* rp0, uint32_t* rp1) {
    uint32_t p0 = *rp0, p1 = *rp1;
    p1 ^= p0; p0 = rotl32(p0, 20);
    p0 += p1; p1 = rotl32(p1, 9);
    p1 ^= p0; p0 = rotl32(p0, 27);
    p0 += p1; p1 = rotl32(p1, 19);
    *rp0 = p0;
    *rp1 = p1;
}

int32_t csrt_string_get_hash_code(csrt_String* s) {
    if (!s) csrt_throw_null_reference();
    const uint8_t* p = (const uint8_t*)s->chars;
    uint32_t count = (uint32_t)s->length * 2u; // always even
    uint32_t p0 = s_marvin_p0, p1 = s_marvin_p1;
    uint32_t v;
    while (count >= 8) {
        memcpy(&v, p, 4); p0 += v; marvin_block(&p0, &p1);
        memcpy(&v, p + 4, 4); p0 += v; marvin_block(&p0, &p1);
        p += 8;
        count -= 8;
    }
    if (count >= 4) {
        memcpy(&v, p, 4); p0 += v; marvin_block(&p0, &p1);
        p += 4;
        count -= 4;
    }
    uint32_t last = 0x80u; // the end marker, after the last char (if one is left)
    if (count == 2) {
        uint16_t two;
        memcpy(&two, p, 2);
        last = 0x80u << 16 | two;
    }
    p0 += last;
    marvin_block(&p0, &p1);
    marvin_block(&p0, &p1);
    return (int32_t)(p1 ^ p0);
}
