// csrt decimal: the method table of boxed System.Decimal. Decimal arithmetic, conversions, parsing
// and formatting are C# (corelib DecimalOps.cs and Number.Formatting.cs, ADR 0008); the boxed
// value's ToString/Equals/GetHashCode slots call their exports.
//
// Layout (matches the CLR): { int32 flags; uint32 hi32; uint64 lo64 }.
#include "csrt.h"

typedef struct { int32_t flags; uint32_t hi; uint64_t lo; } csrt_Decimal;

csrt_String* csrt_decimal_to_string(const void* d);
int32_t csrt_decimal_compare(const csrt_Decimal* a, const csrt_Decimal* b);
int32_t csrt_decimal_get_hash_code(const csrt_Decimal* d);

static csrt_String* box_to_string(void* o) { return csrt_decimal_to_string((char*)o + 8); }
static bool box_equals(void* self, void* other) {
    return other && ((csrt_Object*)other)->mt == ((csrt_Object*)self)->mt &&
           csrt_decimal_compare((const csrt_Decimal*)((char*)self + 8), (const csrt_Decimal*)((char*)other + 8)) == 0;
}
static int32_t box_hash(void* self) { return csrt_decimal_get_hash_code((const csrt_Decimal*)((char*)self + 8)); }

const csrt_BuiltinMethodTable csrt_Boxed_Decimal_MT = {
    { &csrt_Object_MT.header, "System.Decimal", 8 + sizeof(csrt_Decimal), 0 },
    { (void*)box_to_string, (void*)box_equals, (void*)box_hash },
};
