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

// Environment.NewLine of the target: .NET puts it in Console.WriteLine, StringBuilder.AppendLine and
// multi-line exception messages.
#ifdef _WIN32
#define CSRT_NEWLINE "\r\n"
#else
#define CSRT_NEWLINE "\n"
#endif

/// An unhandled exception was reported: end the process like .NET (SIGABRT on Unix, exit code
/// 0xE0434352 on Windows).
_Noreturn void csrt_fail_unhandled(void);

typedef struct csrt_MethodTable csrt_MethodTable;

// Interface map entry: for each interface a class implements (including inherited ones), the
// interface's identity method table and a table of implementation function pointers in the
// interface's member order. Interface calls look the table up with csrt_interface_slots.
typedef struct {
    const csrt_MethodTable* interface_type;
    void* const* slots;
} csrt_InterfaceMapEntry;

enum { CSRT_MT_ABSTRACT = 1, CSRT_MT_INTERFACE = 2, CSRT_MT_ITERATOR = 4 /* src/runtime/async.c */,
       CSRT_MT_NO_REFERENCES = 8 /* instances hold no heap references: the GC never scans them */,
       CSRT_MT_DISPOSABLE = 16 /* runtime-implemented IDisposable whose Dispose() is a no-op (wait handles, CTS) */ };

struct csrt_MethodTable {
    const csrt_MethodTable* base;   // null for System.Object and interfaces
    const char* name;               // fully qualified type name, UTF-8
    uint32_t instance_size;         // bytes, including the header
    uint32_t flags;                 // CSRT_MT_*
    const csrt_InterfaceMapEntry* interfaces;  // all implemented interfaces (flattened)
    uint32_t interface_count;
    uint32_t reserved;
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
extern const csrt_BuiltinMethodTable csrt_PrimitiveArray_MT;  // arrays of non-references: never scanned by the GC

// Lifecycle -------------------------------------------------------------------------------
void csrt_init(int argc, char** argv);
void csrt_shutdown(void);
csrt_Array* csrt_get_args(void);

// Allocation (conservative mark-sweep GC in gc.c — see docs/adr/0004-memory-management.md) --
void* csrt_alloc(const csrt_MethodTable* mt, int64_t size);
csrt_Array* csrt_new_array(int64_t element_size, int64_t length);
csrt_Array* csrt_new_array_primitive(int64_t element_size, int64_t length);   // element type holds no references
void* csrt_object_memberwise_clone(void* obj);
_Noreturn void csrt_fail_fast(const uint16_t* message);
_Noreturn void csrt_pruned_virtual(void);
// The execution-context flow (threads.c): one GC object per logical flow, captured when a thread starts,
// a work item is queued or an await registers its continuation, and current while that code runs.
// The corelib keeps the current cultures in it (.NET flows them through an AsyncLocal).
extern _Thread_local __attribute__((tls_model("local-exec"))) void* csrt_tl_flow;
void* csrt_flow_get(void);
void csrt_flow_set(void* flow);
void* csrt_alloc_noclear(const csrt_MethodTable* mt, int64_t size);           // caller initializes every byte it reads
csrt_Array* csrt_new_array_primitive_noclear(int64_t element_size, int64_t length);
// Integer formatting into a caller buffer ending at `end`; returns the first character written.
char* csrt_itoa_u64(uint64_t value, char* end);
char* csrt_itoa_i64(int64_t value, char* end);
void csrt_gc_collect(void);           // System.GC.Collect()
int64_t csrt_gc_total_memory(void);  // System.GC.GetTotalMemory(bool)

// Exceptions (eh.c) -----------------------------------------------------------------------------
// Exception objects: { mt, String* message, Exception* inner, String* paramName }. Throwing raises
// an Itanium-ABI unwind (_Unwind_RaiseException) handled by csrt_personality; see eh.c.
typedef struct csrt_Exception {
    const csrt_MethodTable* mt;
    csrt_String* message;
    struct csrt_Exception* inner;
    csrt_String* param_name;
    csrt_String* actual_value;  // ArgumentOutOfRangeException(paramName, actualValue, message), formatted
} csrt_Exception;
typedef struct { csrt_MethodTable header; void* vtable[4]; } csrt_ExceptionMethodTable;
enum { CSRT_SLOT_GET_MESSAGE = 3 };

_Noreturn void csrt_throw_object(void* exception);
_Noreturn void csrt_rethrow(void* exception);
_Noreturn void csrt_throw(const csrt_MethodTable* exception_type, csrt_String* message);  // by type name
void* csrt_begin_catch(void* unwind_exception);
void csrt_exception_init(void* exception, csrt_String* message, void* inner, csrt_String* param_name);
void csrt_exception_set_actual_value(void* exception, csrt_String* actual_value);
csrt_String* csrt_object_disposed_message(csrt_String* message, csrt_String* object_name);
csrt_String* csrt_exception_get_message(void* exception);
csrt_String* csrt_exception_to_string(void* exception);
_Noreturn void csrt_unhandled_exception(void* exception);
void* csrt_aggregate_exception_new(void** inners, int64_t count);
void* csrt_task_canceled_exception_new(void);
bool csrt_is_operation_canceled(void* exception);
void* csrt_operation_canceled_exception_new(void);
_Noreturn void csrt_throw_invalid_cast_from(const csrt_MethodTable* from, const csrt_MethodTable* to);
csrt_String* csrt_string_from_utf8(const char* text);
csrt_String* csrt_string_from_utf8_n(const char* text, size_t n);  // validating; ill-formed -> U+FFFD
void csrt_stderr_write_string(csrt_String* s);
csrt_String* csrt_object_type_name(void* obj, bool full);  // obj.GetType().Name / FullName
_Noreturn void csrt_throw_null_reference(void);
_Noreturn void csrt_throw_index_out_of_range(void);
_Noreturn void csrt_throw_divide_by_zero(void);
_Noreturn void csrt_throw_overflow(void);
// A method csharpc could not compile was called (IL front-end, docs/adr/0010-il-frontend.md): the
// compiler reported it as a warning; reaching it at run time ends the program with that message.
_Noreturn void csrt_unsupported_method(const char* message);
_Noreturn void csrt_throw_negate_overflow(void);
_Noreturn void csrt_throw_out_of_memory(const char* message);  // NULL: "Insufficient memory to continue..."

// CLR size limits (Array.MaxLength, string length): checked *before* any size arithmetic so a
// huge request can never wrap into an undersized allocation.
#define CSRT_MAX_ARRAY_LENGTH 0x7FFFFFC7LL
#define CSRT_MAX_STRING_LENGTH 0x3FFFFFDFLL
csrt_String* csrt_string_alloc_checked(int64_t length);  // uninitialized chars; OOM above the limit
csrt_String* csrt_string_alloc_zeroed(int64_t length);   // zero-filled; OOM above the limit
_Noreturn void csrt_throw_invalid_cast(void);

// Type system -------------------------------------------------------------------------------
void* csrt_isinst_class(void* obj, const csrt_MethodTable* type);   // obj if it is a `type`, else null
void* csrt_castclass(void* obj, const csrt_MethodTable* type);      // obj, or InvalidCastException
void* const* csrt_interface_slots(void* obj, const csrt_MethodTable* interface_type);  // obj non-null
csrt_String* csrt_object_to_string(void* obj);                      // virtual ToString(); null -> ""
csrt_String* csrt_object_to_string_default(void* obj);              // System.Object.ToString()
bool csrt_object_equals_default(void* self, void* other);
int32_t csrt_object_get_hash_code_default(void* self);

// Strings -----------------------------------------------------------------------------------
csrt_String* csrt_string_concat(csrt_String* a, csrt_String* b);
bool csrt_string_equals(csrt_String* a, csrt_String* b);
int32_t csrt_string_get_hash_code(csrt_String* s);
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
void csrt_console_write_bool(bool v);
void csrt_console_write_char(uint16_t v);
void csrt_console_newline(void);

// Environment / Math ---------------------------------------------------------------------------
int64_t csrt_environment_tick_count64(void);
_Noreturn void csrt_environment_exit(int32_t code);

#endif

// Boxing -------------------------------------------------------------------------------------
// A boxed primitive is { mt, value } with the value at offset 8. One method table per primitive,
// whose vtable formats / compares the payload (so `object o = 42; o.ToString()` is "42").
#define CSRT_BOXED_TYPES(X) \
    X(Int32, int32_t) X(Int64, int64_t) X(UInt32, uint32_t) X(UInt64, uint64_t) \
    X(Int16, int16_t) X(UInt16, uint16_t) X(Byte, uint8_t) X(SByte, int8_t) \
    X(Double, double) X(Single, float) X(Boolean, bool) X(Char, uint16_t)
#define CSRT_DECLARE_BOXED_MT(Name, CType) extern const csrt_BuiltinMethodTable csrt_Boxed_##Name##_MT;
CSRT_BOXED_TYPES(CSRT_DECLARE_BOXED_MT)
void* csrt_unbox(void* obj, const csrt_MethodTable* type);   // payload address, or NRE / InvalidCast

// Boxed enums: Enum.Equals (same type, same payload bytes). Structs get generated Equals/GetHashCode.
bool csrt_valuetype_equals(void* self, void* other);
