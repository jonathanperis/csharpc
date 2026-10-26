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

// Functions that throw a managed exception. Where exceptions unwind they never return; on wasm32
// there is no unwinder: throwing records the pending exception and returns, and csharpc's
// ExceptionLowering (src/Csharpc/Wasm) makes every caller test for it (docs/adr/0011-wasm32.md).
#ifdef __wasm__
#define CSRT_THROWS
#else
#define CSRT_THROWS _Noreturn
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
       CSRT_MT_DISPOSABLE = 16 /* runtime-implemented IDisposable whose Dispose() is a no-op (wait handles, CTS) */,
       CSRT_MT_MDARRAY = 32 /* rectangular array ("System.Int32[,]"): interfaces dispatch like the untyped array tables */,
       CSRT_MT_VALUE_TYPE = 64 /* a boxed value type (`is System.ValueType`) */,
       CSRT_MT_ENUM = 128 /* a boxed enum (`is System.Enum`) */,
       CSRT_MT_DISPOSE_SLOT = 256 /* with CSRT_MT_DISPOSABLE: vtable slot 3 is its IDisposable.Dispose */ };

struct csrt_MethodTable {
    const csrt_MethodTable* base;   // null for System.Object and interfaces
    const char* name;               // fully qualified type name, UTF-8
    uint32_t instance_size;         // bytes, including the header
    uint32_t flags;                 // CSRT_MT_*
    const csrt_InterfaceMapEntry* interfaces;  // all implemented interfaces (flattened)
    uint32_t interface_count;
    uint32_t reserved;              // CSRT_MT_MDARRAY: the rank; a one-dimensional array type: its element size
    // void* vtable[] follows — the compiler emits it as part of the same global.
};

typedef struct { const csrt_MethodTable* mt; } csrt_Object;
typedef struct { const csrt_MethodTable* mt; int32_t length; uint16_t chars[]; } csrt_String;
typedef struct { const csrt_MethodTable* mt; int64_t length; uint8_t data[]; } csrt_Array;
// Rectangular arrays (T[,]...): the total element count where csrt_Array has its length, then one
// length per dimension, then the row-major data (FunctionCodeGen.MultiDim.cs).
typedef struct { const csrt_MethodTable* mt; int64_t length; int64_t lengths[]; } csrt_MdArray;
int32_t csrt_array_rank(void* array);                               // Array.Rank of an `Array`
int64_t csrt_array_dimension_length(void* array, int32_t dimension); // Array.GetLength(d) of an `Array`

// Every method table is followed by its vtable. Slots 0..2 are System.Object's virtuals.
enum { CSRT_SLOT_TO_STRING = 0, CSRT_SLOT_EQUALS = 1, CSRT_SLOT_GET_HASH_CODE = 2 };
typedef struct { csrt_MethodTable header; void* vtable[3]; } csrt_BuiltinMethodTable;

extern const csrt_BuiltinMethodTable csrt_Object_MT;
extern const csrt_BuiltinMethodTable csrt_String_MT;
extern const csrt_BuiltinMethodTable csrt_Array_MT;
extern const csrt_BuiltinMethodTable csrt_PrimitiveArray_MT;  // arrays of non-references: never scanned by the GC
// Never instantiated: type-test targets. Boxed value types keep csrt_Object_MT as their base; `is ValueType`
// / `is Enum` test CSRT_MT_VALUE_TYPE / CSRT_MT_ENUM instead (csrt_isinst_class).
extern const csrt_BuiltinMethodTable csrt_ValueType_MT;
extern const csrt_BuiltinMethodTable csrt_Enum_MT;

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
void* csrt_flow_capture(void);   // the flow work queued now carries: none while ExecutionContext.SuppressFlow is in effect
void* csrt_alloc_noclear(const csrt_MethodTable* mt, int64_t size);           // caller initializes every byte it reads
csrt_Array* csrt_new_array_primitive_noclear(int64_t element_size, int64_t length);
// Integer formatting into a caller buffer ending at `end`; returns the first character written.
char* csrt_itoa_u64(uint64_t value, char* end);
char* csrt_itoa_i64(int64_t value, char* end);
void csrt_gc_collect(void);           // System.GC.Collect()
int64_t csrt_gc_total_memory(void);  // System.GC.GetTotalMemory(bool)
#if defined(CSRT_GC_NURSERY_DEFAULT)
// Heap reference store + card dirty (codegen write barrier; see gc.c / ADR 0004).
void csrt_gc_write(void** slot, void* value);
void csrt_gc_cards_dirty(void* start, size_t n);
#endif
// Weak references and ephemerons (gc.c; corelib/WeakReferences.cs). Handles are slot index + 1.
intptr_t csrt_weak_alloc(void* target, void* value, void* owner, int32_t ephemeron);
void* csrt_weak_target(intptr_t handle);
void* csrt_weak_value(intptr_t handle);
void csrt_weak_set(intptr_t handle, void* target, void* value);
void csrt_weak_free(intptr_t handle);

// Exceptions (eh.c) -----------------------------------------------------------------------------
// Exception objects: { mt, String* message, Exception* inner, String* paramName, String* actualValue,
// int32[] trace, inners, token }. Throwing raises an Itanium-ABI unwind (_Unwind_RaiseException) handled by
// csrt_personality; see eh.c. Mirrored by ModuleCodeGen.GetLibraryExceptionLayout.
typedef struct csrt_Exception {
    const csrt_MethodTable* mt;
    csrt_String* message;
    struct csrt_Exception* inner;
    csrt_String* param_name;
    csrt_String* actual_value;  // ArgumentOutOfRangeException(paramName, actualValue, message), formatted
    csrt_Array* trace;          // stack-trace frames recorded while thrown (eh.c, "Stack traces"); null: none
    csrt_Array* inners;         // AggregateException: InnerExceptions (exception pointers); null otherwise
    void* token;                // OperationCanceledException.CancellationToken (its source; null: None)
    int32_t hresult;            // HResult when set (constructor argument, the HResult setter); 0: the type's default
} csrt_Exception;
typedef struct { csrt_MethodTable header; void* vtable[4]; } csrt_ExceptionMethodTable;
enum { CSRT_SLOT_GET_MESSAGE = 3 };

CSRT_THROWS void csrt_throw_object(void* exception);
int32_t csrt_exception_get_hresult(void* exception);
void csrt_exception_set_token(void* exception, void* token);
void csrt_exception_set_hresult(void* exception, int32_t hresult);
CSRT_THROWS void csrt_rethrow(void* exception);
// Stack traces (docs/adr/0012-stack-traces.md): compiler-emitted site tables, registered by main.
void csrt_trace_init(const void* tables);
void csrt_trace_frame(void* unwind_exception, const void* site);   // wasm32 only
void csrt_trace_finally(void* unwind_exception, const void* site, const void* parent);
void csrt_trace_new_stack(void* exception);
void csrt_trace_preset(void* exception, const void* const* sites, int32_t count);
void csrt_trace_append_library(void* exception, const void* const* sites, int32_t count);
csrt_String* csrt_exception_get_stack_trace(void* exception);
void* csrt_exception_capture_trace(void* exception);
CSRT_THROWS void csrt_exception_throw_captured(void* exception, void* captured);
CSRT_THROWS void csrt_throw(const csrt_MethodTable* exception_type, csrt_String* message);  // by type name
void* csrt_begin_catch(void* unwind_exception);
void csrt_exception_init(void* exception, csrt_String* message, void* inner, csrt_String* param_name);
void csrt_exception_set_actual_value(void* exception, csrt_String* actual_value);
csrt_String* csrt_object_disposed_message(csrt_String* message, csrt_String* object_name);
csrt_String* csrt_exception_get_message(void* exception);
csrt_String* csrt_exception_to_string(void* exception);
_Noreturn void csrt_unhandled_exception(void* exception);
void* csrt_aggregate_exception_new(void** inners, int64_t count);
void csrt_exception_set_inners(void* exception, csrt_Array* inners);
int32_t csrt_exception_inner_count(void* exception);
void* csrt_exception_inner_at(void* exception, int32_t index);
void* csrt_task_canceled_exception_new(void);
bool csrt_is_operation_canceled(void* exception);
void* csrt_operation_canceled_exception_new(void* token);
void* csrt_object_disposed_exception_new(const char* message); // ObjectDisposedException(null, message)
CSRT_THROWS void csrt_throw_invalid_cast_from(const csrt_MethodTable* from, const csrt_MethodTable* to);
csrt_String* csrt_string_from_utf8(const char* text);
csrt_String* csrt_string_from_utf8_n(const char* text, size_t n);  // validating; ill-formed -> U+FFFD
void csrt_stderr_write_string(csrt_String* s);
csrt_String* csrt_object_type_name(void* obj, bool full);  // obj.GetType().Name / FullName
CSRT_THROWS void csrt_throw_null_reference(void);
CSRT_THROWS void csrt_throw_index_out_of_range(void);
CSRT_THROWS void csrt_throw_rank_out_of_range(void);
CSRT_THROWS void csrt_throw_divide_by_zero(void);
CSRT_THROWS void csrt_throw_overflow(void);
// A method csharpc could not compile was called (IL front-end, docs/adr/0010-il-frontend.md): the
// compiler reported it as a warning; reaching it at run time ends the program with that message.
_Noreturn void csrt_unsupported_method(const char* message);
CSRT_THROWS void csrt_throw_negate_overflow(void);
CSRT_THROWS void csrt_throw_out_of_memory(const char* message);  // NULL: "Insufficient memory to continue..."

// CLR size limits (Array.MaxLength, string length): checked *before* any size arithmetic so a
// huge request can never wrap into an undersized allocation.
#define CSRT_MAX_ARRAY_LENGTH 0x7FFFFFC7LL
#define CSRT_MAX_STRING_LENGTH 0x3FFFFFDFLL
csrt_String* csrt_string_alloc_checked(int64_t length);  // uninitialized chars; OOM above the limit
csrt_String* csrt_string_alloc_zeroed(int64_t length);   // zero-filled; OOM above the limit
CSRT_THROWS void csrt_throw_invalid_cast(void);

// Type metadata (System.Type reflection, src/runtime/type.c; emitted by ModuleCodeGen.TypeInfo.cs) ---
// Generated at compile time, only for programs that use a Type member needing it: one entry per
// method table the program has (plus the types those entries refer to). Runtime functions receive
// the table as an argument, so programs that never ask pay nothing.
enum {
    CSRT_TI_CLASS = 1 << 0, CSRT_TI_VALUE_TYPE = 1 << 1, CSRT_TI_ENUM = 1 << 2, CSRT_TI_ARRAY = 1 << 3,
    CSRT_TI_PRIMITIVE = 1 << 4, CSRT_TI_ABSTRACT = 1 << 5, CSRT_TI_SEALED = 1 << 6, CSRT_TI_INTERFACE = 1 << 7,
    CSRT_TI_PUBLIC = 1 << 8, CSRT_TI_NOT_PUBLIC = 1 << 9, CSRT_TI_NESTED = 1 << 10, CSRT_TI_NESTED_PUBLIC = 1 << 11,
    CSRT_TI_NESTED_PRIVATE = 1 << 12, CSRT_TI_NESTED_ASSEMBLY = 1 << 13, CSRT_TI_NESTED_FAMILY = 1 << 14,
    CSRT_TI_NESTED_FAM_OR_ASSEM = 1 << 15, CSRT_TI_NESTED_FAM_AND_ASSEM = 1 << 16, CSRT_TI_VISIBLE = 1 << 17,
    CSRT_TI_GENERIC_TYPE = 1 << 18, CSRT_TI_GENERIC_DEFINITION = 1 << 19, CSRT_TI_CONSTRUCTED_GENERIC = 1 << 20,
    CSRT_TI_GENERIC_PARAMETER = 1 << 21, CSRT_TI_CONTAINS_GENERIC_PARAMETERS = 1 << 22, CSRT_TI_POINTER = 1 << 23,
    CSRT_TI_SZARRAY = 1 << 24, CSRT_TI_VARIABLE_BOUND_ARRAY = 1 << 25, CSRT_TI_TYPE_DEFINITION = 1 << 26,
    CSRT_TI_BYREF_LIKE = 1 << 27, CSRT_TI_NULLABLE = 1 << 28, CSRT_TI_HAS_ELEMENT_TYPE = 1 << 29,
};
// Bits above 31 (uint64_t flags).
#define CSRT_TI_REFERENCE_TYPE (1ull << 32)      /* for variance and array covariance */
#define CSRT_TI_NO_HIERARCHY (1ull << 33)        /* base type not described (open generic types) */
#define CSRT_TI_ARRAY_INTERFACE (1ull << 35)     /* IList<T>, ICollection<T>, IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T> */
#define CSRT_TI_NO_INTERFACES (1ull << 36)       /* implemented interfaces not described (open generic types) */
#define CSRT_TI_ELEMENT_CLASS_SHIFT 40           /* 4 bits: primitives that share array storage (int[] and uint[], E[]) */

typedef struct csrt_TypeInfo {
    const csrt_MethodTable* mt;
    const csrt_MethodTable* base;                 // Type.BaseType
    const csrt_MethodTable* element;              // Type.GetElementType(); enums: the underlying type
    const csrt_MethodTable* definition;           // Type.GetGenericTypeDefinition()
    const csrt_MethodTable* declaring;            // Type.DeclaringType
    const csrt_MethodTable* const* generic_args;  // arguments (constructed types) or parameters (definitions)
    const csrt_MethodTable* const* interfaces;    // all implemented interfaces
    const char* namespace_name;                   // Type.Namespace (NULL: null)
    const char* assembly;                         // Assembly.FullName (NULL: unknown to csharpc)
    uint64_t flags;                               // CSRT_TI_*
    uint32_t generic_count;
    uint32_t interface_count;
    uint32_t rank;                                // arrays: rank; generic parameters: position
    uint32_t variance;                            // 2 bits per generic argument: 1 = out, 2 = in
} csrt_TypeInfo;

typedef struct { int64_t count; const csrt_TypeInfo* entries; } csrt_TypeInfoTable;

// Member metadata (constructors, methods, parameters, custom attributes): generated at compile time
// by ModuleCodeGen.Reflection.cs for the types the program hands to [DynamicallyAccessedMembers]
// locations (NativeAOT's model, docs/adr/0018-reflection-metadata.md), read by the corelib
// (corelib/Reflection/Members.cs). Only programs that reflect over members carry the table.
typedef struct csrt_AttributeRecord {
    const csrt_MethodTable* type;   // the attribute class
    void* (*create)(void);          // a new instance of the attribute, as GetCustomAttributes returns it
    void* (*data)(void);            // a new CustomAttributeData describing it
} csrt_AttributeRecord;

enum { CSRT_PARAM_HAS_DEFAULT = 1 << 16, CSRT_PARAM_BYREF = 1 << 17 };  // above ParameterAttributes' 16 bits
typedef struct csrt_ParameterRecord {
    const char* name;               // UTF-8; NULL: unnamed
    const csrt_MethodTable* type;   // the parameter type (the element type for by-ref parameters)
    void* (*default_value)(void);   // CSRT_PARAM_HAS_DEFAULT: the boxed constant (NULL here: a null default)
    const csrt_AttributeRecord* attributes;
    uint32_t attribute_count;
    uint32_t flags;                 // ParameterAttributes | CSRT_PARAM_*
} csrt_ParameterRecord;

enum { CSRT_METHOD_SOURCE_LAMBDA = 1 /* a lambda/local function lowered by csharpc: no .NET name */ };
typedef struct csrt_MethodRecord {
    const char* name;                          // ".ctor" for constructors
    const csrt_MethodTable* declaring;
    const csrt_MethodTable* return_type;       // NULL: void (and constructors)
    void* (*invoke)(void* target, void* args); // object Invoke(object target, object[] args); NULL: not invokable
    const csrt_ParameterRecord* parameters;
    const csrt_AttributeRecord* attributes;
    uint32_t parameter_count;
    uint32_t attribute_count;
    uint32_t method_attributes;                // MethodAttributes
    uint32_t flags;                            // CSRT_METHOD_*
    const void* function;                      // an instance method's own function (MethodInfo.CreateDelegate); NULL: none
} csrt_MethodRecord;

// MethodInfo.CreateDelegate: the Invoke signatures of the delegate types the program names.
typedef struct { const csrt_MethodTable* mt; const csrt_MethodTable* return_type; const csrt_MethodTable* const* parameters;
                 uint32_t parameter_count; uint32_t reserved; } csrt_DelegateSignature;
void* csrt_delegate_create(const csrt_MethodTable* mt, void* target, const void* fn);
void* csrt_gc_object_base(const void* p);   // the heap object an interior pointer is in, or NULL

// Which member kinds an entry lists (DynamicallyAccessedMemberTypes they were kept for).
enum { CSRT_RI_PUBLIC_CONSTRUCTORS = 1, CSRT_RI_NONPUBLIC_CONSTRUCTORS = 2, CSRT_RI_PUBLIC_METHODS = 4,
       CSRT_RI_NONPUBLIC_METHODS = 8, CSRT_RI_ATTRIBUTES = 16, CSRT_RI_PUBLIC_PROPERTIES = 32, CSRT_RI_NONPUBLIC_PROPERTIES = 64,
       CSRT_RI_ATTRIBUTES_UNKNOWN = 128 /* the type's attributes can't be listed exactly: querying them throws */ };
enum { CSRT_PROPERTY_PUBLIC = 1 /* an accessor is public */, CSRT_PROPERTY_STATIC = 2 };
typedef struct csrt_PropertyRecord {
    const char* name;
    const csrt_MethodTable* type;
    const csrt_MethodRecord* getter;           // NULL: no get accessor
    const csrt_MethodRecord* setter;           // NULL: no set accessor
    const csrt_AttributeRecord* attributes;
    uint32_t attribute_count;
    uint32_t flags;                            // CSRT_PROPERTY_*
} csrt_PropertyRecord;
typedef struct csrt_ReflectionInfo {
    const csrt_MethodTable* mt;
    const csrt_MethodRecord* constructors;
    const csrt_MethodRecord* methods;
    const csrt_AttributeRecord* attributes;    // the type's (or generic parameter's) own custom attributes
    const csrt_PropertyRecord* properties;
    uint32_t constructor_count;
    uint32_t method_count;
    uint32_t attribute_count;
    uint32_t described;                        // CSRT_RI_*
    uint32_t property_count;
    uint32_t reserved;
} csrt_ReflectionInfo;
typedef struct { int64_t count; const csrt_ReflectionInfo* entries; } csrt_ReflectionTable;

// Delegate.Method: the method a delegate's function pointer stands for.
typedef struct { const void* fn; const csrt_MethodRecord* method; } csrt_DelegateMethodEntry;
typedef struct { int64_t count; const csrt_DelegateMethodEntry* entries; } csrt_DelegateMethodTable;

// Type.FullName where it differs from the method table's name (generic instantiations print
// assembly-qualified arguments, generic parameters have none); `unknown`: csharpc can't tell.
typedef struct { const csrt_MethodTable* mt; const char* full_name; uint64_t unknown; } csrt_TypeNameEntry;
typedef struct { int64_t count; const csrt_TypeNameEntry* entries; } csrt_TypeNameTable;

// Type system -------------------------------------------------------------------------------
void* csrt_isinst_class(void* obj, const csrt_MethodTable* type);   // obj if it is a `type`, else null
void* csrt_castclass(void* obj, const csrt_MethodTable* type);      // obj, or InvalidCastException
void* const* csrt_interface_slots(void* obj, const csrt_MethodTable* interface_type);  // obj non-null
// The same through a call site's cache (a zero-initialized uint64_t per site).
void* const* csrt_interface_slots_cached(void* obj, const csrt_MethodTable* interface_type, uint64_t* site);
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
    X(Double, double) X(Single, float) X(Boolean, bool) X(Char, uint16_t) X(IntPtr, intptr_t) X(UIntPtr, uintptr_t)
#define CSRT_DECLARE_BOXED_MT(Name, CType) extern const csrt_BuiltinMethodTable csrt_Boxed_##Name##_MT;
CSRT_BOXED_TYPES(CSRT_DECLARE_BOXED_MT)
void* csrt_unbox(void* obj, const csrt_MethodTable* type);   // payload address, or NRE / InvalidCast

// Boxed enums: Enum.Equals (same type, same payload bytes). Structs get generated Equals/GetHashCode.
bool csrt_valuetype_equals(void* self, void* other);
