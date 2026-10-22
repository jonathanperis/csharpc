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
    { &csrt_Object_MT.header, "System.String", 0, 0 },
    { (void*)string_to_string, (void*)csrt_string_equals, (void*)csrt_object_get_hash_code_default },
};
const csrt_BuiltinMethodTable csrt_Array_MT = {
    { &csrt_Object_MT.header, "System.Array", 0, 0 },
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

void csrt_shutdown(void) {
    fflush(stdout);
    csrt_gc_shutdown();
}

// ---------------------------------------------------------------------------------------------
// Allocation
// ---------------------------------------------------------------------------------------------
_Noreturn static void out_of_memory(void) {
    fflush(stdout);
    fputs("Unhandled exception. System.OutOfMemoryException: Insufficient memory to continue the execution of the program.\n", stderr);
    abort();
}

void* csrt_alloc(const csrt_MethodTable* mt, int64_t size) {
    csrt_Object* obj = csrt_gc_alloc((size_t)size);
    if (!obj) out_of_memory();
    obj->mt = mt;
    return obj;
}

csrt_Array* csrt_new_array(int64_t element_size, int64_t length) {
    if (length < 0) csrt_throw_overflow();
    if (element_size > 0 && length > (INT64_MAX - (int64_t)sizeof(csrt_Array)) / element_size) out_of_memory();
    csrt_Array* arr = csrt_gc_alloc(sizeof(csrt_Array) + (size_t)(element_size * length));
    if (!arr) out_of_memory();
    arr->mt = &csrt_Array_MT.header;
    arr->length = length;
    return arr;
}

static csrt_String* string_alloc(int32_t length) {
    csrt_String* s = csrt_gc_alloc(sizeof(csrt_String) + sizeof(uint16_t) * (size_t)length);
    if (!s) out_of_memory();
    s->mt = &csrt_String_MT.header;
    s->length = length;
    return s;
}

static csrt_String* string_from_ascii(const char* text) {
    int32_t n = (int32_t)strlen(text);
    csrt_String* s = string_alloc(n);
    for (int32_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    return s;
}

static csrt_String* string_from_utf8(const char* text) {
    // Decode UTF-8 to UTF-16 (two passes: count, then fill).
    const unsigned char* p = (const unsigned char*)text;
    int32_t units = 0;
    for (const unsigned char* q = p; *q;) {
        if (*q < 0x80) q += 1, units += 1;
        else if (*q < 0xE0) q += 2, units += 1;
        else if (*q < 0xF0) q += 3, units += 1;
        else q += 4, units += 2;
    }
    csrt_String* s = string_alloc(units);
    int32_t i = 0;
    while (*p) {
        uint32_t cp;
        if (*p < 0x80) cp = *p++;
        else if (*p < 0xE0) { cp = (*p++ & 0x1Fu) << 6; cp |= (*p++ & 0x3Fu); }
        else if (*p < 0xF0) { cp = (*p++ & 0x0Fu) << 12; cp |= (*p++ & 0x3Fu) << 6; cp |= (*p++ & 0x3Fu); }
        else { cp = (*p++ & 0x07u) << 18; cp |= (*p++ & 0x3Fu) << 12; cp |= (*p++ & 0x3Fu) << 6; cp |= (*p++ & 0x3Fu); }
        if (cp >= 0x10000) {
            cp -= 0x10000;
            s->chars[i++] = (uint16_t)(0xD800 + (cp >> 10));
            s->chars[i++] = (uint16_t)(0xDC00 + (cp & 0x3FF));
        } else {
            s->chars[i++] = (uint16_t)cp;
        }
    }
    return s;
}

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
// Number formatting, matching .NET Core 3.0+ semantics (shortest round-trippable "R" output,
// scientific notation outside [1e-4, 1e15), "E+XX" exponents, and the invariant culture's "NaN"/"Infinity").
// ---------------------------------------------------------------------------------------------
static void format_real(char* out, size_t cap, double v, int max_digits, bool is_float) {
    if (isnan(v)) { snprintf(out, cap, "NaN"); return; }
    if (isinf(v)) { snprintf(out, cap, v > 0 ? "Infinity" : "-Infinity"); return; }  // invariant culture
    if (v == 0) { snprintf(out, cap, signbit(v) ? "-0" : "0"); return; }

    // Find the shortest digit string that round-trips.
    char sci[64];
    for (int p = 1; p <= max_digits; p++) {
        snprintf(sci, sizeof sci, "%.*e", p - 1, v);
        if (is_float ? (strtof(sci, NULL) == (float)v) : (strtod(sci, NULL) == v)) break;
    }

    // sci looks like "-d.ddddde+XX". Split into sign, digits and decimal exponent.
    bool negative = sci[0] == '-';
    const char* s = sci + (negative ? 1 : 0);
    char digits[32];
    int nd = 0;
    for (; *s && *s != 'e'; s++)
        if (*s != '.') digits[nd++] = *s;
    while (nd > 1 && digits[nd - 1] == '0') nd--;  // trim trailing zeros
    digits[nd] = 0;
    int exp = atoi(s + 1);

    char* o = out;
    char* end = out + cap - 1;
    if (negative && o < end) *o++ = '-';
    if (exp >= 15 || exp < -4) {
        *o++ = digits[0];
        if (nd > 1) { *o++ = '.'; for (int i = 1; i < nd && o < end; i++) *o++ = digits[i]; }
        snprintf(o, (size_t)(end - o + 1), "E%c%02d", exp < 0 ? '-' : '+', exp < 0 ? -exp : exp);
        return;
    }
    if (exp < 0) {
        *o++ = '0'; *o++ = '.';
        for (int i = 0; i < -exp - 1; i++) *o++ = '0';
        for (int i = 0; i < nd; i++) *o++ = digits[i];
    } else {
        for (int i = 0; i <= exp; i++) *o++ = i < nd ? digits[i] : '0';
        if (nd > exp + 1) { *o++ = '.'; for (int i = exp + 1; i < nd; i++) *o++ = digits[i]; }
    }
    *o = 0;
}

// ---------------------------------------------------------------------------------------------
// Type system
// ---------------------------------------------------------------------------------------------
void* csrt_isinst_class(void* obj, const csrt_MethodTable* type) {
    if (!obj) return NULL;
    const csrt_MethodTable* objmt = ((csrt_Object*)obj)->mt;
    if (type->flags & CSRT_MT_INTERFACE) {
        for (uint32_t i = 0; i < objmt->interface_count; i++)
            if (objmt->interfaces[i].interface_type == type) return obj;
        return NULL;
    }
    for (const csrt_MethodTable* mt = objmt; mt; mt = mt->base)
        if (mt == type) return obj;
    return NULL;
}

void* const* csrt_interface_slots(void* obj, const csrt_MethodTable* interface_type) {
    const csrt_MethodTable* mt = ((csrt_Object*)obj)->mt;
    for (uint32_t i = 0; i < mt->interface_count; i++)
        if (mt->interfaces[i].interface_type == interface_type) return mt->interfaces[i].slots;
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
    csrt_String* s = string_alloc(la + lb);
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

csrt_String* csrt_string_from_i32(int32_t v) { char b[16]; snprintf(b, sizeof b, "%d", v); return string_from_ascii(b); }
csrt_String* csrt_string_from_i64(int64_t v) { char b[24]; snprintf(b, sizeof b, "%lld", (long long)v); return string_from_ascii(b); }
csrt_String* csrt_string_from_u32(uint32_t v) { char b[16]; snprintf(b, sizeof b, "%u", v); return string_from_ascii(b); }
csrt_String* csrt_string_from_u64(uint64_t v) { char b[24]; snprintf(b, sizeof b, "%llu", (unsigned long long)v); return string_from_ascii(b); }
csrt_String* csrt_string_from_f64(double v) { char b[64]; format_real(b, sizeof b, v, 17, false); return string_from_utf8(b); }
csrt_String* csrt_string_from_f32(float v) { char b[64]; format_real(b, sizeof b, v, 9, true); return string_from_utf8(b); }
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
void csrt_console_write_i32(int32_t v) { printf("%d", v); }
void csrt_console_write_i64(int64_t v) { printf("%lld", (long long)v); }
void csrt_console_write_u32(uint32_t v) { printf("%u", v); }
void csrt_console_write_u64(uint64_t v) { printf("%llu", (unsigned long long)v); }
void csrt_console_write_f64(double v) { char b[64]; format_real(b, sizeof b, v, 17, false); fputs(b, stdout); }
void csrt_console_write_f32(float v) { char b[64]; format_real(b, sizeof b, v, 9, true); fputs(b, stdout); }
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
