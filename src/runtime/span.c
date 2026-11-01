// csrt spans: helpers behind Span<T> / ReadOnlySpan<T> / MemoryExtensions (FunctionCodeGen.Spans.cs).
// Spans are { T* ref, int32 length } aggregates in compiled code; here they arrive as (ptr, len).
// Element equality is bitwise over the element's storage size (what IEquatable<T> gives for
// integral primitives and chars; doubles compare bitwise, so NaN finds NaN as in .NET).
#include "csrt.h"

#include <stdlib.h>
#include <string.h>

csrt_String* csrt_string_from_chars(const uint16_t* chars, int32_t length) {
    csrt_String* s = csrt_string_alloc_zeroed((int64_t)length);
    s->length = length;
    if (length) memcpy(s->chars, chars, 2 * (size_t)length);
    return s;
}

csrt_Array* csrt_span_to_array(const void* data, int32_t length, int64_t element_size) {
    csrt_Array* a = csrt_new_array(element_size, length);
    CSRT_DIRTY(a->data, element_size * length);
    if (length) memcpy(a->data, data, (size_t)(element_size * length));
    return a;
}

// Searches over bytes and chars, 16 bytes at a time with portable vector extensions (SSE2 / NEON /
// wasm simd128 when enabled, scalar code otherwise): Kestrel's HTTP parser looks for CR, LF, ':' and
// friends in every header with MemoryExtensions.IndexOf/IndexOfAny. `except` finds the first element
// that is none of the values. Values a, b, c may repeat (IndexOfAny with two values passes b twice).
//
// Most searches are over a few dozen bytes, so there is no scalar head or tail: every load is an
// aligned 16-byte block, the first and last ones masked to the span. An aligned block never crosses a
// page boundary, so reading the bytes around the span can't fault (as libc's memchr does); the
// sanitizers are told so.
typedef uint8_t u8x16 __attribute__((vector_size(16), aligned(16)));
typedef uint16_t u16x8 __attribute__((vector_size(16), aligned(16)));
typedef bool b16 __attribute__((ext_vector_type(16)));
typedef bool b8 __attribute__((ext_vector_type(8)));

#if defined(__has_feature)
#if __has_feature(address_sanitizer)
#define CSRT_NO_ASAN __attribute__((no_sanitize("address")))
#endif
#endif
#ifndef CSRT_NO_ASAN
#define CSRT_NO_ASAN
#endif

CSRT_NO_ASAN int32_t csrt_span_index_of_any_u8(const uint8_t* p, int32_t length, uint8_t a, uint8_t b, uint8_t c, int32_t except) {
    if (length <= 0) return -1;
    const uint8_t* block = (const uint8_t*)((uintptr_t)p & ~(uintptr_t)15);
    uint32_t first = (uint32_t)(p - block);  // lanes of the first block before the span
    for (const uint8_t* end = p + length; block < end; block += 16) {
        u8x16 v = *(const u8x16*)block;
        u8x16 hit = (u8x16)((v == a) | (v == b) | (v == c));
        if (except) hit = ~hit;
        uint32_t mask = __builtin_bit_cast(uint16_t, __builtin_convertvector(hit, b16)) >> first << first;
        first = 0;
        if (mask) {
            int64_t index = block - p + __builtin_ctz(mask);
            return index < length ? (int32_t)index : -1;
        }
    }
    return -1;
}

CSRT_NO_ASAN int32_t csrt_span_index_of_any_u16(const uint16_t* p, int32_t length, uint16_t a, uint16_t b, uint16_t c, int32_t except) {
    if (length <= 0) return -1;
    if ((uintptr_t)p & 1) {  // not char-aligned (a span over unaligned memory): plain loop
        for (int32_t i = 0; i < length; i++) {
            uint16_t x;
            memcpy(&x, (const uint8_t*)p + 2 * (size_t)i, 2);
            if ((x == a || x == b || x == c) != (except != 0)) return i;
        }
        return -1;
    }
    const uint16_t* block = (const uint16_t*)((uintptr_t)p & ~(uintptr_t)15);
    uint32_t first = (uint32_t)(p - block);
    for (const uint16_t* end = p + length; block < end; block += 8) {
        u16x8 v = *(const u16x8*)block;
        u16x8 hit = (u16x8)((v == a) | (v == b) | (v == c));
        if (except) hit = ~hit;
        uint32_t mask = (uint32_t)__builtin_bit_cast(uint8_t, __builtin_convertvector(hit, b8)) >> first << first;
        first = 0;
        if (mask) {
            int64_t index = block - p + __builtin_ctz(mask);
            return index < length ? (int32_t)index : -1;
        }
    }
    return -1;
}

/// Narrows the leading ASCII chars of `src` into `dst` (at most `length` of each), 8 at a time; returns
/// how many it wrote: the encoders' fast path (Encoding.ASCII/UTF8/Latin1.GetBytes), which Kestrel runs
/// on every response header value it writes. Unaligned 16-byte accesses through memcpy (one movdqu).
int32_t csrt_narrow_ascii(const uint16_t* src, uint8_t* dst, int32_t length) {
    typedef uint16_t u16x8u __attribute__((vector_size(16)));
    typedef uint8_t u8x8u __attribute__((vector_size(8)));
    int32_t i = 0;
    for (; i + 8 <= length; i += 8) {
        u16x8u v;
        memcpy(&v, src + i, sizeof v);
        u16x8u high = v & (uint16_t)0xFF80;
        if (__builtin_reduce_or(high)) break;
        u8x8u narrow = __builtin_convertvector(v, u8x8u);
        memcpy(dst + i, &narrow, sizeof narrow);
    }
    for (; i < length && src[i] < 0x80; i++) dst[i] = (uint8_t)src[i];
    return i;
}

/// The reverse: widens the leading ASCII bytes of `src` into chars; returns how many it wrote.
int32_t csrt_widen_ascii(const uint8_t* src, uint16_t* dst, int32_t length) {
    typedef uint16_t u16x8u __attribute__((vector_size(16)));
    typedef uint8_t u8x8u __attribute__((vector_size(8)));
    int32_t i = 0;
    for (; i + 8 <= length; i += 8) {
        u8x8u v;
        memcpy(&v, src + i, sizeof v);
        if (__builtin_reduce_or(v & (uint8_t)0x80)) break;
        u16x8u wide = __builtin_convertvector(v, u16x8u);
        memcpy(dst + i, &wide, sizeof wide);
    }
    for (; i < length && src[i] < 0x80; i++) dst[i] = src[i];
    return i;
}

/// System.Text.Ascii.Equals over `length` elements of each side (bytes or chars: `a_wide`/`b_wide`): true
/// when every element is ASCII and equal. Kestrel compares each request header value with the string it
/// kept from the previous request on the connection (to reuse it), 8 elements at a time here.
int32_t csrt_ascii_equals(const void* a, int32_t a_wide, const void* b, int32_t b_wide, int32_t length) {
    typedef uint16_t u16x8u __attribute__((vector_size(16)));
    typedef uint8_t u8x8u __attribute__((vector_size(8)));
    int32_t i = 0;
    for (; i + 8 <= length; i += 8) {
        u16x8u x, y;
        if (a_wide) memcpy(&x, (const uint16_t*)a + i, sizeof x);
        else { u8x8u n; memcpy(&n, (const uint8_t*)a + i, sizeof n); x = __builtin_convertvector(n, u16x8u); }
        if (b_wide) memcpy(&y, (const uint16_t*)b + i, sizeof y);
        else { u8x8u n; memcpy(&n, (const uint8_t*)b + i, sizeof n); y = __builtin_convertvector(n, u16x8u); }
        // Equal and ASCII: (x ^ y) | (x & 0xFF80) is zero in every lane.
        if (__builtin_reduce_or((x ^ y) | (x & (uint16_t)0xFF80))) return 0;
    }
    for (; i < length; i++) {
        uint16_t x = a_wide ? ((const uint16_t*)a)[i] : ((const uint8_t*)a)[i];
        uint16_t y = b_wide ? ((const uint16_t*)b)[i] : ((const uint8_t*)b)[i];
        if (x != y || x > 0x7F) return 0;
    }
    return 1;
}

// 4- and 8-byte elements (int, uint, float bits, long, ...: MemoryExtensions.IndexOf/Contains, Enumerable.Contains
// over arrays and lists), 16 bytes at a time through unaligned loads, then the tail one element at a time.
typedef uint32_t u32x4u __attribute__((vector_size(16)));
typedef uint64_t u64x2u __attribute__((vector_size(16)));

static int32_t index_of_u32(const void* data, int32_t length, uint32_t v) {
    const uint8_t* p = data;
    int32_t i = 0;
    for (; i + 8 <= length; i += 8) {
        u32x4u x, y;
        memcpy(&x, p + 4 * (size_t)i, sizeof x);
        memcpy(&y, p + 4 * (size_t)i + 16, sizeof y);
        if (__builtin_reduce_or((x == v) | (y == v))) break;
    }
    for (; i < length; i++) {
        uint32_t x;
        memcpy(&x, p + 4 * (size_t)i, sizeof x);
        if (x == v) return i;
    }
    return -1;
}

static int32_t index_of_u64(const void* data, int32_t length, uint64_t v) {
    const uint8_t* p = data;
    int32_t i = 0;
    for (; i + 4 <= length; i += 4) {
        u64x2u x, y;
        memcpy(&x, p + 8 * (size_t)i, sizeof x);
        memcpy(&y, p + 8 * (size_t)i + 16, sizeof y);
        if (__builtin_reduce_or((x == v) | (y == v))) break;
    }
    for (; i < length; i++) {
        uint64_t x;
        memcpy(&x, p + 8 * (size_t)i, sizeof x);
        if (x == v) return i;
    }
    return -1;
}

int32_t csrt_span_index_of(const void* data, int32_t length, int64_t element_size, const void* value) {
    if (element_size == 1) {
        const void* hit = length > 0 ? memchr(data, *(const uint8_t*)value, (size_t)length) : NULL;
        return hit ? (int32_t)((const uint8_t*)hit - (const uint8_t*)data) : -1;
    }
    if (element_size == 2) {
        uint16_t v;
        memcpy(&v, value, sizeof v);
        return csrt_span_index_of_any_u16(data, length, v, v, v, 0);
    }
    if (element_size == 4) {
        uint32_t v;
        memcpy(&v, value, sizeof v);
        return index_of_u32(data, length, v);
    }
    if (element_size == 8) {
        uint64_t v;
        memcpy(&v, value, sizeof v);
        return index_of_u64(data, length, v);
    }
    const char* p = data;
    for (int32_t i = 0; i < length; i++)
        if (memcmp(p + (size_t)i * (size_t)element_size, value, (size_t)element_size) == 0) return i;
    return -1;
}

int32_t csrt_span_last_index_of(const void* data, int32_t length, int64_t element_size, const void* value) {
    const char* p = data;
    for (int32_t i = length - 1; i >= 0; i--)
        if (memcmp(p + (size_t)i * (size_t)element_size, value, (size_t)element_size) == 0) return i;
    return -1;
}

int32_t csrt_span_index_of_span(const void* data, int32_t length, const void* needle, int32_t needle_length, int64_t element_size) {
    if (needle_length == 0) return 0;
    const char* p = data;
    size_t bytes = (size_t)needle_length * (size_t)element_size;
    for (int32_t i = 0; i + needle_length <= length; i++)
        if (memcmp(p + (size_t)i * (size_t)element_size, needle, bytes) == 0) return i;
    return -1;
}

/// mode 0: SequenceEqual, 1: StartsWith, 2: EndsWith
bool csrt_span_compare_mode(const void* a, int32_t a_length, const void* b, int32_t b_length, int64_t element_size, int32_t mode) {
    if (mode == 0 && a_length != b_length) return false;
    if (b_length > a_length) return false;
    const char* start = (const char*)a + (mode == 2 ? (size_t)(a_length - b_length) * (size_t)element_size : 0);
    return memcmp(start, b, (size_t)b_length * (size_t)element_size) == 0;
}

void csrt_span_reverse(void* data, int32_t length, int64_t element_size) {
    char* p = data;
    char tmp[64];
    if (element_size > (int64_t)sizeof tmp) return;
    for (int32_t i = 0, j = length - 1; i < j; i++, j--) {
        memcpy(tmp, p + (size_t)i * (size_t)element_size, (size_t)element_size);
        memcpy(p + (size_t)i * (size_t)element_size, p + (size_t)j * (size_t)element_size, (size_t)element_size);
        memcpy(p + (size_t)j * (size_t)element_size, tmp, (size_t)element_size);
    }
}

static bool is_space(uint16_t c) {
    return c == ' ' || (c >= 0x09 && c <= 0x0D) || c == 0x85 || c == 0xA0 || c == 0x1680 ||
           (c >= 0x2000 && c <= 0x200A) || c == 0x2028 || c == 0x2029 || c == 0x202F || c == 0x205F || c == 0x3000;
}

typedef struct { const uint16_t* ptr; int32_t length; } CharSpan;

/// ReadOnlySpan<char>.Trim/TrimStart/TrimEnd (mode bit 1: start, bit 2: end) — result written to *out.
void csrt_span_trim(const uint16_t* chars, int32_t length, int32_t mode, CharSpan* out) {
    int32_t start = 0, end = length;
    if (mode & 1) while (start < end && is_space(chars[start])) start++;
    if (mode & 2) while (end > start && is_space(chars[end - 1])) end--;
    out->ptr = chars + start;
    out->length = end - start;
}

// ---------------------------------------------------------------------------------------------
// Enumerable aggregates over arrays and lists (corelib/LinqSpans.cs), what .NET's Enumerable runs on
// the span of an array or List<T>.
// ---------------------------------------------------------------------------------------------

/// Enumerable.Sum over ints, as .NET's Sum<int, int>(ReadOnlySpan<int>): a span of at least four vectors is
/// summed by SumSignedIntegersVectorized with Vector<int> of 16 bytes (csharpc's and NativeAOT's Vector<T>
/// size, FunctionCodeGen.IlCalls.cs): four lanes, a lane overflow detected per unrolled step and per tail of
/// whole vectors, then the lanes and the remaining elements added with overflow checks, in that order. The
/// order decides which inputs overflow (an intermediate lane can overflow when the sequential sum would
/// not, and the other way round), so it is .NET's exactly. Returns 1 on overflow, else 0 with the sum.
int32_t csrt_linq_sum_i32(const int32_t* p, int32_t length, int32_t* result) {
    enum { COUNT = 4 };
    int32_t sum = 0;
    size_t index = 0, n = (size_t)(length > 0 ? length : 0);
    if (n >= COUNT * 4) {
        const uint32_t sign = 0x80000000u;
        u32x4u acc = { 0, 0, 0, 0 };
        size_t limit = n - COUNT * 4;
        do {
            u32x4u data, acc2, overflow;
            memcpy(&data, p + index, sizeof data);
            acc2 = acc + data;
            overflow = (acc2 ^ acc) & (acc2 ^ data);
            memcpy(&data, p + index + COUNT, sizeof data);
            acc = acc2 + data;
            overflow |= (acc ^ acc2) & (acc ^ data);
            memcpy(&data, p + index + COUNT * 2, sizeof data);
            acc2 = acc + data;
            overflow |= (acc2 ^ acc) & (acc2 ^ data);
            memcpy(&data, p + index + COUNT * 3, sizeof data);
            acc = acc2 + data;
            overflow |= (acc ^ acc2) & (acc ^ data);
            if (__builtin_reduce_or(overflow & sign)) return 1;
            index += COUNT * 4;
        } while (index < limit);
        limit = n - COUNT;
        if (index < limit) {
            u32x4u overflow = { 0, 0, 0, 0 };
            do {
                u32x4u data, acc2;
                memcpy(&data, p + index, sizeof data);
                acc2 = acc + data;
                overflow |= (acc2 ^ acc) & (acc2 ^ data);
                acc = acc2;
                index += COUNT;
            } while (index < limit);
            if (__builtin_reduce_or(overflow & sign)) return 1;
        }
        for (int i = 0; i < COUNT; i++)
            if (__builtin_add_overflow(sum, (int32_t)acc[i], &sum)) return 1;
    }
    for (; index < n; index++)
        if (__builtin_add_overflow(sum, p[index], &sum)) return 1;
    *result = sum;
    return 0;
}

/// Min (max = 0) or max (max = 1) of a non-empty span of ints or longs: plain reductions clang vectorizes.
int32_t csrt_linq_min_max_i32(const int32_t* p, int32_t length, int32_t max) {
    int32_t m = p[0];
    if (max) { for (int32_t i = 1; i < length; i++) m = p[i] > m ? p[i] : m; }
    else { for (int32_t i = 1; i < length; i++) m = p[i] < m ? p[i] : m; }
    return m;
}

int64_t csrt_linq_min_max_i64(const int64_t* p, int32_t length, int32_t max) {
    int64_t m = p[0];
    if (max) { for (int32_t i = 1; i < length; i++) m = p[i] > m ? p[i] : m; }
    else { for (int32_t i = 1; i < length; i++) m = p[i] < m ? p[i] : m; }
    return m;
}

/// Enumerable.Min (max = 0) / Max (max = 1) over a non-empty span of doubles or floats, with .NET's span
/// MinFloat / MaxFloat results: Min returns the first NaN after the first element, else the first element
/// if it is NaN; Max skips NaNs (all NaN: the last element). With all_nan_first (MemoryExtensions.Min/Max,
/// which order by CompareTo), Min returns the first NaN and an all-NaN Max the first element. Otherwise the extreme value, and of equal extremes the first, which only matters for -0.0 and
/// 0.0 (equal, distinct bits): a zero result is the first zero of the span. Two lanes, 16 bytes at a time.
#define LINQ_MIN_MAX_FLOAT(name, T, VT, LANES)                                                              \
    T name(const T* p, int32_t length, int32_t max, int32_t all_nan_first) {                                  \
        typedef T vec __attribute__((vector_size(16)));                                                       \
        int32_t i = all_nan_first ? 0 : 1, start = 0;                                                         \
        if (!max) {                                                                                           \
            for (; i + LANES <= length; i += LANES) {                                                         \
                vec x; memcpy(&x, p + i, sizeof x);                                                           \
                if (__builtin_reduce_or((VT)(x != x))) break;                                                 \
            }                                                                                                 \
            for (; i < length; i++) if (p[i] != p[i]) return p[i];                                            \
            if (p[0] != p[0]) return p[0];                                                                    \
        } else {                                                                                              \
            while (start < length && p[start] != p[start]) start++;                                           \
            if (start == length) return p[all_nan_first ? 0 : length - 1];                                    \
        }                                                                                                     \
        T m = p[start];                                                                                       \
        vec acc = m - (vec){0};                                                                               \
        for (i = start; i + LANES <= length; i += LANES) {                                                    \
            vec x; memcpy(&x, p + i, sizeof x);                                                               \
            acc = max ? __builtin_elementwise_max(acc, x) : __builtin_elementwise_min(acc, x);                \
        }                                                                                                     \
        for (int32_t l = 0; l < LANES; l++) m = max ? (acc[l] > m ? acc[l] : m) : (acc[l] < m ? acc[l] : m);  \
        for (; i < length; i++) {                                                                             \
            T x = p[i];                                                                                       \
            if (max ? x > m : x < m) m = x;                                                                   \
        }                                                                                                     \
        if (m == 0)                                                                                           \
            for (i = start; i < length; i++) if (p[i] == 0) return p[i];                                      \
        return m;                                                                                             \
    }

typedef int64_t i64x2v __attribute__((vector_size(16)));
typedef int32_t i32x4v __attribute__((vector_size(16)));
LINQ_MIN_MAX_FLOAT(csrt_linq_min_max_f64, double, i64x2v, 2)
LINQ_MIN_MAX_FLOAT(csrt_linq_min_max_f32, float, i32x4v, 4)
