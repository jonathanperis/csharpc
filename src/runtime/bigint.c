// csrt bigint: the method table of boxed System.Numerics.BigInteger. BigInteger itself is C#
// (corelib BigIntegerOps.cs, ADR 0008): a value is a reference to an immutable uint[] (null = 0);
// the boxed value's ToString/Equals/GetHashCode slots call the corelib exports.
#include "csrt.h"

csrt_String* csrt_bigint_to_string(void* a);
int32_t csrt_bigint_compare(void* a, void* b);
int32_t csrt_bigint_get_hash_code(void* a);

static csrt_String* box_to_string(void* o) { return csrt_bigint_to_string(*(void**)((char*)o + 8)); }
static bool box_equals(void* a, void* b) {
    return b && ((csrt_Object*)b)->mt == ((csrt_Object*)a)->mt &&
           csrt_bigint_compare(*(void**)((char*)a + 8), *(void**)((char*)b + 8)) == 0;
}
static int32_t box_hash(void* a) { return csrt_bigint_get_hash_code(*(void**)((char*)a + 8)); }

const csrt_BuiltinMethodTable csrt_Boxed_BigInteger_MT = {
    { &csrt_Object_MT.header, "System.Numerics.BigInteger", 16, 0, NULL, 0, 0 },
    { (void*)box_to_string, (void*)box_equals, (void*)box_hash },
};
