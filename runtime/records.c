// csrt helpers for compiler-synthesized members (records, anonymous types).
#include "csrt.h"

int32_t csrt_string_get_hash_code(csrt_String* s);

int32_t csrt_string_get_hash_code_or_zero(csrt_String* s) { return s ? csrt_string_get_hash_code(s) : 0; }

/// EqualityComparer<object>.Default.GetHashCode(o): 0 for null, else virtual GetHashCode().
int32_t csrt_object_get_hash_code_or_zero(void* obj) {
    if (!obj) return 0;
    const csrt_MethodTable* mt = ((csrt_Object*)obj)->mt;
    const void* const* vtable = (const void* const*)((const char*)mt + sizeof(csrt_MethodTable));
    return ((int32_t (*)(void*))vtable[CSRT_SLOT_GET_HASH_CODE])(obj);
}

/// object.Equals(a, b): reference-equal, or both non-null and a.Equals(b) (virtual).
bool csrt_object_equals_static(void* a, void* b) {
    if (a == b) return true;
    if (!a || !b) return false;
    const csrt_MethodTable* mt = ((csrt_Object*)a)->mt;
    const void* const* vtable = (const void* const*)((const char*)mt + sizeof(csrt_MethodTable));
    return ((bool (*)(void*, void*))vtable[CSRT_SLOT_EQUALS])(a, b);
}

/// Hash of an address (used for a record's EqualityContract, i.e. its runtime type).
int32_t csrt_pointer_hash(const void* p) { return (int32_t)(((uintptr_t)p >> 3) * 2654435761u); }
