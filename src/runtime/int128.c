// csrt int128: the method tables of boxed System.Int128 and System.UInt128 (an i128 payload at offset 8).
// Arithmetic is LLVM's i128 (CodeGen/Int128Support.cs); text is the corelib's (Number.FormatInt128).
#include "csrt.h"

csrt_String* csrt_int128_to_string(uint64_t lo, uint64_t hi);
csrt_String* csrt_uint128_to_string(uint64_t lo, uint64_t hi);

static csrt_String* box_to_string_signed(void* o) {
    const uint64_t* p = (const uint64_t*)((char*)o + 8);
    return csrt_int128_to_string(p[0], p[1]);
}
static csrt_String* box_to_string_unsigned(void* o) {
    const uint64_t* p = (const uint64_t*)((char*)o + 8);
    return csrt_uint128_to_string(p[0], p[1]);
}
static bool box_equals(void* self, void* other) {
    if (!other || ((csrt_Object*)other)->mt != ((csrt_Object*)self)->mt) return false;
    const uint64_t* a = (const uint64_t*)((char*)self + 8);
    const uint64_t* b = (const uint64_t*)((char*)other + 8);
    return a[0] == b[0] && a[1] == b[1];
}
static int32_t box_hash(void* self) {
    const uint64_t* p = (const uint64_t*)((char*)self + 8);
    uint64_t h = (p[0] ^ (p[1] * 0x9E3779B97F4A7C15ull)) * 0xBF58476D1CE4E5B9ull;
    return (int32_t)(h ^ (h >> 32));
}

const csrt_BuiltinMethodTable csrt_Boxed_Int128_MT = {
    { &csrt_Object_MT.header, "System.Int128", 8 + 16, CSRT_MT_VALUE_TYPE },
    { (void*)box_to_string_signed, (void*)box_equals, (void*)box_hash },
};

const csrt_BuiltinMethodTable csrt_Boxed_UInt128_MT = {
    { &csrt_Object_MT.header, "System.UInt128", 8 + 16, CSRT_MT_VALUE_TYPE },
    { (void*)box_to_string_unsigned, (void*)box_equals, (void*)box_hash },
};
