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
