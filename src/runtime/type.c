// csrt type: System.Type objects ("reflection-lite"). One canonical Type object per method table,
// so typeof(X) == obj.GetType() is a pointer comparison. Names come from the method table's CLR
// full name ("System.Int32", "Program+Nested", "MyNs.Point", "System.Int32[]").
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

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

static csrt_String* from_ascii(const char* p, size_t n) {
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * n));
    s->length = (int32_t)n;
    for (size_t i = 0; i < n; i++) s->chars[i] = (uint8_t)p[i];
    return s;
}

static const char* full_name(TypeObject* t) {
    if (!t) csrt_throw_null_reference();
    return t->target->name ? t->target->name : "";
}

/// Type.FullName / ToString(): "Program+Nested", "System.Int32[]".
csrt_String* csrt_type_full_name(TypeObject* t) { const char* n = full_name(t); return from_ascii(n, strlen(n)); }

/// Type.Name: after the last '.' or '+' outside of a generic argument list.
csrt_String* csrt_type_name(TypeObject* t) {
    const char* n = full_name(t);
    const char* start = n;
    for (const char* p = n; *p && *p != '['; p++)
        if (*p == '.' || *p == '+') start = p + 1;
    return from_ascii(start, strlen(start));
}

/// Type.Namespace: the part of the top-level type name before its last '.'; null if none.
csrt_String* csrt_type_namespace(TypeObject* t) {
    const char* n = full_name(t);
    const char* end = NULL;
    for (const char* p = n; *p && *p != '+' && *p != '['; p++)
        if (*p == '.') end = p;
    return end ? from_ascii(n, (size_t)(end - n)) : NULL;
}

static csrt_String* type_to_string(void* o) { return csrt_type_full_name(o); }
