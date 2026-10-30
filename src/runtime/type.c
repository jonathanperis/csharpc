// csrt type: System.Type objects. One canonical Type object per method table, so
// typeof(X) == obj.GetType() is a pointer comparison. Names come from the method table's CLR
// name ("System.Int32", "Program+Nested", "MyNs.Point", "System.Int32[]"); everything else
// (IsGenericType, BaseType, IsAssignableFrom, Assembly, ...) from compile-time metadata tables
// (csrt_TypeInfo in csrt.h, emitted by ModuleCodeGen.TypeInfo.cs only for programs that ask).
//
// Throwing helpers return on wasm32 (CSRT_THROWS), so every throw below is followed by a return.
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"
#include "csrt_pthread.h"
#ifndef _WIN32
#include <sched.h>
#endif

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { const csrt_MethodTable* mt; const csrt_MethodTable* target; } TypeObject;

static csrt_String* type_to_string(void* o);
static void* type_virtual_unsupported(void);
// Type members reach the runtime through the compiler's Type handlers, not this vtable. A MemberInfo
// virtual called on a Type through a MemberInfo-typed reference lands in the slots after Object's:
// they throw instead of running whatever data would follow a three-slot vtable.
#define TYPE_MEMBERINFO_SLOTS 61
const struct { csrt_MethodTable header; void* vtable[3 + TYPE_MEMBERINFO_SLOTS]; } csrt_Type_MT = {
    { &csrt_Object_MT.header, "System.RuntimeType", sizeof(TypeObject), 0 },
    { (void*)type_to_string, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default,
      [3 ... 3 + TYPE_MEMBERINFO_SLOTS - 1] = (void*)type_virtual_unsupported },
};

// Open-addressing cache in .bss — the conservative GC scans the executable's data segments, so
// the Type objects referenced from here stay alive.
#define TYPE_CACHE_SIZE 4096
static struct { const csrt_MethodTable* key; TypeObject* value; } s_types[TYPE_CACHE_SIZE];

// A thread inserting a type claims its slot by compare-and-swapping the key, then publishes the value;
// a reader that finds the key waits for the value. Unsynchronized, two threads asking for a new type at
// once could see a key whose value was still NULL, or create two Type objects for one type
// (typeof(X) != typeof(X)).
TypeObject* csrt_type_from_mt(const csrt_MethodTable* mt) {
    size_t h = ((uintptr_t)mt >> 4) & (TYPE_CACHE_SIZE - 1);
    for (size_t probe = 0; probe < TYPE_CACHE_SIZE;) {
        const csrt_MethodTable* key = __atomic_load_n(&s_types[h].key, __ATOMIC_ACQUIRE);
        if (!key) {
            if (!__atomic_compare_exchange_n(&s_types[h].key, &key, mt, false, __ATOMIC_ACQ_REL, __ATOMIC_ACQUIRE))
                continue;  // another thread claimed the slot: look at it again
            TypeObject* t = csrt_alloc(&csrt_Type_MT.header, sizeof(TypeObject));
            t->target = mt;
            __atomic_store_n(&s_types[h].value, t, __ATOMIC_RELEASE);
            return t;
        }
        if (key == mt) {
            TypeObject* t;
            while (!(t = __atomic_load_n(&s_types[h].value, __ATOMIC_ACQUIRE))) sched_yield();
            return t;
        }
        probe++;
        h = (h + 1) & (TYPE_CACHE_SIZE - 1);
    }
    abort();  // more than 4096 distinct types asked for at runtime
}

TypeObject* csrt_type_of_object(void* obj) {
    if (!obj) csrt_throw_null_reference();
    return csrt_type_from_mt(((csrt_Object*)obj)->mt);
}

static csrt_String* from_utf8(const char* p, size_t n) { return csrt_string_from_utf8_n(p, n); }

static const char* full_name(TypeObject* t) {
    if (!t) { csrt_throw_null_reference(); return ""; }
    return t->target->name ? t->target->name : "";
}

/// Type.ToString() (and Type.FullName of non-generic types): "Program+Nested", "System.Int32[]",
/// "System.Collections.Generic.List`1[System.Int32]".
csrt_String* csrt_type_full_name(TypeObject* t) { const char* n = full_name(t); return from_utf8(n, strlen(n)); }

/// Type.Name: the last '.'/'+' separated component, without generic arguments but with array and
/// pointer suffixes ("System.Collections.Generic.List`1[System.Int32][]" -> "List`1[]").
csrt_String* csrt_type_name(TypeObject* t) {
    const char* n = full_name(t);
    size_t len = strlen(n);
    char stack[256];
    char* buf = len < sizeof stack ? stack : malloc(len + 1);
    if (!buf) { csrt_throw_out_of_memory(NULL); return NULL; }
    size_t out = 0, start = 0;
    for (size_t i = 0; i < len; i++) {
        // A generic argument list: '[' not followed by ']', ',' or '*' (those are array ranks).
        if (n[i] == '[' && n[i + 1] != ']' && n[i + 1] != ',' && n[i + 1] != '*') {
            for (int depth = 0; i < len; i++) {
                if (n[i] == '[') depth++;
                else if (n[i] == ']' && --depth == 0) break;
            }
            continue;
        }
        if (n[i] == '.' || n[i] == '+') start = out + 1;
        buf[out++] = n[i];
    }
    csrt_String* r = from_utf8(buf + start, out - start);
    if (buf != stack) free(buf);
    return r;
}

static const csrt_TypeInfoTable* s_table;  // the program's metadata, registered by the first query
static const csrt_TypeInfo* lookup_info(const csrt_MethodTable* mt);

/// Type.Namespace: the part of the top-level type name before its last '.'; null if none. Generic
/// parameters (only reachable through the metadata) report their declaring type's namespace.
csrt_String* csrt_type_namespace(TypeObject* t) {
    const char* n = full_name(t);
    if (t && __atomic_load_n(&s_table, __ATOMIC_ACQUIRE)) {
        const csrt_TypeInfo* info = lookup_info(t->target);
        if (info && (info->flags & CSRT_TI_GENERIC_PARAMETER))
            return info->namespace_name ? csrt_string_from_utf8(info->namespace_name) : NULL;
    }
    const char* end = NULL;
    for (const char* p = n; *p && *p != '+' && *p != '['; p++)
        if (*p == '.') end = p;
    return end ? from_utf8(n, (size_t)(end - n)) : NULL;
}

static csrt_String* type_to_string(void* o) { return csrt_type_full_name(o); }

static bool mt_is_assignable(const csrt_MethodTable* mt, const csrt_MethodTable* target) {
    for (const csrt_MethodTable* m = mt; m; m = m->base)
        if (m == target) return true;
    for (uint32_t i = 0; i < mt->interface_count; i++)
        if (mt->interfaces[i].interface_type == target) return true;
    return target == &csrt_Object_MT.header;
}

/// CoreIntrinsics.IsInstanceOfType (System.Text.Json): obj's class or one of its bases, or an
/// interface it implements. Needs no metadata table.
bool csrt_type_is_instance(TypeObject* t, void* obj) {
    if (!t) { csrt_throw_null_reference(); return false; }
    return obj && mt_is_assignable(((csrt_Object*)obj)->mt, t->target);
}

/// CoreIntrinsics.IsTypeAssignableTo: classes and interfaces (no variance, no value-type rules).
bool csrt_type_is_assignable(TypeObject* from, TypeObject* to) {
    if (!from || !to) { csrt_throw_null_reference(); return false; }
    return mt_is_assignable(from->target, to->target);
}

/// CoreIntrinsics.IsInterfaceTypeObject.
bool csrt_type_is_interface(TypeObject* t) {
    if (!t) { csrt_throw_null_reference(); return false; }
    return (t->target->flags & CSRT_MT_INTERFACE) != 0;
}

// ============================================================================================
// Type metadata: System.Type reflection over the compile-time tables
// ============================================================================================

static const csrt_MethodTable mt_not_supported = { NULL, "System.NotSupportedException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_invalid_operation = { NULL, "System.InvalidOperationException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_argument = { NULL, "System.ArgumentException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_argument_null = { NULL, "System.ArgumentNullException", 0, 0, NULL, 0, 0 };

// mt -> table entry, open addressing over pointers into the constant table. A program has at most
// one table of each kind, so each index is built once.
typedef struct { const void** slots; size_t mask; } Index;

static void build_index(Index* index, const void* entries, int64_t count, size_t stride) {
    if (__atomic_load_n(&index->slots, __ATOMIC_ACQUIRE)) return;
    size_t size = 16;
    while (size < (size_t)count * 2) size <<= 1;
    const void** slots = calloc(size, sizeof *slots);
    if (!slots) { csrt_throw_out_of_memory(NULL); return; }
    for (int64_t i = 0; i < count; i++) {
        const void* entry = (const char*)entries + (size_t)i * stride;
        const csrt_MethodTable* key = *(const csrt_MethodTable* const*)entry;
        size_t h = ((uintptr_t)key >> 4) & (size - 1);
        while (slots[h]) h = (h + 1) & (size - 1);
        slots[h] = entry;
    }
    // Racing threads build identical indexes; the first one published wins.
    index->mask = size - 1;
    const void** expected = NULL;
    if (!__atomic_compare_exchange_n(&index->slots, &expected, slots, false, __ATOMIC_ACQ_REL, __ATOMIC_ACQUIRE))
        free(slots);
}

static const void* index_find(const Index* index, const csrt_MethodTable* mt) {
    const void** slots = __atomic_load_n(&index->slots, __ATOMIC_ACQUIRE);
    if (!slots) return NULL;
    for (size_t h = ((uintptr_t)mt >> 4) & index->mask;; h = (h + 1) & index->mask) {
        const void* entry = slots[h];
        if (!entry || *(const csrt_MethodTable* const*)entry == mt) return entry;
    }
}

static Index s_info_index;
static Index s_name_index;

static const csrt_TypeInfo* synthesized_info_for(const csrt_MethodTable* mt); // below, with CreateInstance

static const csrt_TypeInfo* lookup_info(const csrt_MethodTable* mt) {
    const csrt_TypeInfoTable* table = __atomic_load_n(&s_table, __ATOMIC_ACQUIRE);
    if (!table) return NULL;
    build_index(&s_info_index, table->entries, table->count, sizeof(csrt_TypeInfo));
    const csrt_TypeInfo* found = index_find(&s_info_index, mt);
    if (found) return found;
    return synthesized_info_for(mt);
}

static void throw_utf8(const csrt_MethodTable* type, const char* message) { csrt_throw(type, csrt_string_from_utf8(message)); }

static void* type_virtual_unsupported(void) {
    throw_utf8(&mt_not_supported, "Calling a MemberInfo member on a System.Type through a MemberInfo-typed reference is not supported by csharpc yet.");
    return NULL;
}

/// A type or query the compile-time metadata doesn't describe: an explicit NotSupportedException
/// rather than a wrong answer.
static void throw_unsupported(const csrt_MethodTable* mt, const char* what) {
    char message[512];
    snprintf(message, sizeof message, "%s of type '%s' is not supported by csharpc yet.", what, mt && mt->name ? mt->name : "?");
    throw_utf8(&mt_not_supported, message);
}

/// NULL after throwing NotSupportedException.
static const csrt_TypeInfo* info_for(const csrt_MethodTable* mt) {
    const csrt_TypeInfo* info = lookup_info(mt);
    if (!info) throw_unsupported(mt, "Reflection");
    return info;
}

/// The metadata of a Type object (NULL after throwing); registers the program's table on first use.
static const csrt_TypeInfo* info_of(TypeObject* t, const csrt_TypeInfoTable* table) {
    if (!t) { csrt_throw_null_reference(); return NULL; }
    if (!__atomic_load_n(&s_table, __ATOMIC_ACQUIRE)) __atomic_store_n(&s_table, table, __ATOMIC_RELEASE);
    return info_for(t->target);
}

static TypeObject* type_or_null(const csrt_MethodTable* mt) { return mt ? csrt_type_from_mt(mt) : NULL; }

/// Boolean Type properties (IsClass, IsGenericType, ...): one bit each, computed at compile time.
bool csrt_type_flag(TypeObject* t, const csrt_TypeInfoTable* table, uint64_t mask) {
    const csrt_TypeInfo* info = info_of(t, table);
    return info && (info->flags & mask) != 0;
}

/// NULL (after throwing) when the base type and interfaces of `info` are not described.
static const csrt_TypeInfo* with_hierarchy(const csrt_TypeInfo* info, const char* what) {
    if (info && (info->flags & CSRT_TI_NO_HIERARCHY)) { throw_unsupported(info->mt, what); return NULL; }
    return info;
}

/// Type.BaseType.
TypeObject* csrt_type_base(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = with_hierarchy(info_of(t, table), "Type.BaseType");
    return info ? type_or_null(info->base) : NULL;
}

/// Type.GetElementType().
TypeObject* csrt_type_element(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    return info && !(info->flags & CSRT_TI_ENUM) ? type_or_null(info->element) : NULL;
}

/// Type.GetEnumUnderlyingType().
TypeObject* csrt_type_enum_underlying(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return NULL;
    if (!(info->flags & CSRT_TI_ENUM)) { throw_utf8(&mt_argument, "Type provided must be an Enum. (Parameter 'enumType')"); return NULL; }
    return type_or_null(info->element);
}

/// Type.DeclaringType.
TypeObject* csrt_type_declaring(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    return info ? type_or_null(info->declaring) : NULL;
}

/// Type.GetGenericTypeDefinition().
TypeObject* csrt_type_generic_definition(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return NULL;
    if (!(info->flags & CSRT_TI_GENERIC_TYPE)) {
        throw_utf8(&mt_invalid_operation, "This operation is only valid on generic types.");
        return NULL;
    }
    return csrt_type_from_mt(info->definition);
}

/// Type.GetGenericArguments(), or Type.GenericTypeArguments (constructed_only: empty for generic
/// type definitions). A new array each call, as in .NET; the caller tags it as System.Type[].
csrt_Array* csrt_type_generic_arguments(TypeObject* t, const csrt_TypeInfoTable* table, bool constructed_only) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return NULL;
    uint32_t count = constructed_only && !(info->flags & CSRT_TI_CONSTRUCTED_GENERIC) ? 0 : info->generic_count;
    csrt_Array* array = csrt_new_array(sizeof(void*), count);
    for (uint32_t i = 0; i < count; i++) ((TypeObject**)array->data)[i] = csrt_type_from_mt(info->generic_args[i]);
    return array;
}

/// Type.GetInterfaces(): all implemented interfaces described for this Type (empty when open generics
/// omit the list). A new array each call; the caller tags it as System.Type[].
csrt_Array* csrt_type_interfaces(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return NULL;
    if (info->flags & CSRT_TI_NO_INTERFACES) {
        throw_unsupported(info->mt, "Type.GetInterfaces()");
        return NULL;
    }
    uint32_t count = info->interface_count;
    csrt_Array* array = csrt_new_array(sizeof(void*), count);
    for (uint32_t i = 0; i < count; i++) ((TypeObject**)array->data)[i] = csrt_type_from_mt(info->interfaces[i]);
    return array;
}

static uint16_t fold_ascii(uint16_t c) { return c >= 'A' && c <= 'Z' ? (uint16_t)(c + 32) : c; }

/// Does the UTF-8 type name `n` equal the UTF-16 `s` (ASCII case folded when `ignore_case`)?
static bool name_equals(const char* n, const uint16_t* s, int32_t length, bool ignore_case) {
    int32_t i = 0;
    const unsigned char* p = (const unsigned char*)n;
    while (*p) {
        uint32_t cp;
        if (*p < 0x80) cp = *p++;
        else if ((*p & 0xE0) == 0xC0 && p[1]) { cp = ((uint32_t)(p[0] & 0x1F) << 6) | (p[1] & 0x3F); p += 2; }
        else if ((*p & 0xF0) == 0xE0 && p[1] && p[2]) { cp = ((uint32_t)(p[0] & 0x0F) << 12) | ((uint32_t)(p[1] & 0x3F) << 6) | (p[2] & 0x3F); p += 3; }
        else if (p[1] && p[2] && p[3]) { cp = ((uint32_t)(p[0] & 0x07) << 18) | ((uint32_t)(p[1] & 0x3F) << 12) | ((uint32_t)(p[2] & 0x3F) << 6) | (p[3] & 0x3F); p += 4; }
        else return false;
        if (cp >= 0x10000) {
            if (i + 1 >= length) return false;
            uint32_t hi = 0xD800 + ((cp - 0x10000) >> 10), lo = 0xDC00 + ((cp - 0x10000) & 0x3FF);
            if (s[i] != hi || s[i + 1] != lo) return false;
            i += 2;
            continue;
        }
        if (i >= length) return false;
        uint16_t c = (uint16_t)cp, d = s[i++];
        if (ignore_case ? fold_ascii(c) != fold_ascii(d) : c != d) return false;
    }
    return i == length;
}

/// Type.GetType(name) once the corelib normalized the name (no assembly qualification, generic arguments
/// in ToString form): the program's type of that full name, or NULL — a type csharpc didn't keep is
/// missing, as under NativeAOT.
TypeObject* csrt_type_find(csrt_String* name, bool ignore_case, const csrt_TypeInfoTable* table) {
    if (!name) { csrt_throw_null_reference(); return NULL; }
    if (!__atomic_load_n(&s_table, __ATOMIC_ACQUIRE)) __atomic_store_n(&s_table, table, __ATOMIC_RELEASE);
    for (int64_t i = 0; i < table->count; i++) {
        const csrt_TypeInfo* info = &table->entries[i];
        if (!info->mt || !info->mt->name || (info->flags & CSRT_TI_GENERIC_PARAMETER)) continue;
        if (name_equals(info->mt->name, name->chars, name->length, ignore_case)) return csrt_type_from_mt(info->mt);
    }
    return NULL;
}

/// Type.GetArrayRank().
int32_t csrt_type_array_rank(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return 0;
    if (!(info->flags & CSRT_TI_ARRAY)) { throw_utf8(&mt_argument, "Must be an array type."); return 0; }
    return (int32_t)info->rank;
}

/// Type.GenericParameterPosition.
int32_t csrt_type_generic_parameter_position(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return 0;
    if (!(info->flags & CSRT_TI_GENERIC_PARAMETER)) {
        throw_utf8(&mt_invalid_operation, "Method may only be called on a Type for which Type.IsGenericParameter is true.");
        return 0;
    }
    return (int32_t)info->rank;
}

/// Nullable.GetUnderlyingType(nullableType).
TypeObject* csrt_type_nullable_underlying(TypeObject* t, const csrt_TypeInfoTable* table) {
    if (!t) { throw_utf8(&mt_argument_null, "Value cannot be null. (Parameter 'nullableType')"); return NULL; }
    const csrt_TypeInfo* info = info_of(t, table);
    return info && (info->flags & CSRT_TI_NULLABLE) && (info->flags & CSRT_TI_CONSTRUCTED_GENERIC)
        ? csrt_type_from_mt(info->generic_args[0]) : NULL;
}

// Assignability. Each helper returns false after throwing (wasm32 continues past throws).

static bool assignable(const csrt_TypeInfo* to, const csrt_TypeInfo* from);

static uint32_t element_class(const csrt_TypeInfo* info) { return (uint32_t)(info->flags >> CSRT_TI_ELEMENT_CLASS_SHIFT) & 15; }

/// Array element compatibility (the CLR's array covariance): the same type, primitives sharing a
/// storage class (int[] and uint[], enums and their underlying type), or assignable reference types.
static bool element_compatible(const csrt_MethodTable* to_mt, const csrt_MethodTable* from_mt) {
    if (to_mt == from_mt) return true;
    const csrt_TypeInfo* to = info_for(to_mt);
    const csrt_TypeInfo* from = to ? info_for(from_mt) : NULL;
    if (!from) return false;
    if (element_class(to) && element_class(to) == element_class(from)) return true;
    return (from->flags & CSRT_TI_REFERENCE_TYPE) && assignable(to, from);
}

/// `from` converts to `to` through generic variance: the same definition, and every argument
/// identical or related as its parameter's variance allows (reference types only).
static bool variant_compatible(const csrt_TypeInfo* to, const csrt_TypeInfo* from) {
    if (!to->variance || !(to->flags & CSRT_TI_CONSTRUCTED_GENERIC) || to->definition != from->definition
        || !(from->flags & CSRT_TI_CONSTRUCTED_GENERIC))
        return false;
    for (uint32_t k = 0; k < to->generic_count; k++) {
        if (to->generic_args[k] == from->generic_args[k]) continue;
        const csrt_TypeInfo* a = info_for(to->generic_args[k]);
        const csrt_TypeInfo* b = a ? info_for(from->generic_args[k]) : NULL;
        if (!b) return false;
        uint32_t v = (to->variance >> (2 * k)) & 3;
        if (v == 1 && (b->flags & CSRT_TI_REFERENCE_TYPE) && assignable(a, b)) continue;
        if (v == 2 && (a->flags & CSRT_TI_REFERENCE_TYPE) && assignable(b, a)) continue;
        return false;
    }
    return true;
}

/// to.IsAssignableFrom(from), with the CLR's rules (RuntimeType.IsAssignableFrom).
static bool assignable(const csrt_TypeInfo* to, const csrt_TypeInfo* from) {
    if (to == from) return true;
    if ((to->flags | from->flags) & CSRT_TI_GENERIC_PARAMETER) {
        throw_unsupported(from->mt, "Type.IsAssignableFrom with generic parameters");
        return false;
    }
    if (to->flags & CSRT_TI_GENERIC_DEFINITION) return false;  // nothing derives from an open definition
    if (from->flags & CSRT_TI_POINTER) return false;
    if (to->mt == &csrt_Object_MT.header) return true;
    // typeof(int?).IsAssignableFrom(typeof(int)): boxing an int gives something an int? unboxes.
    if ((to->flags & CSRT_TI_NULLABLE) && (to->flags & CSRT_TI_CONSTRUCTED_GENERIC)) return from->mt == to->generic_args[0];
    if (to->flags & CSRT_TI_INTERFACE) {
        if (variant_compatible(to, from)) return true;
        if (from->flags & CSRT_TI_NO_INTERFACES) { throw_unsupported(from->mt, "Type.IsAssignableFrom"); return false; }
        for (uint32_t i = 0; i < from->interface_count; i++) {
            if (from->interfaces[i] == to->mt) return true;
            const csrt_TypeInfo* itf = info_for(from->interfaces[i]);
            if (!itf) return false;
            if (variant_compatible(to, itf)) return true;
        }
        // T[] implements IList<U> & co. for every U its elements are compatible with.
        if ((from->flags & CSRT_TI_SZARRAY) && (to->flags & CSRT_TI_ARRAY_INTERFACE))
            return element_compatible(to->generic_args[0], from->element);
        return false;
    }
    if (!with_hierarchy(from, "Type.IsAssignableFrom")) return false;
    for (const csrt_MethodTable* b = from->base; b;) {
        if (b == to->mt) return true;
        const csrt_TypeInfo* info = info_for(b);
        if (!info) return false;
        b = info->base;
    }
    if ((to->flags & CSRT_TI_ARRAY) && (from->flags & CSRT_TI_ARRAY) && to->rank == from->rank
        && (to->flags & CSRT_TI_SZARRAY) == (from->flags & CSRT_TI_SZARRAY))
        return element_compatible(to->element, from->element);
    return variant_compatible(to, from);  // delegates: Func<string> as Func<object>
}

/// Type.IsAssignableFrom(c): false for null.
bool csrt_type_is_assignable_from(TypeObject* to, TypeObject* from, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* target = info_of(to, table);
    if (!target || !from) return false;
    const csrt_TypeInfo* source = info_of(from, table);
    return source && assignable(target, source);
}

/// Type.IsAssignableTo(targetType): false for null.
bool csrt_type_is_assignable_to(TypeObject* from, TypeObject* to, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* source = info_of(from, table);
    if (!source || !to) return false;
    const csrt_TypeInfo* target = info_of(to, table);
    return target && assignable(target, source);
}

/// Type.IsInstanceOfType(o): false for null.
bool csrt_type_is_instance_of(TypeObject* t, void* obj, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* target = info_of(t, table);
    if (!target || !obj) return false;
    const csrt_TypeInfo* source = info_for(((csrt_Object*)obj)->mt);
    return source && assignable(target, source);
}

/// Type.IsSubclassOf(c): a proper base class (interfaces never count).
bool csrt_type_is_subclass_of(TypeObject* t, TypeObject* c, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return false;
    if (!c) { throw_utf8(&mt_argument_null, "Value cannot be null. (Parameter 'type')"); return false; }
    if (c->target == t->target || !with_hierarchy(info, "Type.IsSubclassOf")) return false;
    for (const csrt_MethodTable* b = info->base; b;) {
        if (b == c->target) return true;
        const csrt_TypeInfo* base = info_for(b);
        if (!base) return false;
        b = base->base;
    }
    return false;
}

/// NULL after throwing when csharpc doesn't know the assembly.
static const char* assembly_of(const csrt_TypeInfo* info) {
    if (!info->assembly) throw_unsupported(info->mt, "Type.Assembly");
    return info->assembly;
}

/// The full name of Type.Assembly ("System.Private.CoreLib, Version=11.0.0.0, ..."); the corelib
/// turns it into one Assembly object per name.
csrt_String* csrt_type_assembly_name(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    const char* assembly = info ? assembly_of(info) : NULL;
    return assembly ? csrt_string_from_utf8(assembly) : NULL;
}

/// Type.FullName: the method table's name, unless the (small) name table says otherwise — generic
/// instantiations list assembly-qualified arguments, generic parameters have none.
/// NULL for a null FullName (and after throwing).
static const char* full_name_of(TypeObject* t, const csrt_TypeNameTable* names, const char* what) {
    build_index(&s_name_index, names->entries, names->count, sizeof(csrt_TypeNameEntry));
    const csrt_TypeNameEntry* entry = index_find(&s_name_index, t->target);
    if (!entry) return full_name(t);
    if (entry->unknown) { throw_unsupported(t->target, what); return NULL; }
    return entry->full_name;
}

csrt_String* csrt_type_full_name_of(TypeObject* t, const csrt_TypeNameTable* names) {
    if (!t) { csrt_throw_null_reference(); return NULL; }
    const char* n = full_name_of(t, names, "Type.FullName");
    return n ? csrt_string_from_utf8(n) : NULL;
}

/// Type.AssemblyQualifiedName: "FullName, Assembly"; null when FullName is.
csrt_String* csrt_type_assembly_qualified_name(TypeObject* t, const csrt_TypeInfoTable* table, const csrt_TypeNameTable* names) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return NULL;
    const char* name = full_name_of(t, names, "Type.AssemblyQualifiedName");
    if (!name) return NULL;
    const char* assembly = assembly_of(info);
    if (!assembly) return NULL;
    size_t a = strlen(name), b = strlen(assembly);
    char* buf = malloc(a + b + 3);
    if (!buf) { csrt_throw_out_of_memory(NULL); return NULL; }
    memcpy(buf, name, a);
    memcpy(buf + a, ", ", 2);
    memcpy(buf + a + 2, assembly, b + 1);
    csrt_String* r = csrt_string_from_utf8(buf);
    free(buf);
    return r;
}

// ============================================================================================
// Reflection beyond System.Type's own answers (docs/adr/0018-reflection-metadata.md): generic
// instantiation and arrays over the program's method tables, and the member metadata tables.
// ============================================================================================

static const csrt_MethodTable mt_index_out_of_range = { NULL, "System.IndexOutOfRangeException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_invalid_cast = { NULL, "System.InvalidCastException", 0, 0, NULL, 0, 0 };

static void throw_formatted(const csrt_MethodTable* type, const char* format, const char* argument) {
    char message[768];
    snprintf(message, sizeof message, format, argument);
    throw_utf8(type, message);
}

/// The method table of a Type object (corelib: CoreIntrinsics.TypeHandle).
const csrt_MethodTable* csrt_type_handle(TypeObject* t) {
    if (!t) { csrt_throw_null_reference(); return NULL; }
    return t->target;
}

/// Type.MakeGenericType(typeArguments): the closed instantiation among the program's types. csharpc
/// compiles generic code per instantiation, so one the program never creates has no code (NativeAOT's
/// limit for value types; csharpc's for every type): NotSupportedException rather than a wrong answer.
TypeObject* csrt_type_make_generic(TypeObject* t, csrt_Array* arguments, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* definition = info_of(t, table);
    if (!definition) return NULL;
    if (!(definition->flags & CSRT_TI_GENERIC_DEFINITION)) {
        throw_formatted(&mt_invalid_operation, "%s is not a GenericTypeDefinition. MakeGenericType may only be called on a type for which Type.IsGenericTypeDefinition is true.", full_name(t));
        return NULL;
    }
    if (!arguments) { throw_utf8(&mt_argument_null, "Value cannot be null. (Parameter 'typeArguments')"); return NULL; }
    TypeObject** args = (TypeObject**)arguments->data;
    for (int64_t i = 0; i < arguments->length; i++)
        if (!args[i]) { throw_utf8(&mt_argument_null, "Value cannot be null."); return NULL; }
    if (arguments->length != (int64_t)definition->generic_count) {
        throw_utf8(&mt_argument, "The number of generic arguments provided doesn't equal the arity of the generic type definition. (Parameter 'typeArguments')");
        return NULL;
    }
    for (int64_t e = 0; e < table->count; e++) {
        const csrt_TypeInfo* candidate = &table->entries[e];
        if (candidate->definition != definition->mt || !(candidate->flags & CSRT_TI_CONSTRUCTED_GENERIC)
            || candidate->generic_count != definition->generic_count)
            continue;
        bool same = true;
        for (uint32_t k = 0; k < candidate->generic_count && same; k++) same = candidate->generic_args[k] == args[k]->target;
        if (same) return csrt_type_from_mt(candidate->mt);
    }
    char names[512];
    size_t used = 0;
    names[0] = 0;
    for (int64_t i = 0; i < arguments->length && used < sizeof names - 1; i++)
        used += (size_t)snprintf(names + used, sizeof names - used, i ? ",%s" : "%s", full_name(args[i]));
    char message[1024];
    snprintf(message, sizeof message, "MakeGenericType of '%s' with [%s] is not supported by csharpc yet: csharpc compiles only the generic instantiations the program creates.",
             full_name(t), names);
    throw_utf8(&mt_not_supported, message);
    return NULL;
}

/// Synthesized T[] TypeInfo entries for Array.CreateInstance / MakeArrayType when the program never
/// named T[] at compile time (DI / configuration binders). Compile-time tables stay preferred.
typedef struct { csrt_ArrayMethodTable array_mt; csrt_TypeInfo info; char* name; } SynthesizedArray;
static SynthesizedArray** s_synth_arrays;
static size_t s_synth_arrays_count, s_synth_arrays_cap;
static pthread_mutex_t s_synth_arrays_mu = PTHREAD_MUTEX_INITIALIZER;

static const csrt_TypeInfo* synthesized_info_for(const csrt_MethodTable* mt) {
    pthread_mutex_lock(&s_synth_arrays_mu);
    const csrt_TypeInfo* found = NULL;
    for (size_t i = 0; i < s_synth_arrays_count; i++)
        if (&s_synth_arrays[i]->array_mt.header == mt) { found = &s_synth_arrays[i]->info; break; }
    pthread_mutex_unlock(&s_synth_arrays_mu);
    return found;
}

static const csrt_TypeInfo* synthesized_array_of_locked(const csrt_MethodTable* element) {
    for (size_t i = 0; i < s_synth_arrays_count; i++)
        if (s_synth_arrays[i]->info.element == element) return &s_synth_arrays[i]->info;
    return NULL;
}

static const csrt_TypeInfo* synthesized_array_of(const csrt_MethodTable* element) {
    pthread_mutex_lock(&s_synth_arrays_mu);
    const csrt_TypeInfo* found = synthesized_array_of_locked(element);
    pthread_mutex_unlock(&s_synth_arrays_mu);
    return found;
}

/// The program's method table for `element[]` (NULL if there is none), including synthesized ones.
static const csrt_TypeInfo* array_info_of(const csrt_MethodTable* element, const csrt_TypeInfoTable* table) {
    for (int64_t e = 0; e < table->count; e++) {
        const csrt_TypeInfo* candidate = &table->entries[e];
        if ((candidate->flags & CSRT_TI_SZARRAY) && candidate->element == element) return candidate;
    }
    return synthesized_array_of(element);
}

/// Allocate a T[] method table + TypeInfo for a reference-type element the program described but
/// never allocated as T[]. Locked: binders create many distinct element types concurrently.
static const csrt_TypeInfo* synthesize_szarray(const csrt_MethodTable* element) {
    pthread_mutex_lock(&s_synth_arrays_mu);
    const csrt_TypeInfo* existing = synthesized_array_of_locked(element);
    if (existing) { pthread_mutex_unlock(&s_synth_arrays_mu); return existing; }

    const char* ename = element->name ? element->name : "?";
    size_t n = strlen(ename);
    char* name = malloc(n + 3);
    if (!name) { pthread_mutex_unlock(&s_synth_arrays_mu); csrt_throw_out_of_memory(NULL); return NULL; }
    memcpy(name, ename, n);
    name[n] = '['; name[n + 1] = ']'; name[n + 2] = 0;

    SynthesizedArray* syn = calloc(1, sizeof *syn);
    if (!syn) { free(name); pthread_mutex_unlock(&s_synth_arrays_mu); csrt_throw_out_of_memory(NULL); return NULL; }
    syn->name = name;
    syn->array_mt.header.base = &csrt_Array_MT.header;
    syn->array_mt.header.name = name;
    syn->array_mt.header.instance_size = 0;
    // Reference elements, but no element table: the Type's method table may not be the one its instances
    // carry (library types), so stores into these arrays aren't type-checked (csrt_array_store_check).
    syn->array_mt.header.flags = CSRT_MT_REFERENCE_ELEMENTS;
    syn->array_mt.element = NULL;
    syn->array_mt.header.interfaces = NULL;
    syn->array_mt.header.interface_count = 0;
    syn->array_mt.header.reserved = (uint32_t)sizeof(void*);
    syn->array_mt.vtable[0] = (void*)csrt_object_to_string_default;
    syn->array_mt.vtable[1] = (void*)csrt_object_equals_default;
    syn->array_mt.vtable[2] = (void*)csrt_object_get_hash_code_default;
    // Match ModuleCodeGen.TypeInfo array flags (Class|Public|Sealed|Array|HasElementType|ReferenceType|SZArray|Visible).
    syn->info.mt = &syn->array_mt.header;
    syn->info.base = &csrt_Array_MT.header;
    syn->info.element = element;
    syn->info.flags = CSRT_TI_CLASS | CSRT_TI_PUBLIC | CSRT_TI_SEALED | CSRT_TI_ARRAY | CSRT_TI_HAS_ELEMENT_TYPE
        | CSRT_TI_SZARRAY | CSRT_TI_VISIBLE | CSRT_TI_REFERENCE_TYPE;
    syn->info.rank = 1;

    if (s_synth_arrays_count == s_synth_arrays_cap) {
        size_t ncap = s_synth_arrays_cap ? s_synth_arrays_cap * 2 : 8;
        SynthesizedArray** grown = realloc(s_synth_arrays, ncap * sizeof *grown);
        if (!grown) {
            free(name); free(syn);
            pthread_mutex_unlock(&s_synth_arrays_mu);
            csrt_throw_out_of_memory(NULL);
            return NULL;
        }
        s_synth_arrays = grown;
        s_synth_arrays_cap = ncap;
    }
    s_synth_arrays[s_synth_arrays_count++] = syn;
    pthread_mutex_unlock(&s_synth_arrays_mu);
    return &syn->info;
}

static const csrt_TypeInfo* ensure_szarray(const csrt_MethodTable* element, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* array = array_info_of(element, table);
    return array ? array : synthesize_szarray(element);
}

/// Type.MakeArrayType().
TypeObject* csrt_type_make_array(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return NULL;
    // Value-type arrays need the element layout at allocation time; synthesizing only covers refs.
    if (!(info->flags & CSRT_TI_REFERENCE_TYPE)) {
        const csrt_TypeInfo* array = array_info_of(t->target, table);
        if (!array) { throw_formatted(&mt_not_supported, "MakeArrayType of '%s' is not supported by csharpc yet: the program has no arrays of it.", full_name(t)); return NULL; }
        return csrt_type_from_mt(array->mt);
    }
    const csrt_TypeInfo* array = ensure_szarray(t->target, table);
    if (!array) return NULL;
    return csrt_type_from_mt(array->mt);
}

/// Array.CreateInstance(elementType, length) once the corelib checked the arguments: arrays of reference
/// types (their elements are pointers; csharpc's value-type arrays need the element layout).
csrt_Array* csrt_array_create_instance(TypeObject* element, int64_t length, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(element, table);
    if (!info) return NULL;
    if (!(info->flags & CSRT_TI_REFERENCE_TYPE)) {
        throw_formatted(&mt_not_supported, "Array.CreateInstance of value type '%s' is not supported by csharpc yet.", full_name(element));
        return NULL;
    }
    const csrt_TypeInfo* array = ensure_szarray(element->target, table);
    if (!array) return NULL;
    csrt_Array* result = csrt_new_array(sizeof(void*), length);
    result->mt = array->mt;
    return result;
}

/// Array.SetValue(value, index) on a one-dimensional array of a reference type.
void csrt_array_set_value(csrt_Array* array, void* value, int64_t index, const csrt_TypeInfoTable* table) {
    if (!array) { csrt_throw_null_reference(); return; }
    if (!__atomic_load_n(&s_table, __ATOMIC_ACQUIRE)) __atomic_store_n(&s_table, table, __ATOMIC_RELEASE);
    const csrt_TypeInfo* info = info_for(array->mt);
    if (!info) return;
    if (!(info->flags & CSRT_TI_SZARRAY)) { throw_utf8(&mt_argument, "Only single dimension arrays are supported here."); return; }
    const csrt_TypeInfo* element = info_for(info->element);
    if (!element) return;
    if (!(element->flags & CSRT_TI_REFERENCE_TYPE)) {
        throw_formatted(&mt_not_supported, "Array.SetValue on an array of value type '%s' is not supported by csharpc yet.", info->element->name);
        return;
    }
    if ((uint64_t)index >= (uint64_t)array->length) { throw_utf8(&mt_index_out_of_range, "Index was outside the bounds of the array."); return; }
    if (value) {
        const csrt_TypeInfo* from = info_for(((csrt_Object*)value)->mt);
        if (!from) return;
        if (!assignable(element, from)) { throw_utf8(&mt_invalid_cast, "Object cannot be stored in an array of this type."); return; }
    }
    CSRT_STORE_REF(((void**)array->data)[index], value);
}

/// Array.GetValue(index) on a one-dimensional array of a reference type (synthesized T[] included).
void* csrt_array_get_value(csrt_Array* array, int64_t index, const csrt_TypeInfoTable* table) {
    if (!array) { csrt_throw_null_reference(); return NULL; }
    if (!__atomic_load_n(&s_table, __ATOMIC_ACQUIRE)) __atomic_store_n(&s_table, table, __ATOMIC_RELEASE);
    const csrt_TypeInfo* info = info_for(array->mt);
    if (!info) return NULL;
    if (!(info->flags & CSRT_TI_SZARRAY)) { throw_utf8(&mt_argument, "Only single dimension arrays are supported here."); return NULL; }
    const csrt_TypeInfo* element = info_for(info->element);
    if (!element) return NULL;
    if (!(element->flags & CSRT_TI_REFERENCE_TYPE)) {
        throw_formatted(&mt_not_supported, "Array.GetValue on an array of value type '%s' is not supported by csharpc yet.", info->element->name);
        return NULL;
    }
    if ((uint64_t)index >= (uint64_t)array->length) { throw_utf8(&mt_index_out_of_range, "Index was outside the bounds of the array."); return NULL; }
    return ((void**)array->data)[index];
}

/// Type.GenericParameterAttributes (kept in the variance field of a generic parameter's entry).
int32_t csrt_type_generic_parameter_attributes(TypeObject* t, const csrt_TypeInfoTable* table) {
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return 0;
    if (!(info->flags & CSRT_TI_GENERIC_PARAMETER)) {
        throw_utf8(&mt_invalid_operation, "Method may only be called on a Type for which Type.IsGenericParameter is true.");
        return 0;
    }
    return (int32_t)info->variance;
}

/// Payload width of an enum or primitive integral (element class, csrt.h); 0 if not integral.
static size_t integral_width(uint32_t element_class) {
    switch (element_class) { case 1: return 1; case 2: return 2; case 3: return 4; case 4: return 8; case 5: return sizeof(void*); default: return 0; }
}

/// Enum.ToObject(enumType, value): a box of the enum holding the integral (or enum) value, truncated
/// to the enum's width as .NET does.
void* csrt_enum_to_object(TypeObject* t, void* value, const csrt_TypeInfoTable* table) {
    if (!t) { throw_utf8(&mt_argument_null, "Value cannot be null. (Parameter 'enumType')"); return NULL; }
    const csrt_TypeInfo* target = info_of(t, table);
    if (!target) return NULL;
    if (!(target->flags & CSRT_TI_ENUM)) { throw_utf8(&mt_argument, "Type provided must be an Enum. (Parameter 'enumType')"); return NULL; }
    if (!value) { throw_utf8(&mt_argument_null, "Value cannot be null. (Parameter 'value')"); return NULL; }
    const csrt_TypeInfo* source = info_for(((csrt_Object*)value)->mt);
    if (!source) return NULL;
    size_t from = integral_width(element_class(source)), to = integral_width(element_class(target));
    if (!from || !(source->flags & (CSRT_TI_PRIMITIVE | CSRT_TI_ENUM)) || ((source->flags & CSRT_TI_PRIMITIVE) && ((csrt_Object*)value)->mt == &csrt_Boxed_Char_MT.header)) {
        throw_utf8(&mt_argument, "The value passed in must be an enum base or an underlying type for an enum, such as an Int32. (Parameter 'value')");
        return NULL;
    }
    uint64_t bits = 0;
    memcpy(&bits, (char*)value + 8, from);  // little-endian targets: the low bytes
    // Sign-extend a signed source (boxed Int*/SByte, or a signed enum's own payload) before truncating.
    const csrt_MethodTable* smt = ((csrt_Object*)value)->mt;
    bool signed_source = smt == &csrt_Boxed_SByte_MT.header || smt == &csrt_Boxed_Int16_MT.header || smt == &csrt_Boxed_Int32_MT.header
        || smt == &csrt_Boxed_Int64_MT.header || smt == &csrt_Boxed_IntPtr_MT.header;
    if (signed_source && from < 8 && (bits >> (8 * from - 1)) & 1) bits |= ~0ull << (8 * from);
    void* box = csrt_alloc(t->target, (int64_t)(8 + to));
    memcpy((char*)box + 8, &bits, to);
    return box;
}

/// RuntimeHelpers.GetUninitializedObject(type): a zeroed instance (or box) without running a constructor.
void* csrt_type_uninitialized_object(TypeObject* t, const csrt_TypeInfoTable* table) {
    if (!t) { throw_utf8(&mt_argument_null, "Value cannot be null. (Parameter 'type')"); return NULL; }
    const csrt_TypeInfo* info = info_of(t, table);
    if (!info) return NULL;
    if (info->flags & (CSRT_TI_ARRAY | CSRT_TI_POINTER | CSRT_TI_BYREF_LIKE | CSRT_TI_CONTAINS_GENERIC_PARAMETERS)
        || t->target == &csrt_String_MT.header) {
        throw_formatted(&mt_argument, "Type '%s' cannot be created without a constructor.", full_name(t));
        return NULL;
    }
    static const csrt_MethodTable mt_member_access = { NULL, "System.MemberAccessException", 0, 0, NULL, 0, 0 };
    if (info->flags & (CSRT_TI_ABSTRACT | CSRT_TI_INTERFACE)) { throw_utf8(&mt_member_access, "Cannot create an abstract class."); return NULL; }
    if (t->target->instance_size == 0) {
        throw_formatted(&mt_not_supported, "RuntimeHelpers.GetUninitializedObject of '%s' is not supported by csharpc yet: the program has no instances of it.", full_name(t));
        return NULL;
    }
    return csrt_alloc(t->target, t->target->instance_size);
}

static Index s_reflection_index;
static Index s_delegate_index;

/// The member metadata of a type (NULL: none was generated).
const csrt_ReflectionInfo* csrt_reflection_info(TypeObject* t, const csrt_ReflectionTable* table) {
    if (!t) { csrt_throw_null_reference(); return NULL; }
    build_index(&s_reflection_index, table->entries, table->count, sizeof(csrt_ReflectionInfo));
    return index_find(&s_reflection_index, t->target);
}

/// The method record of a delegate's function (NULL: not described).
const csrt_MethodRecord* csrt_reflection_delegate_method(const void* fn, const csrt_DelegateMethodTable* table) {
    build_index(&s_delegate_index, table->entries, table->count, sizeof(csrt_DelegateMethodEntry));
    const csrt_DelegateMethodEntry* entry = index_find(&s_delegate_index, (const csrt_MethodTable*)fn);
    return entry ? entry->method : NULL;
}
