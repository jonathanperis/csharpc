// csrt type: System.Type objects. One canonical Type object per method table, so
// typeof(X) == obj.GetType() is a pointer comparison. Names come from the method table's CLR
// name ("System.Int32", "Program+Nested", "MyNs.Point", "System.Int32[]"); everything else
// (IsGenericType, BaseType, IsAssignableFrom, Assembly, ...) from compile-time metadata tables
// (csrt_TypeInfo in csrt.h, emitted by ModuleCodeGen.TypeInfo.cs only for programs that ask).
//
// Throwing helpers return on wasm32 (CSRT_THROWS), so every throw below is followed by a return.
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { const csrt_MethodTable* mt; const csrt_MethodTable* target; } TypeObject;

static csrt_String* type_to_string(void* o);
const csrt_BuiltinMethodTable csrt_Type_MT = {
    { &csrt_Object_MT.header, "System.RuntimeType", sizeof(TypeObject), 0 },
    { (void*)type_to_string, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

// Open-addressing cache in .bss — the conservative GC scans the executable's data segments, so
// the Type objects referenced from here stay alive.
#define TYPE_CACHE_SIZE 4096
static struct { const csrt_MethodTable* key; TypeObject* value; } s_types[TYPE_CACHE_SIZE];

TypeObject* csrt_type_from_mt(const csrt_MethodTable* mt) {
    size_t h = ((uintptr_t)mt >> 4) & (TYPE_CACHE_SIZE - 1);
    for (size_t probe = 0; probe < TYPE_CACHE_SIZE; probe++, h = (h + 1) & (TYPE_CACHE_SIZE - 1)) {
        if (s_types[h].key == mt) return s_types[h].value;
        if (!s_types[h].key) {
            TypeObject* t = csrt_alloc(&csrt_Type_MT.header, sizeof(TypeObject));
            t->target = mt;
            s_types[h].key = mt;
            s_types[h].value = t;
            return t;
        }
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

static const csrt_TypeInfo* lookup_info(const csrt_MethodTable* mt) {
    const csrt_TypeInfoTable* table = __atomic_load_n(&s_table, __ATOMIC_ACQUIRE);
    if (!table) return NULL;
    build_index(&s_info_index, table->entries, table->count, sizeof(csrt_TypeInfo));
    return index_find(&s_info_index, mt);
}

static void throw_utf8(const csrt_MethodTable* type, const char* message) { csrt_throw(type, csrt_string_from_utf8(message)); }

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
    return info ? type_or_null(info->element) : NULL;
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
