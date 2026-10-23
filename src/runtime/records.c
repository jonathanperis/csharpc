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

/// [Flags] enum ToString, as .NET does it: exact name if defined; otherwise decompose into the
/// defined flags (largest first, listed in ascending value order, ", "-separated); if bits remain
/// that no flag covers, the plain number. `values` are sorted ascending; names are csrt strings.
csrt_String* csrt_string_from_u64(uint64_t v);
csrt_String* csrt_string_from_i64(int64_t v);
csrt_String* csrt_string_concat(csrt_String* a, csrt_String* b);

csrt_String* csrt_enum_format_flags(uint64_t value, const uint64_t* values, csrt_String* const* names, int32_t count,
                                    bool is_signed, int64_t signed_value) {
    for (int32_t i = 0; i < count; i++) if (values[i] == value) return names[i];
    if (value == 0) return csrt_string_from_u64(0);
    uint64_t remaining = value;
    bool used[64] = {false};
    for (int32_t i = count - 1; i >= 0; i--) {
        if (values[i] != 0 && (remaining & values[i]) == values[i] && i < 64) { used[i] = true; remaining &= ~values[i]; }
    }
    if (remaining != 0) return is_signed ? csrt_string_from_i64(signed_value) : csrt_string_from_u64(value);
    csrt_String* result = NULL;
    static csrt_String* separator;
    if (!separator) {
        separator = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 4));
        separator->length = 2; separator->chars[0] = ','; separator->chars[1] = ' ';
    }
    for (int32_t i = 0; i < count && i < 64; i++) {
        if (!used[i]) continue;
        result = result ? csrt_string_concat(csrt_string_concat(result, separator), names[i]) : names[i];
    }
    return result;
}

static const csrt_MethodTable mt_format_exception = { NULL, "System.FormatException", 0, 0 };

/// Enum.ToString(format) / "{e:F}": NULL means "G" (the caller formats by name). D: the number;
/// X: hex digits for the full underlying width (uppercase for 'x' too, as in .NET); F: flags
/// decomposition like .NET's TryFormatFlagNames (exact match searched from the highest index, then
/// largest flags first, listed in ascending order; leftover bits give the number).
/// `values` are all members sorted ascending (aliases included); `names` match.
csrt_String* csrt_enum_format(uint64_t value, int64_t signed_value, csrt_String* format,
                              const uint64_t* values, csrt_String* const* names, int32_t count, int32_t bits, bool is_signed) {
    if (!format || format->length == 0) return NULL;
    if (format->length == 1) {
        switch (format->chars[0]) {
        case 'G': case 'g': return NULL;
        case 'D': case 'd': return is_signed ? csrt_string_from_i64(signed_value) : csrt_string_from_u64(value);
        case 'X': case 'x': {
            int digits = bits / 4;
            csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)digits));
            s->length = digits;
            for (int i = digits - 1; i >= 0; i--) { s->chars[i] = (uint16_t)"0123456789ABCDEF"[value & 15]; value >>= 4; }
            return s;
        }
        case 'F': case 'f': {
            for (int32_t i = count - 1; i >= 0; i--) if (values[i] == value) return names[i];
            if (value == 0) return csrt_string_from_u64(0);
            uint64_t remaining = value;
            bool used[256] = {false};
            for (int32_t i = count - 1; i >= 0; i--) {
                if (values[i] != 0 && (remaining & values[i]) == values[i] && i < 256) { used[i] = true; remaining &= ~values[i]; }
            }
            if (remaining != 0) return is_signed ? csrt_string_from_i64(signed_value) : csrt_string_from_u64(value);
            csrt_String* result = NULL;
            csrt_String* separator = csrt_string_from_utf8(", ");
            for (int32_t i = 0; i < count && i < 256; i++) {
                if (!used[i]) continue;
                result = result ? csrt_string_concat(csrt_string_concat(result, separator), names[i]) : names[i];
            }
            return result;
        }
        }
    }
    csrt_throw(&mt_format_exception,
               csrt_string_from_utf8("Format string can be only \"G\", \"g\", \"X\", \"x\", \"F\", \"f\", \"D\" or \"d\"."));
    return NULL;  // wasm32: throwing returns (CSRT_THROWS)
}
