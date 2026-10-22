// csrt — the csharpc bootstrap runtime.
//
// This is deliberately tiny: just enough runtime for the spike's subset of C#. The long-term plan
// (docs/04-runtime.md) is for most of this to be replaced by a corelib written in C# and compiled
// by csharpc itself, the way Rust's core/alloc/std or NativeAOT's System.Private.CoreLib work.
//
// Object layout contract with the compiler (see docs/03-lowering.md):
//
//   object : { const csrt_MethodTable* mt; fields... }
//   string : { const csrt_MethodTable* mt; int32_t length; uint16_t chars[length]; }   // UTF-16
//   T[]    : { const csrt_MethodTable* mt; int64_t length; T data[length]; }           // data @ 16
#ifndef CSRT_H
#define CSRT_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef struct csrt_MethodTable csrt_MethodTable;
struct csrt_MethodTable {
    const csrt_MethodTable* base;   // null for System.Object
    const char* name;               // fully qualified type name, UTF-8
    uint32_t instance_size;         // bytes, including the header
    uint32_t flags;
    // void* vtable[] follows — the compiler emits it as part of the same global.
};

typedef struct { const csrt_MethodTable* mt; } csrt_Object;
typedef struct { const csrt_MethodTable* mt; int32_t length; uint16_t chars[]; } csrt_String;
typedef struct { const csrt_MethodTable* mt; int64_t length; uint8_t data[]; } csrt_Array;

// Every method table is followed by its vtable. Slots 0..2 are System.Object's virtuals.
enum { CSRT_SLOT_TO_STRING = 0, CSRT_SLOT_EQUALS = 1, CSRT_SLOT_GET_HASH_CODE = 2 };
typedef struct { csrt_MethodTable header; void* vtable[3]; } csrt_BuiltinMethodTable;

extern const csrt_BuiltinMethodTable csrt_Object_MT;
extern const csrt_BuiltinMethodTable csrt_String_MT;
extern const csrt_BuiltinMethodTable csrt_Array_MT;

// Lifecycle -------------------------------------------------------------------------------
void csrt_init(int argc, char** argv);
void csrt_shutdown(void);
csrt_Array* csrt_get_args(void);

// Allocation (no GC yet: memory is never freed — see docs/adr/0004-memory-management.md) --
void* csrt_alloc(const csrt_MethodTable* mt, int64_t size);
csrt_Array* csrt_new_array(int64_t element_size, int64_t length);

// Exceptions (spike: unhandled-only, they terminate the process like .NET does) -------------
_Noreturn void csrt_throw_null_reference(void);
_Noreturn void csrt_throw_index_out_of_range(void);
_Noreturn void csrt_throw_divide_by_zero(void);
_Noreturn void csrt_throw_overflow(void);
_Noreturn void csrt_throw_invalid_cast(void);
_Noreturn void csrt_throw(const csrt_MethodTable* exception_type, csrt_String* message);

// Type system -------------------------------------------------------------------------------
void* csrt_isinst_class(void* obj, const csrt_MethodTable* type);   // obj if it is a `type`, else null
void* csrt_castclass(void* obj, const csrt_MethodTable* type);      // obj, or InvalidCastException
csrt_String* csrt_object_to_string(void* obj);                      // virtual ToString(); null -> ""
csrt_String* csrt_object_to_string_default(void* obj);              // System.Object.ToString()
bool csrt_object_equals_default(void* self, void* other);
int32_t csrt_object_get_hash_code_default(void* self);

// Strings -----------------------------------------------------------------------------------
csrt_String* csrt_string_concat(csrt_String* a, csrt_String* b);
bool csrt_string_equals(csrt_String* a, csrt_String* b);
bool csrt_string_is_null_or_empty(csrt_String* s);
uint16_t csrt_string_char_at(csrt_String* s, int32_t index);
csrt_String* csrt_string_from_i32(int32_t v);
csrt_String* csrt_string_from_i64(int64_t v);
csrt_String* csrt_string_from_u32(uint32_t v);
csrt_String* csrt_string_from_u64(uint64_t v);
csrt_String* csrt_string_from_f64(double v);
csrt_String* csrt_string_from_f32(float v);
csrt_String* csrt_string_from_bool(bool v);
csrt_String* csrt_string_from_char(uint16_t v);

// Console -----------------------------------------------------------------------------------
void csrt_console_write_string(csrt_String* s);
void csrt_console_write_i32(int32_t v);
void csrt_console_write_i64(int64_t v);
void csrt_console_write_u32(uint32_t v);
void csrt_console_write_u64(uint64_t v);
void csrt_console_write_f64(double v);
void csrt_console_write_f32(float v);
void csrt_console_write_bool(bool v);
void csrt_console_write_char(uint16_t v);
void csrt_console_newline(void);

// Environment / Math ---------------------------------------------------------------------------
int64_t csrt_environment_tick_count64(void);
_Noreturn void csrt_environment_exit(int32_t code);

#endif
