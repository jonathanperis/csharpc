// csrt — the csharpc bootstrap runtime. See csrt.h for the ABI contract.
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"
#include "gc.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

static csrt_String* string_to_string(void* self) { return self; }

const csrt_BuiltinMethodTable csrt_Object_MT = {
    { NULL, "System.Object", sizeof(csrt_Object), 0 },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};
const csrt_BuiltinMethodTable csrt_String_MT = {
    { &csrt_Object_MT.header, "System.String", 0, CSRT_MT_NO_REFERENCES },
    { (void*)string_to_string, (void*)csrt_string_equals, (void*)csrt_object_get_hash_code_default },
};
const csrt_BuiltinMethodTable csrt_Array_MT = {
    { &csrt_Object_MT.header, "System.Array", 0, 0 },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};
const csrt_BuiltinMethodTable csrt_PrimitiveArray_MT = {
    { &csrt_Object_MT.header, "System.Array", 0, CSRT_MT_NO_REFERENCES },
    { (void*)csrt_object_to_string_default, (void*)csrt_object_equals_default, (void*)csrt_object_get_hash_code_default },
};

static int s_argc;
static char** s_argv;

// ---------------------------------------------------------------------------------------------
// Output buffering. .NET's Console.Out autoflushes, but process exit flushes too; for a native
// executable a big stdio buffer flushed at shutdown (and before any crash) is the sane default.
// ---------------------------------------------------------------------------------------------
static char s_stdout_buffer[1 << 16];

void csrt_init(int argc, char** argv) {
    s_argc = argc;
    s_argv = argv;
    csrt_gc_init();
    setvbuf(stdout, s_stdout_buffer, _IOFBF, sizeof s_stdout_buffer);
}

void csrt_threads_wait_foreground(void);

void csrt_shutdown(void) {
    csrt_threads_wait_foreground();  // like .NET: the process outlives Main while foreground threads run
    fflush(stdout);
    csrt_gc_shutdown();
}

// ---------------------------------------------------------------------------------------------
// Allocation
// ---------------------------------------------------------------------------------------------
// Allocation failure is a catchable OutOfMemoryException, as in .NET (eh.c).
_Noreturn static void out_of_memory(void) { csrt_throw_out_of_memory(NULL); }

void* csrt_alloc(const csrt_MethodTable* mt, int64_t size) {
    csrt_Object* obj = csrt_gc_alloc((size_t)size);
    if (!obj) out_of_memory();
    obj->mt = mt;
    return obj;
}

void* csrt_alloc_noclear(const csrt_MethodTable* mt, int64_t size) {
    csrt_Object* obj = csrt_gc_alloc_noclear((size_t)size);
    if (!obj) out_of_memory();
    obj->mt = mt;
    return obj;
}

static inline csrt_Array* new_array(const csrt_MethodTable* mt, int64_t element_size, int64_t length, bool clear) {
    // .NET: negative or > int.MaxValue lengths overflow; > Array.MaxLength is OutOfMemoryException.
    if (length < 0 || length > INT32_MAX) csrt_throw_overflow();
    if (length > CSRT_MAX_ARRAY_LENGTH) csrt_throw_out_of_memory("Array dimensions exceeded supported range.");
    size_t size = sizeof(csrt_Array) + (size_t)(element_size * length);
    csrt_Array* arr = clear ? csrt_gc_alloc(size) : csrt_gc_alloc_noclear(size);
    if (!arr) out_of_memory();
    arr->mt = mt;
    arr->length = length;
    return arr;
}

csrt_Array* csrt_new_array(int64_t element_size, int64_t length) {
    return new_array(&csrt_Array_MT.header, element_size, length, true);
}

csrt_Array* csrt_new_array_primitive(int64_t element_size, int64_t length) {
    return new_array(&csrt_PrimitiveArray_MT.header, element_size, length, true);
}

csrt_Array* csrt_new_array_primitive_noclear(int64_t element_size, int64_t length) {
    return new_array(&csrt_PrimitiveArray_MT.header, element_size, length, false);
}

/// Every runtime string allocation goes through here. Lengths are 64-bit so callers can pass sums
/// (a + b, count * n) without wrapping; anything above the CLR limit is an OutOfMemoryException.
csrt_String* csrt_string_alloc_checked(int64_t length) {
    if (length < 0 || length > CSRT_MAX_STRING_LENGTH) out_of_memory();
    // Not zeroed: every caller writes all `length` chars, and the GC never scans strings.
    csrt_String* s = csrt_gc_alloc_noclear(sizeof(csrt_String) + sizeof(uint16_t) * (size_t)length);
    if (!s) out_of_memory();
    s->mt = &csrt_String_MT.header;
    s->length = (int32_t)length;
    return s;
}

/// Same limit check, zero-filled (for callers that may not write every char).
csrt_String* csrt_string_alloc_zeroed(int64_t length) {
    if (length < 0 || length > CSRT_MAX_STRING_LENGTH) out_of_memory();
    csrt_String* s = csrt_gc_alloc(sizeof(csrt_String) + sizeof(uint16_t) * (size_t)length);
    if (!s) out_of_memory();
    s->mt = &csrt_String_MT.header;
    s->length = (int32_t)length;
    return s;
}

static csrt_String* string_alloc(int64_t length) { return csrt_string_alloc_checked(length); }

static csrt_String* string_from_ascii(const char* text) {
    int32_t n = (int32_t)strlen(text);
    csrt_String* s = string_alloc(n);
    for (int32_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    return s;
}

/// Decodes one UTF-8 sequence at p[0..n) (n >= 1) the way .NET's UTF8Encoding does: well-formed
/// sequences only (no overlongs, surrogates or > U+10FFFF); an ill-formed one becomes U+FFFD and
/// consumes just its maximal valid prefix (Unicode "substitution of maximal subparts"). Never reads
/// past p + n — input may come from stdin, files or argv.
static uint32_t utf8_decode_one(const unsigned char* p, size_t n, size_t* consumed) {
    unsigned char c = p[0];
    if (c < 0x80) { *consumed = 1; return c; }
    size_t need;
    uint32_t cp;
    unsigned char lo = 0x80, hi = 0xBF;  // allowed range of the *first* continuation byte
    if (c >= 0xC2 && c <= 0xDF) { need = 1; cp = c & 0x1Fu; }
    else if (c >= 0xE0 && c <= 0xEF) {
        need = 2; cp = c & 0x0Fu;
        if (c == 0xE0) lo = 0xA0;        // no overlongs
        else if (c == 0xED) hi = 0x9F;   // no surrogates
    } else if (c >= 0xF0 && c <= 0xF4) {
        need = 3; cp = c & 0x07u;
        if (c == 0xF0) lo = 0x90;        // no overlongs
        else if (c == 0xF4) hi = 0x8F;   // <= U+10FFFF
    } else { *consumed = 1; return 0xFFFD; }  // continuation byte or invalid lead (C0, C1, F5..FF)
    size_t i = 1;
    for (; i <= need; i++) {
        if (i >= n) break;
        unsigned char b = p[i];
        if (b < (i == 1 ? lo : 0x80) || b > (i == 1 ? hi : 0xBF)) break;
        cp = (cp << 6) | (b & 0x3Fu);
    }
    if (i <= need) { *consumed = i; return 0xFFFD; }  // truncated or bad continuation
    *consumed = need + 1;
    return cp;
}

/// UTF-8 bytes p[0..n) → string (two passes: count UTF-16 units, then fill).
csrt_String* csrt_string_from_utf8_n(const char* text, size_t n) {
    const unsigned char* p = (const unsigned char*)text;
    int64_t units = 0;
    for (size_t i = 0, k; i < n; i += k) units += utf8_decode_one(p + i, n - i, &k) >= 0x10000 ? 2 : 1;
    csrt_String* s = string_alloc(units);
    int32_t o = 0;
    for (size_t i = 0, k; i < n; i += k) {
        uint32_t cp = utf8_decode_one(p + i, n - i, &k);
        if (cp >= 0x10000) {
            cp -= 0x10000;
            s->chars[o++] = (uint16_t)(0xD800 + (cp >> 10));
            s->chars[o++] = (uint16_t)(0xDC00 + (cp & 0x3FF));
        } else {
            s->chars[o++] = (uint16_t)cp;
        }
    }
    return s;
}

static csrt_String* string_from_utf8(const char* text) { return csrt_string_from_utf8_n(text, strlen(text)); }

csrt_Array* csrt_get_args(void) {
    // Like .NET, args excludes the program name.
    int64_t n = s_argc > 0 ? s_argc - 1 : 0;
    csrt_Array* arr = csrt_new_array(sizeof(void*), n);
    csrt_String** items = (csrt_String**)arr->data;
    for (int64_t i = 0; i < n; i++) items[i] = string_from_utf8(s_argv[i + 1]);
    return arr;
}

// ---------------------------------------------------------------------------------------------
// Exceptions live in eh.c (exception classes, throw/catch, the unwinder personality routine).
// ---------------------------------------------------------------------------------------------
static void write_utf16(FILE* f, const uint16_t* chars, int32_t length);

csrt_String* csrt_string_from_utf8(const char* text) { return string_from_utf8(text); }
void csrt_stderr_write_string(csrt_String* s) { if (s) write_utf16(stderr, s->chars, s->length); }

// ---------------------------------------------------------------------------------------------
// Type system
// ---------------------------------------------------------------------------------------------
// Arrays and strings implement BCL generic interfaces (IList<T>, IEnumerable<char>...) through
// corelib helpers: an interface MT's otherwise-unused `interfaces` field holds the slot table used
// when the receiver is an array, and its `base` field the one used for strings (CodeGen/CoreLib*.cs).
static void noop_dispose(void* self) { (void)self; }
static void* const s_noop_dispose_slots[] = { (void*)noop_dispose };

static void* const* builtin_interface_slots(const csrt_MethodTable* objmt, const csrt_MethodTable* itf) {
    // Arrays allocated by generated code have a per-element-type MT ("System.Int32[]") whose
    // interface map lists exactly the interfaces of that element type; the shared tables only serve
    // untagged arrays (made by the runtime), whose element type is unknown.
    if (objmt == &csrt_Array_MT.header || objmt == &csrt_PrimitiveArray_MT.header) return (void* const*)itf->interfaces;
    if (objmt == &csrt_String_MT.header) return (void* const*)itf->base;
    // Runtime-implemented disposables (`using var e = new CountdownEvent(n)`): IDisposable.Dispose
    // (its only slot) is a no-op — their native resources live as long as the object.
    if ((objmt->flags & CSRT_MT_DISPOSABLE) && strcmp(itf->name, "System.IDisposable") == 0) return s_noop_dispose_slots;
    return NULL;
}

// Generic variance (IEnumerable<string> usable as IEnumerable<object>, Action<object> as
// Action<string>). At startup the compiler registers a descriptor for every variant generic interface
// instantiation it emitted: generic definition key, type-argument method tables, and per argument
// 2 bits of variance (1 = out, 2 = in) plus bit 2 = "reference type". Variant interfaces over
// reference types share their slot layout, so a compatible instantiation's slot table can be used.
typedef struct {
    const csrt_MethodTable* itf;
    const void* definition;
    int32_t argc;
    const csrt_MethodTable* const* args;
    uint32_t mask;  // 3 bits per argument
} VariantDescriptor;
static VariantDescriptor* s_variant;
static int s_variant_count, s_variant_cap;

void csrt_register_variant_interface(const csrt_MethodTable* itf, const void* definition, int32_t argc,
                                     const csrt_MethodTable* const* args, uint32_t mask) {
    if (s_variant_count == s_variant_cap) {
        s_variant_cap = s_variant_cap ? s_variant_cap * 2 : 16;
        s_variant = realloc(s_variant, sizeof *s_variant * (size_t)s_variant_cap);
    }
    s_variant[s_variant_count++] = (VariantDescriptor){ itf, definition, argc, args, mask };
}

static const VariantDescriptor* variant_descriptor(const csrt_MethodTable* itf) {
    for (int i = 0; i < s_variant_count; i++) if (s_variant[i].itf == itf) return &s_variant[i];
    return NULL;
}

static void* const* exact_interface_slots(const csrt_MethodTable* mt, const csrt_MethodTable* itf) {
    for (uint32_t i = 0; i < mt->interface_count; i++)
        if (mt->interfaces[i].interface_type == itf) return mt->interfaces[i].slots;
    return builtin_interface_slots(mt, itf);
}

static bool variant_compatible(const csrt_MethodTable* from, const csrt_MethodTable* to, int depth);

/// Is a reference of runtime type `src` (a type-argument MT) assignable to type-argument `dst`?
static bool argument_assignable(const csrt_MethodTable* src, const csrt_MethodTable* dst, int depth) {
    if (src == dst || dst == &csrt_Object_MT.header) return true;
    if (dst->flags & CSRT_MT_INTERFACE) {
        if (src->flags & CSRT_MT_INTERFACE) return variant_compatible(src, dst, depth + 1);
        if (exact_interface_slots(src, dst)) return true;
        for (uint32_t i = 0; i < src->interface_count; i++)
            if (variant_compatible(src->interfaces[i].interface_type, dst, depth + 1)) return true;
        return false;
    }
    for (const csrt_MethodTable* mt = src; mt; mt = mt->base) if (mt == dst) return true;
    return false;
}

static bool variant_compatible(const csrt_MethodTable* from, const csrt_MethodTable* to, int depth) {
    if (from == to) return true;
    if (depth > 4) return false;
    const VariantDescriptor* f = variant_descriptor(from);
    const VariantDescriptor* t = variant_descriptor(to);
    if (!f || !t || f->definition != t->definition || f->argc != t->argc) return false;
    for (int32_t i = 0; i < t->argc; i++) {
        const csrt_MethodTable* a = f->args[i];
        const csrt_MethodTable* b = t->args[i];
        if (a == b) continue;
        uint32_t bits = (t->mask >> (3 * i)) & 7u;
        uint32_t fbits = (f->mask >> (3 * i)) & 7u;
        if (!(bits & 4u) || !(fbits & 4u)) return false;               // variance needs reference types
        if ((bits & 1u) && argument_assignable(a, b, depth)) continue; // out T
        if ((bits & 2u) && argument_assignable(b, a, depth)) continue; // in T
        return false;
    }
    return true;
}

static void* const* variant_interface_slots(const csrt_MethodTable* mt, const csrt_MethodTable* itf) {
    if (!variant_descriptor(itf)) return NULL;
    for (uint32_t i = 0; i < mt->interface_count; i++)
        if (variant_compatible(mt->interfaces[i].interface_type, itf, 0)) return mt->interfaces[i].slots;
    return NULL;
}

void* csrt_isinst_class(void* obj, const csrt_MethodTable* type) {
    if (!obj) return NULL;
    const csrt_MethodTable* objmt = ((csrt_Object*)obj)->mt;
    if (type->flags & CSRT_MT_INTERFACE) {
        return exact_interface_slots(objmt, type) || variant_interface_slots(objmt, type) ? obj : NULL;
    }
    for (const csrt_MethodTable* mt = objmt; mt; mt = mt->base)
        if (mt == type) return obj;
    return NULL;
}

void* const* csrt_interface_slots(void* obj, const csrt_MethodTable* interface_type) {
    const csrt_MethodTable* mt = ((csrt_Object*)obj)->mt;
    for (uint32_t i = 0; i < mt->interface_count; i++)
        if (mt->interfaces[i].interface_type == interface_type) return mt->interfaces[i].slots;
    void* const* builtin = builtin_interface_slots(mt, interface_type);
    if (builtin) return builtin;
    void* const* variant = variant_interface_slots(mt, interface_type);
    if (variant) return variant;
    fflush(stdout);
    fprintf(stderr, "Unhandled exception. System.InvalidCastException: Unable to cast object of type '%s' to type '%s'.\n",
            mt->name, interface_type->name);
    abort();
}

_Noreturn static void invalid_cast(const csrt_MethodTable* from, const csrt_MethodTable* to) {
    csrt_throw_invalid_cast_from(from, to);
}

void* csrt_castclass(void* obj, const csrt_MethodTable* type) {
    if (obj && !csrt_isinst_class(obj, type)) invalid_cast(((csrt_Object*)obj)->mt, type);
    return obj;
}

typedef csrt_String* (*csrt_ToStringFn)(void*);

csrt_String* csrt_object_to_string(void* obj) {
    if (!obj) return string_alloc(0);
    const void* const* vtable = (const void* const*)(((csrt_Object*)obj)->mt + 1);
    return ((csrt_ToStringFn)vtable[CSRT_SLOT_TO_STRING])(obj);
}

csrt_String* csrt_object_to_string_default(void* obj) { return string_from_utf8(((csrt_Object*)obj)->mt->name); }
bool csrt_object_equals_default(void* self, void* other) { return self == other; }
int32_t csrt_object_get_hash_code_default(void* self) { return (int32_t)(((uintptr_t)self >> 3) * 2654435761u); }

// ---------------------------------------------------------------------------------------------
// Strings
// ---------------------------------------------------------------------------------------------
bool csrt_string_is_null_or_empty(csrt_String* s) { return !s || s->length == 0; }



csrt_String* csrt_string_concat(csrt_String* a, csrt_String* b) {
    int32_t la = a ? a->length : 0, lb = b ? b->length : 0;
    csrt_String* s = string_alloc((int64_t)la + lb);
    if (la) memcpy(s->chars, a->chars, sizeof(uint16_t) * (size_t)la);
    if (lb) memcpy(s->chars + la, b->chars, sizeof(uint16_t) * (size_t)lb);
    return s;
}

bool csrt_string_equals(csrt_String* a, csrt_String* b) {
    if (a == b) return true;
    if (!a || !b || a->length != b->length) return false;
    return memcmp(a->chars, b->chars, sizeof(uint16_t) * (size_t)a->length) == 0;
}

uint16_t csrt_string_char_at(csrt_String* s, int32_t index) {
    if (!s) csrt_throw_null_reference();
    if ((uint32_t)index >= (uint32_t)s->length) csrt_throw_index_out_of_range();
    return s->chars[index];
}

char* csrt_itoa_u64(uint64_t value, char* end) {
    static const char digits2[] =
        "00010203040506070809101112131415161718192021222324252627282930313233343536373839"
        "40414243444546474849505152535455565758596061626364656667686970717273747576777879"
        "8081828384858687888990919293949596979899";
    char* p = end;
    while (value >= 100) {
        unsigned r = (unsigned)(value % 100);
        value /= 100;
        *--p = digits2[r * 2 + 1];
        *--p = digits2[r * 2];
    }
    if (value >= 10) {
        *--p = digits2[value * 2 + 1];
        *--p = digits2[value * 2];
    } else {
        *--p = (char)('0' + value);
    }
    return p;
}

char* csrt_itoa_i64(int64_t value, char* end) {
    if (value >= 0) return csrt_itoa_u64((uint64_t)value, end);
    char* p = csrt_itoa_u64(0 - (uint64_t)value, end);
    *--p = '-';
    return p;
}

static csrt_String* string_from_digits(const char* begin, const char* end) {
    csrt_String* s = string_alloc((int32_t)(end - begin));
    for (int32_t i = 0; begin + i < end; i++) s->chars[i] = (uint8_t)begin[i];
    return s;
}

csrt_String* csrt_string_from_i32(int32_t v) { char b[24]; return string_from_digits(csrt_itoa_i64(v, b + 24), b + 24); }
csrt_String* csrt_string_from_i64(int64_t v) { char b[24]; return string_from_digits(csrt_itoa_i64(v, b + 24), b + 24); }
csrt_String* csrt_string_from_u32(uint32_t v) { char b[24]; return string_from_digits(csrt_itoa_u64(v, b + 24), b + 24); }
csrt_String* csrt_string_from_u64(uint64_t v) { char b[24]; return string_from_digits(csrt_itoa_u64(v, b + 24), b + 24); }
csrt_String* csrt_string_from_bool(bool v) { return string_from_ascii(v ? "True" : "False"); }
csrt_String* csrt_string_from_char(uint16_t v) { csrt_String* s = string_alloc(1); s->chars[0] = v; return s; }

// ---------------------------------------------------------------------------------------------
// Console (UTF-16 -> UTF-8)
// ---------------------------------------------------------------------------------------------
static void write_utf16(FILE* f, const uint16_t* chars, int32_t length) {
    for (int32_t i = 0; i < length; i++) {
        uint32_t cp = chars[i];
        if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < length && chars[i + 1] >= 0xDC00 && chars[i + 1] <= 0xDFFF) {
            cp = 0x10000 + ((cp - 0xD800) << 10) + (chars[++i] - 0xDC00);
        } else if (cp >= 0xD800 && cp <= 0xDFFF) {
            cp = 0xFFFD;  // lone surrogate
        }
        if (cp < 0x80) fputc((int)cp, f);
        else if (cp < 0x800) { fputc(0xC0 | (int)(cp >> 6), f); fputc(0x80 | (int)(cp & 0x3F), f); }
        else if (cp < 0x10000) {
            fputc(0xE0 | (int)(cp >> 12), f); fputc(0x80 | (int)((cp >> 6) & 0x3F), f); fputc(0x80 | (int)(cp & 0x3F), f);
        } else {
            fputc(0xF0 | (int)(cp >> 18), f); fputc(0x80 | (int)((cp >> 12) & 0x3F), f);
            fputc(0x80 | (int)((cp >> 6) & 0x3F), f); fputc(0x80 | (int)(cp & 0x3F), f);
        }
    }
}

void csrt_console_write_string(csrt_String* s) { if (s) write_utf16(stdout, s->chars, s->length); }
static void write_digits(const char* begin, const char* end) { fwrite(begin, 1, (size_t)(end - begin), stdout); }
void csrt_console_write_i32(int32_t v) { char b[24]; write_digits(csrt_itoa_i64(v, b + 24), b + 24); }
void csrt_console_write_i64(int64_t v) { char b[24]; write_digits(csrt_itoa_i64(v, b + 24), b + 24); }
void csrt_console_write_u32(uint32_t v) { char b[24]; write_digits(csrt_itoa_u64(v, b + 24), b + 24); }
void csrt_console_write_u64(uint64_t v) { char b[24]; write_digits(csrt_itoa_u64(v, b + 24), b + 24); }
void csrt_console_write_bool(bool v) { fputs(v ? "True" : "False", stdout); }
void csrt_console_write_char(uint16_t v) { write_utf16(stdout, &v, 1); }
void csrt_console_newline(void) { fputc('\n', stdout); }

// ---------------------------------------------------------------------------------------------
// Environment
// ---------------------------------------------------------------------------------------------
void csrt_environment_exit(int32_t code) {
    fflush(stdout);
    exit(code);
}

int64_t csrt_environment_tick_count64(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
}

// ---------------------------------------------------------------------------------------------
// Boxing
// ---------------------------------------------------------------------------------------------
#define BOX_PAYLOAD(obj) ((void*)((char*)(obj) + 8))

static csrt_String* box_to_string_Int32(void* o) { return csrt_string_from_i32(*(int32_t*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_Int64(void* o) { return csrt_string_from_i64(*(int64_t*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_UInt32(void* o) { return csrt_string_from_u32(*(uint32_t*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_UInt64(void* o) { return csrt_string_from_u64(*(uint64_t*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_Int16(void* o) { return csrt_string_from_i32(*(int16_t*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_UInt16(void* o) { return csrt_string_from_i32(*(uint16_t*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_Byte(void* o) { return csrt_string_from_i32(*(uint8_t*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_SByte(void* o) { return csrt_string_from_i32(*(int8_t*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_Double(void* o) { return csrt_string_from_f64(*(double*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_Single(void* o) { return csrt_string_from_f32(*(float*)BOX_PAYLOAD(o)); }
static csrt_String* box_to_string_Boolean(void* o) { return csrt_string_from_bool(*(uint8_t*)BOX_PAYLOAD(o) != 0); }
static csrt_String* box_to_string_Char(void* o) { return csrt_string_from_char(*(uint16_t*)BOX_PAYLOAD(o)); }

#define DEFINE_BOXED(Name, CType) \
    static bool box_equals_##Name(void* self, void* other) { \
        return other && ((csrt_Object*)other)->mt == ((csrt_Object*)self)->mt && \
               memcmp(BOX_PAYLOAD(self), BOX_PAYLOAD(other), sizeof(CType)) == 0; \
    } \
    static int32_t box_hash_##Name(void* self) { \
        uint64_t bits = 0; memcpy(&bits, BOX_PAYLOAD(self), sizeof(CType)); \
        return (int32_t)(bits ^ (bits >> 32)); \
    } \
    const csrt_BuiltinMethodTable csrt_Boxed_##Name##_MT = { \
        { &csrt_Object_MT.header, "System." #Name, 8 + sizeof(CType), 0 }, \
        { (void*)box_to_string_##Name, (void*)box_equals_##Name, (void*)box_hash_##Name }, \
    };
CSRT_BOXED_TYPES(DEFINE_BOXED)

void* csrt_unbox(void* obj, const csrt_MethodTable* type) {
    if (!obj) csrt_throw_null_reference();
    if (((csrt_Object*)obj)->mt != type) invalid_cast(((csrt_Object*)obj)->mt, type);
    return BOX_PAYLOAD(obj);
}

// ---------------------------------------------------------------------------------------------
// Boxed user structs (payload at offset 8, instance_size = 8 + payload size)
// ---------------------------------------------------------------------------------------------
bool csrt_valuetype_equals(void* self, void* other) {
    const csrt_MethodTable* mt = ((csrt_Object*)self)->mt;
    if (!other || ((csrt_Object*)other)->mt != mt) return false;
    return memcmp((char*)self + 8, (char*)other + 8, mt->instance_size - 8) == 0;
}

int32_t csrt_valuetype_get_hash_code(void* self) {
    const csrt_MethodTable* mt = ((csrt_Object*)self)->mt;
    const unsigned char* p = (const unsigned char*)self + 8;
    uint32_t h = 2166136261u;
    for (uint32_t i = 0; i < mt->instance_size - 8; i++) h = (h ^ p[i]) * 16777619u;
    return (int32_t)h;
}

// ---------------------------------------------------------------------------------------------
// Corelib exports (ADR 0008). Number formatting and parsing are C# (src/Csharpc/corelib/Number.*.cs);
// the compiler emits these symbols from the corelib whenever the program references the runtime
// code that calls them (boxed doubles, Enum.Parse, ...). These weak definitions only satisfy the
// linker in programs where that code is dead.
// ---------------------------------------------------------------------------------------------
_Noreturn static void missing_export(const char* name) {
    fprintf(stderr, "csrt: corelib export %s was not emitted\n", name);
    abort();
}
__attribute__((weak)) csrt_String* csrt_string_from_f64(double v) { (void)v; missing_export("csrt_string_from_f64"); }
__attribute__((weak)) csrt_String* csrt_string_from_f32(float v) { (void)v; missing_export("csrt_string_from_f32"); }
__attribute__((weak)) csrt_String* csrt_decimal_to_string(const void* d) { (void)d; missing_export("csrt_decimal_to_string"); }
__attribute__((weak)) int32_t csrt_decimal_compare(const void* a, const void* b) { (void)a; (void)b; missing_export("csrt_decimal_compare"); }
__attribute__((weak)) int32_t csrt_decimal_get_hash_code(const void* d) { (void)d; missing_export("csrt_decimal_get_hash_code"); }
__attribute__((weak)) csrt_String* csrt_bigint_to_string(void* a) { (void)a; missing_export("csrt_bigint_to_string"); }
__attribute__((weak)) int32_t csrt_bigint_compare(void* a, void* b) { (void)a; (void)b; missing_export("csrt_bigint_compare"); }
__attribute__((weak)) int32_t csrt_bigint_get_hash_code(void* a) { (void)a; missing_export("csrt_bigint_get_hash_code"); }
__attribute__((weak)) csrt_String* csrt_timespan_to_string(int64_t ticks) { (void)ticks; missing_export("csrt_timespan_to_string"); }
__attribute__((weak)) csrt_String* csrt_datetime_to_string(int64_t ticks) { (void)ticks; missing_export("csrt_datetime_to_string"); }
__attribute__((weak)) int32_t csrt_parse_i64_styled(csrt_String* s, int32_t styles, int32_t bits, bool is_signed, int64_t* result) {
    (void)s; (void)styles; (void)bits; (void)is_signed; (void)result;
    missing_export("csrt_parse_i64_styled");
}
