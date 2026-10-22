// csrt exception handling: exception classes, throw/rethrow, and the unwinder personality.
//
// Design (docs/adr/0005-exceptions.md):
//  * Exceptions are ordinary GC objects: { mt, message, inner, paramName, actualValue } (csrt_Exception).
//  * `throw` wraps the object in an Itanium-ABI _Unwind_Exception and calls
//    _Unwind_RaiseException (libgcc_s / libunwind). Two-phase unwinding, as in C++ and .NET.
//  * csharpc emits LLVM `invoke` + `landingpad` for calls inside try regions. Catch clauses use
//    the catch type's *method table pointer* as type-info (null = catch-all); every landing pad
//    is also a cleanup. LLVM turns that into the standard LSDA in .gcc_except_table.
//  * csrt_personality (below) parses the LSDA. Search phase: a frame "handles" the exception if
//    one of the call site's clauses names a method table on the thrown object's base chain.
//    Cleanup phase: every frame with a landing pad is entered; the compiled code re-checks the
//    clause types (and runs `when` filters) and either enters a handler or rethrows outward.
//  * If the search phase finds no handler, the exception is unhandled. Like .NET we still run
//    the finally blocks on the way out (a forced unwind), then print
//    "Unhandled exception. <ToString()>" and abort() (exit status 134).
#define _GNU_SOURCE
#include "csrt.h"

#include <setjmp.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unwind.h>

// ---------------------------------------------------------------------------------------------
// The BCL exception hierarchy csharpc knows about. User exception classes derive from these;
// csharpc references them by symbol name ("csrt_MT_" + full name with '.' -> '_').
// vtable: [ToString, Equals, GetHashCode, get_Message]
// ---------------------------------------------------------------------------------------------
#define EXC_VTABLE { (void*)csrt_exception_to_string, (void*)csrt_object_equals_default, \
                     (void*)csrt_object_get_hash_code_default, (void*)csrt_exception_get_message }
#define DEF_EXC(sym, name, base) \
    const csrt_ExceptionMethodTable csrt_MT_##sym = { { base, name, sizeof(csrt_Exception), 0 }, EXC_VTABLE };

DEF_EXC(System_Exception, "System.Exception", &csrt_Object_MT.header)
DEF_EXC(System_SystemException, "System.SystemException", &csrt_MT_System_Exception.header)
DEF_EXC(System_ArgumentException, "System.ArgumentException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_ArgumentNullException, "System.ArgumentNullException", &csrt_MT_System_ArgumentException.header)
DEF_EXC(System_ArgumentOutOfRangeException, "System.ArgumentOutOfRangeException", &csrt_MT_System_ArgumentException.header)
DEF_EXC(System_InvalidOperationException, "System.InvalidOperationException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_ObjectDisposedException, "System.ObjectDisposedException", &csrt_MT_System_InvalidOperationException.header)
DEF_EXC(System_NotSupportedException, "System.NotSupportedException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_NotImplementedException, "System.NotImplementedException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_NullReferenceException, "System.NullReferenceException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_IndexOutOfRangeException, "System.IndexOutOfRangeException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_ArithmeticException, "System.ArithmeticException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_DivideByZeroException, "System.DivideByZeroException", &csrt_MT_System_ArithmeticException.header)
DEF_EXC(System_OverflowException, "System.OverflowException", &csrt_MT_System_ArithmeticException.header)
DEF_EXC(System_InvalidCastException, "System.InvalidCastException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_FormatException, "System.FormatException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_Threading_ThreadStateException, "System.Threading.ThreadStateException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_Threading_SynchronizationLockException, "System.Threading.SynchronizationLockException",
        &csrt_MT_System_SystemException.header)
DEF_EXC(System_Collections_Generic_KeyNotFoundException, "System.Collections.Generic.KeyNotFoundException",
        &csrt_MT_System_SystemException.header)
DEF_EXC(System_OperationCanceledException, "System.OperationCanceledException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_Threading_Tasks_TaskCanceledException, "System.Threading.Tasks.TaskCanceledException",
        &csrt_MT_System_OperationCanceledException.header)
DEF_EXC(System_AggregateException, "System.AggregateException", &csrt_MT_System_Exception.header)
DEF_EXC(System_OutOfMemoryException, "System.OutOfMemoryException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_TypeLoadException, "System.TypeLoadException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_DllNotFoundException, "System.DllNotFoundException", &csrt_MT_System_TypeLoadException.header)
DEF_EXC(System_EntryPointNotFoundException, "System.EntryPointNotFoundException", &csrt_MT_System_TypeLoadException.header)

// ---------------------------------------------------------------------------------------------
// Exception members
// ---------------------------------------------------------------------------------------------
void csrt_exception_init(void* exception, csrt_String* message, void* inner, csrt_String* param_name) {
    csrt_Exception* e = exception;
    e->message = message;
    e->inner = inner;
    e->param_name = param_name;
}

static csrt_String* concat_utf8(csrt_String* a, const char* b) { return csrt_string_concat(a, csrt_string_from_utf8(b)); }

// Exception.Message (+ ArgumentException's " (Parameter 'x')" suffix).
csrt_String* csrt_exception_get_message(void* exception) {
    csrt_Exception* e = exception;
    csrt_String* message = e->message;
    if (!message) {
        char buffer[512];
        snprintf(buffer, sizeof buffer, "Exception of type '%s' was thrown.", e->mt->name);
        message = csrt_string_from_utf8(buffer);
    }
    if (e->param_name && e->param_name->length > 0) {
        message = concat_utf8(message, " (Parameter '");
        message = csrt_string_concat(message, e->param_name);
        message = concat_utf8(message, "')");
    }
    if (e->actual_value) {  // ArgumentOutOfRangeException.Message: "...\nActual value was X."
        message = concat_utf8(message, "\nActual value was ");
        message = csrt_string_concat(message, e->actual_value);
        message = concat_utf8(message, ".");
    }
    return message;
}

void csrt_exception_set_actual_value(void* exception, csrt_String* actual_value) {
    ((csrt_Exception*)exception)->actual_value = actual_value;
}

typedef csrt_String* (*csrt_MessageFn)(void*);

static csrt_String* virtual_message(void* exception) {
    const void* const* vtable = (const void* const*)(((csrt_Object*)exception)->mt + 1);
    return ((csrt_MessageFn)vtable[CSRT_SLOT_GET_MESSAGE])(exception);
}

// Exception.ToString() without a stack trace: "Type: Message\n ---> Inner\n   --- End of ... ---".
csrt_String* csrt_exception_to_string(void* exception) {
    csrt_Exception* e = exception;
    csrt_String* s = csrt_string_from_utf8(e->mt->name);
    csrt_String* message = virtual_message(exception);
    if (message && message->length > 0) s = csrt_string_concat(concat_utf8(s, ": "), message);
    if (e->inner) {
        s = csrt_string_concat(concat_utf8(s, "\n ---> "), csrt_object_to_string(e->inner));
        s = concat_utf8(s, "\n   --- End of inner exception stack trace ---");
    }
    return s;
}

// obj.GetType().Name / .FullName, folded by the compiler (no System.Type objects yet).
csrt_String* csrt_object_type_name(void* obj, bool full) {
    if (!obj) csrt_throw_null_reference();
    const char* name = ((csrt_Object*)obj)->mt->name;
    if (!full)
        for (const char* p = name; *p; p++)
            if (*p == '.' || *p == '+') name = p + 1;
    return csrt_string_from_utf8(name);
}

// ---------------------------------------------------------------------------------------------
// Throwing
// ---------------------------------------------------------------------------------------------
static const uint64_t CSRT_EXCEPTION_CLASS = 0x43534841524E4554ULL;  // "CSHARNET"

typedef struct {
    void* exception;  // the managed object (also rooted in s_in_flight while unwinding)
    struct _Unwind_Exception unwind;
} csrt_Throw;

// GC root for the exception currently being propagated (unwind headers live in malloc memory,
// which the conservative collector doesn't scan). Thread-local: the GC scans every thread's TLS.
static _Thread_local __attribute__((tls_model("local-exec"))) void* volatile s_in_flight;

static void throw_cleanup(_Unwind_Reason_Code reason, struct _Unwind_Exception* unwind) {
    (void)reason;
    free((char*)unwind - offsetof(csrt_Throw, unwind));
}

_Noreturn static void unhandled(void* exception) {
    fflush(stdout);
    fputs("Unhandled exception. ", stderr);
    csrt_stderr_write_string(csrt_object_to_string(exception));
    fputc('\n', stderr);
    fflush(stderr);
    abort();
}

static _Unwind_Reason_Code unhandled_stop(int version, _Unwind_Action actions, uint64_t exception_class,
                                          struct _Unwind_Exception* unwind, struct _Unwind_Context* context, void* parameter) {
    (void)version; (void)exception_class; (void)context; (void)parameter;
    if (actions & _UA_END_OF_STACK)
        unhandled(((csrt_Throw*)((char*)unwind - offsetof(csrt_Throw, unwind)))->exception);
    return _URC_NO_REASON;
}

static void* new_exception(const csrt_ExceptionMethodTable* mt, const char* message) {
    void* e = csrt_alloc(&mt->header, sizeof(csrt_Exception));
    csrt_exception_init(e, message ? csrt_string_from_utf8(message) : NULL, NULL, NULL);
    return e;
}

// Exception traps (thread-pool workers): an exception no C# handler catches on this thread runs
// its finally blocks (forced unwind) and then returns to the innermost csrt_trap_call instead of
// terminating the process, so Parallel.For can rethrow it on the calling thread.
typedef struct { jmp_buf env; uintptr_t frame; void* exception; } csrt_Trap;
static _Thread_local __attribute__((tls_model("local-exec"))) csrt_Trap* tl_trap;

static _Unwind_Reason_Code trap_stop(int version, _Unwind_Action actions, uint64_t exception_class,
                                     struct _Unwind_Exception* unwind, struct _Unwind_Context* context, void* parameter) {
    (void)version; (void)exception_class;
    csrt_Trap* trap = parameter;
    if ((actions & _UA_END_OF_STACK) || _Unwind_GetCFA(context) > trap->frame) {
        csrt_Throw* t = (csrt_Throw*)((char*)unwind - offsetof(csrt_Throw, unwind));
        trap->exception = t->exception;
        free(t);
        s_in_flight = NULL;
        longjmp(trap->env, 1);
    }
    return _URC_NO_REASON;
}

/// Runs fn(arg); returns NULL, or the exception it threw that nothing on this thread caught.
__attribute__((noinline)) void* csrt_trap_call(void (*fn)(void*), void* arg) {
    csrt_Trap trap;
    trap.exception = NULL;
    trap.frame = (uintptr_t)__builtin_frame_address(0);
    csrt_Trap* volatile outer = tl_trap;
    if (setjmp(trap.env) == 0) {
        tl_trap = &trap;
        fn(arg);
        tl_trap = outer;
        return NULL;
    }
    tl_trap = outer;
    return trap.exception;
}

/// An exception that escaped an `async void` method: crash like .NET (the finally blocks already ran).
void csrt_unhandled_exception(void* exception) { unhandled(exception); }

/// new AggregateException(inners): "One or more errors occurred. (m1) (m2)", InnerException = first.
void* csrt_aggregate_exception_new(void** inners, int64_t count) {
    csrt_String* message = csrt_string_from_utf8("One or more errors occurred.");
    for (int64_t i = 0; i < count; i++) {
        message = concat_utf8(message, " (");
        message = csrt_string_concat(message, virtual_message(inners[i]));
        message = concat_utf8(message, ")");
    }
    void* e = csrt_alloc(&csrt_MT_System_AggregateException.header, sizeof(csrt_Exception));
    csrt_exception_init(e, message, count > 0 ? inners[0] : NULL, NULL);
    return e;
}

void* csrt_task_canceled_exception_new(void) {
    return new_exception(&csrt_MT_System_Threading_Tasks_TaskCanceledException, "A task was canceled.");
}

void* csrt_operation_canceled_exception_new(void) {
    return new_exception(&csrt_MT_System_OperationCanceledException, "The operation was canceled.");
}

void csrt_throw_object(void* exception) {
    if (!exception)  // `throw null;` throws a NullReferenceException
        exception = new_exception(&csrt_MT_System_NullReferenceException, "Object reference not set to an instance of an object.");
    csrt_Throw* t = calloc(1, sizeof *t);
    if (!t) abort();
    t->exception = exception;
    t->unwind.exception_class = CSRT_EXCEPTION_CLASS;
    t->unwind.exception_cleanup = throw_cleanup;
    s_in_flight = exception;
    _Unwind_RaiseException(&t->unwind);
    if (tl_trap) _Unwind_ForcedUnwind(&t->unwind, trap_stop, tl_trap);  // never returns
    // Only returns when the search phase found no handler: the exception is unhandled. Like .NET,
    // still run the finally blocks on the way out (forced unwind), then report at the stack's end.
    _Unwind_ForcedUnwind(&t->unwind, unhandled_stop, NULL);
    unhandled(exception);
}

void csrt_rethrow(void* exception) { csrt_throw_object(exception); }

// Called first thing in a landing pad: unwrap the managed exception object.
void* csrt_begin_catch(void* unwind_exception) {
    struct _Unwind_Exception* unwind = unwind_exception;
    csrt_Throw* t = (csrt_Throw*)((char*)unwind - offsetof(csrt_Throw, unwind));
    void* exception = t->exception;
    free(t);
    s_in_flight = NULL;
    return exception;
}

// Runtime-raised exceptions, with .NET's exact messages.
void csrt_throw_null_reference(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_NullReferenceException, "Object reference not set to an instance of an object."));
}
void csrt_throw_index_out_of_range(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_IndexOutOfRangeException, "Index was outside the bounds of the array."));
}
void csrt_throw_divide_by_zero(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_DivideByZeroException, "Attempted to divide by zero."));
}
void csrt_throw_overflow(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_OverflowException, "Arithmetic operation resulted in an overflow."));
}
/// Allocation failed / exceeded a CLR size limit: a catchable OutOfMemoryException (never abort).
void csrt_throw_out_of_memory(const char* message) {
    csrt_throw_object(new_exception(&csrt_MT_System_OutOfMemoryException,
        message ? message : "Insufficient memory to continue the execution of the program."));
}
void csrt_throw_negate_overflow(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_OverflowException, "Negating the minimum value of a twos complement number is invalid."));
}
void csrt_throw_invalid_cast(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_InvalidCastException, "Specified cast is not valid."));
}
void csrt_throw_invalid_cast_from(const csrt_MethodTable* from, const csrt_MethodTable* to) {
    char buffer[1024];
    snprintf(buffer, sizeof buffer, "Unable to cast object of type '%s' to type '%s'.", from->name, to->name);
    csrt_throw_object(new_exception(&csrt_MT_System_InvalidCastException, buffer));
}

// Throw by type *name* + message: for runtime code that only has a name-only method table
// (strings.c, format.c). Resolves to the real exception class so `catch (FormatException)` works.
static const csrt_ExceptionMethodTable* const s_by_name[] = {
    &csrt_MT_System_Exception, &csrt_MT_System_SystemException, &csrt_MT_System_ArgumentException,
    &csrt_MT_System_ArgumentNullException, &csrt_MT_System_ArgumentOutOfRangeException,
    &csrt_MT_System_InvalidOperationException, &csrt_MT_System_ObjectDisposedException,
    &csrt_MT_System_NotSupportedException, &csrt_MT_System_NotImplementedException,
    &csrt_MT_System_NullReferenceException, &csrt_MT_System_IndexOutOfRangeException,
    &csrt_MT_System_ArithmeticException, &csrt_MT_System_DivideByZeroException, &csrt_MT_System_OverflowException,
    &csrt_MT_System_InvalidCastException, &csrt_MT_System_FormatException,
    &csrt_MT_System_Collections_Generic_KeyNotFoundException,
    &csrt_MT_System_Threading_ThreadStateException, &csrt_MT_System_Threading_SynchronizationLockException,
    &csrt_MT_System_Collections_Generic_KeyNotFoundException, &csrt_MT_System_OperationCanceledException,
    &csrt_MT_System_Threading_Tasks_TaskCanceledException, &csrt_MT_System_AggregateException,
    &csrt_MT_System_OutOfMemoryException,
    &csrt_MT_System_TypeLoadException, &csrt_MT_System_DllNotFoundException, &csrt_MT_System_EntryPointNotFoundException,
};

static csrt_String* chars_slice(csrt_String* s, int32_t start, int32_t length) {
    csrt_String* r = csrt_string_alloc_zeroed((int64_t)length);
    memcpy(r->chars, s->chars + start, 2 * (size_t)length);
    return r;
}

void csrt_throw(const csrt_MethodTable* type, csrt_String* message) {
    const csrt_ExceptionMethodTable* mt = &csrt_MT_System_Exception;
    for (size_t i = 0; type && i < sizeof s_by_name / sizeof *s_by_name; i++)
        if (strcmp(s_by_name[i]->header.name, type->name) == 0) mt = s_by_name[i];
    void* e = csrt_alloc(&mt->header, sizeof(csrt_Exception));
    // Runtime helpers spell ArgumentException messages out in full ("... (Parameter 'x')"); split
    // the suffix off into ParamName, which .NET sets and Message re-appends.
    csrt_String* param_name = NULL;
    if (message && (mt == &csrt_MT_System_ArgumentException || mt == &csrt_MT_System_ArgumentNullException
                    || mt == &csrt_MT_System_ArgumentOutOfRangeException)) {
        static const char marker[] = " (Parameter '";
        const int32_t m = (int32_t)sizeof marker - 1, n = message->length;
        const uint16_t* c = message->chars;
        if (n >= m + 2 && c[n - 1] == ')' && c[n - 2] == '\'')
            for (int32_t at = n - 2 - m; at >= 0; at--) {
                int32_t k = 0;
                while (k < m && c[at + k] == (uint16_t)marker[k]) k++;
                if (k < m) continue;
                param_name = chars_slice(message, at + m, n - 2 - (at + m));
                message = chars_slice(message, 0, at);
                break;
            }
    }
    csrt_exception_init(e, message, NULL, param_name);
    csrt_throw_object(e);
}

// ---------------------------------------------------------------------------------------------
// Personality routine (Itanium C++ ABI §7, LSDA format as emitted by LLVM)
// ---------------------------------------------------------------------------------------------
enum {
    DW_EH_PE_absptr = 0x00, DW_EH_PE_uleb128 = 0x01, DW_EH_PE_udata2 = 0x02, DW_EH_PE_udata4 = 0x03,
    DW_EH_PE_udata8 = 0x04, DW_EH_PE_sleb128 = 0x09, DW_EH_PE_sdata2 = 0x0A, DW_EH_PE_sdata4 = 0x0B,
    DW_EH_PE_sdata8 = 0x0C, DW_EH_PE_pcrel = 0x10, DW_EH_PE_indirect = 0x80, DW_EH_PE_omit = 0xFF,
};

static uintptr_t read_uleb128(const uint8_t** p) {
    uintptr_t result = 0;
    unsigned shift = 0;
    uint8_t byte;
    do {
        byte = *(*p)++;
        result |= (uintptr_t)(byte & 0x7F) << shift;
        shift += 7;
    } while (byte & 0x80);
    return result;
}

static intptr_t read_sleb128(const uint8_t** p) {
    uintptr_t result = 0;
    unsigned shift = 0;
    uint8_t byte;
    do {
        byte = *(*p)++;
        result |= (uintptr_t)(byte & 0x7F) << shift;
        shift += 7;
    } while (byte & 0x80);
    if ((byte & 0x40) && shift < sizeof(result) * 8) result |= ~(uintptr_t)0 << shift;
    return (intptr_t)result;
}

static size_t encoded_size(uint8_t encoding) {
    switch (encoding & 0x0F) {
        case DW_EH_PE_absptr: return sizeof(uintptr_t);
        case DW_EH_PE_udata2: case DW_EH_PE_sdata2: return 2;
        case DW_EH_PE_udata4: case DW_EH_PE_sdata4: return 4;
        case DW_EH_PE_udata8: case DW_EH_PE_sdata8: return 8;
        default: abort();
    }
}

static uintptr_t read_encoded(const uint8_t** p, uint8_t encoding) {
    if (encoding == DW_EH_PE_omit) return 0;
    const uint8_t* start = *p;
    uintptr_t result;
    switch (encoding & 0x0F) {
        case DW_EH_PE_absptr: memcpy(&result, *p, sizeof result); *p += sizeof result; break;
        case DW_EH_PE_uleb128: result = read_uleb128(p); break;
        case DW_EH_PE_sleb128: result = (uintptr_t)read_sleb128(p); break;
        case DW_EH_PE_udata2: { uint16_t v; memcpy(&v, *p, 2); *p += 2; result = v; break; }
        case DW_EH_PE_udata4: { uint32_t v; memcpy(&v, *p, 4); *p += 4; result = v; break; }
        case DW_EH_PE_udata8: { uint64_t v; memcpy(&v, *p, 8); *p += 8; result = (uintptr_t)v; break; }
        case DW_EH_PE_sdata2: { int16_t v; memcpy(&v, *p, 2); *p += 2; result = (uintptr_t)(intptr_t)v; break; }
        case DW_EH_PE_sdata4: { int32_t v; memcpy(&v, *p, 4); *p += 4; result = (uintptr_t)(intptr_t)v; break; }
        case DW_EH_PE_sdata8: { int64_t v; memcpy(&v, *p, 8); *p += 8; result = (uintptr_t)v; break; }
        default: abort();
    }
    if (result != 0) {
        if ((encoding & 0x70) == DW_EH_PE_pcrel) result += (uintptr_t)start;
        else if ((encoding & 0x70) != 0) abort();  // textrel/datarel/funcrel: not emitted on our targets
        if (encoding & DW_EH_PE_indirect) result = *(const uintptr_t*)result;
    }
    return result;
}

static int is_instance_of(void* exception, const csrt_MethodTable* type) {
    for (const csrt_MethodTable* mt = ((csrt_Object*)exception)->mt; mt; mt = mt->base)
        if (mt == type) return 1;
    return 0;
}

_Unwind_Reason_Code csrt_personality(int version, _Unwind_Action actions, uint64_t exception_class,
                                     struct _Unwind_Exception* unwind, struct _Unwind_Context* context) {
    if (version != 1) return _URC_FATAL_PHASE1_ERROR;
    if (exception_class != CSRT_EXCEPTION_CLASS) return _URC_CONTINUE_UNWIND;  // foreign (e.g. C++)

    const uint8_t* lsda = _Unwind_GetLanguageSpecificData(context);
    if (!lsda) return _URC_CONTINUE_UNWIND;

    uintptr_t function_start = _Unwind_GetRegionStart(context);
    int ip_before = 0;
    uintptr_t ip = _Unwind_GetIPInfo(context, &ip_before);
    if (!ip_before) ip--;  // IP is the return address: look up the call instruction itself

    // LSDA header
    const uint8_t* p = lsda;
    uint8_t lp_start_encoding = *p++;
    uintptr_t lp_start = lp_start_encoding == DW_EH_PE_omit ? function_start : read_encoded(&p, lp_start_encoding);
    uint8_t ttype_encoding = *p++;
    const uint8_t* class_info = NULL;
    if (ttype_encoding != DW_EH_PE_omit) {
        uintptr_t offset = read_uleb128(&p);
        class_info = p + offset;
    }
    uint8_t call_site_encoding = *p++;
    uintptr_t call_site_table_length = read_uleb128(&p);
    const uint8_t* call_site_table_end = p + call_site_table_length;
    const uint8_t* action_table = call_site_table_end;

    // Find this call site.
    uintptr_t landing_pad = 0, action = 0;
    int found = 0;
    while (p < call_site_table_end) {
        uintptr_t start = read_encoded(&p, call_site_encoding);
        uintptr_t length = read_encoded(&p, call_site_encoding);
        uintptr_t pad = read_encoded(&p, call_site_encoding);
        uintptr_t act = read_uleb128(&p);
        if (ip < function_start + start) break;
        if (ip < function_start + start + length) {
            landing_pad = pad;
            action = act;
            found = 1;
            break;
        }
    }
    if (!found) return _URC_CONTINUE_UNWIND;  // no entry: call can't throw here per LLVM; keep unwinding
    if (landing_pad == 0) return _URC_CONTINUE_UNWIND;

    void* exception = ((csrt_Throw*)((char*)unwind - offsetof(csrt_Throw, unwind)))->exception;

    if (actions & _UA_SEARCH_PHASE) {
        if (action == 0) return _URC_CONTINUE_UNWIND;  // cleanup only
        const uint8_t* record = action_table + action - 1;
        for (;;) {
            intptr_t type_index = read_sleb128(&record);
            const uint8_t* displacement_at = record;
            intptr_t displacement = read_sleb128(&record);
            if (type_index > 0 && class_info) {
                const uint8_t* entry = class_info - (uintptr_t)type_index * encoded_size(ttype_encoding);
                const csrt_MethodTable* type = (const csrt_MethodTable*)read_encoded(&entry, ttype_encoding);
                if (!type || is_instance_of(exception, type)) return _URC_HANDLER_FOUND;
            }
            if (displacement == 0) break;
            record = displacement_at + displacement;
        }
        return _URC_CONTINUE_UNWIND;
    }

    // Cleanup phase: enter the landing pad; the compiled code dispatches on the exception type.
    _Unwind_SetGR(context, __builtin_eh_return_data_regno(0), (uintptr_t)unwind);
    _Unwind_SetGR(context, __builtin_eh_return_data_regno(1), 0);
    _Unwind_SetIP(context, lp_start + landing_pad);
    return _URC_INSTALL_CONTEXT;
}
