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
//  * wasm32 has no unwinder (docs/adr/0011-wasm32.md): throwing stores the exception in
//    csrt_pending_exception and returns; csharpc's ExceptionLowering makes every caller test it and
//    branch to its landing-pad code or return. The same handler code runs; see the __wasm__ blocks.
#define _GNU_SOURCE
#include "csrt.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#ifndef __wasm__
#include <setjmp.h>
#include <unwind.h>
#endif
#ifdef _WIN32
#include <windows.h>
#endif

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
DEF_EXC(System_PlatformNotSupportedException, "System.PlatformNotSupportedException", &csrt_MT_System_NotSupportedException.header)
DEF_EXC(System_NotImplementedException, "System.NotImplementedException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_NullReferenceException, "System.NullReferenceException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_IndexOutOfRangeException, "System.IndexOutOfRangeException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_ArithmeticException, "System.ArithmeticException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_DivideByZeroException, "System.DivideByZeroException", &csrt_MT_System_ArithmeticException.header)
DEF_EXC(System_OverflowException, "System.OverflowException", &csrt_MT_System_ArithmeticException.header)
DEF_EXC(System_InvalidCastException, "System.InvalidCastException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_ArrayTypeMismatchException, "System.ArrayTypeMismatchException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_FormatException, "System.FormatException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_Threading_ThreadStateException, "System.Threading.ThreadStateException", &csrt_MT_System_SystemException.header)
DEF_EXC(System_Threading_SynchronizationLockException, "System.Threading.SynchronizationLockException",
        &csrt_MT_System_SystemException.header)
DEF_EXC(System_Threading_ThreadInterruptedException, "System.Threading.ThreadInterruptedException",
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

// Not inlined: exception formatting is cold, and each inlined copy is two allocations' worth of code.
__attribute__((noinline)) static csrt_String* concat_utf8(csrt_String* a, const char* b) { return csrt_string_concat(a, csrt_string_from_utf8(b)); }

static bool is_exception_of(const csrt_Exception* e, const csrt_MethodTable* type) {
    for (const csrt_MethodTable* mt = e->mt; mt; mt = mt->base)
        if (mt == type) return true;
    return false;
}

/// FileNotFoundException keeps its FileName in the param_name slot (ArgumentException's ParamName).
static bool is_file_not_found(const csrt_Exception* e) {
    for (const csrt_MethodTable* mt = e->mt; mt; mt = mt->base)
        if (strcmp(mt->name, "System.IO.FileNotFoundException") == 0) return true;
    return false;
}

// Exception.Message (+ ArgumentException's " (Parameter 'x')" suffix).
csrt_String* csrt_exception_get_message(void* exception) {
    csrt_Exception* e = exception;
    csrt_String* message = e->message;
    if (!message) {
        char buffer[512];
        snprintf(buffer, sizeof buffer, "Exception of type '%s' was thrown.", e->mt->name);
        message = csrt_string_from_utf8(buffer);
    }
    if (e->param_name && e->param_name->length > 0 && is_exception_of(e, &csrt_MT_System_ArgumentException.header)) {
        message = concat_utf8(message, " (Parameter '");
        message = csrt_string_concat(message, e->param_name);
        message = concat_utf8(message, "')");
    }
    if (e->actual_value) {  // ArgumentOutOfRangeException.Message: "...\nActual value was X."
        message = concat_utf8(message, CSRT_NEWLINE "Actual value was ");
        message = csrt_string_concat(message, e->actual_value);
        message = concat_utf8(message, ".");
    }
    return message;
}

/// Exception.HResult: the value set, else the default .NET's constructors assign for the nearest type that
/// has one (HResults.cs), walking the method-table chain by name.
int32_t csrt_exception_get_hresult(void* exception) {
    if (!exception) csrt_throw_null_reference();
    csrt_Exception* e = exception;
    if (e->hresult) return e->hresult;
    static const struct { const char* name; uint32_t hresult; } defaults[] = {
        { "System.ArgumentNullException", 0x80004003 }, { "System.ArgumentOutOfRangeException", 0x80131502 },
        { "System.ArgumentException", 0x80070057 }, { "System.DivideByZeroException", 0x80020012 },
        { "System.OverflowException", 0x80131516 }, { "System.ArithmeticException", 0x80070216 },
        { "System.FormatException", 0x80131537 }, { "System.IndexOutOfRangeException", 0x80131508 },
        { "System.InvalidCastException", 0x80004002 }, { "System.ObjectDisposedException", 0x80131622 },
        { "System.InvalidOperationException", 0x80131509 }, { "System.PlatformNotSupportedException", 0x80131539 },
        { "System.NotSupportedException", 0x80131515 }, { "System.NotImplementedException", 0x80004001 },
        { "System.NullReferenceException", 0x80004003 }, { "System.OperationCanceledException", 0x8013153B },
        { "System.TimeoutException", 0x80131505 }, { "System.Collections.Generic.KeyNotFoundException", 0x80131577 },
        { "System.IO.FileNotFoundException", 0x80070002 }, { "System.IO.DirectoryNotFoundException", 0x80070003 },
        { "System.IO.PathTooLongException", 0x800700CE }, { "System.IO.EndOfStreamException", 0x80070026 },
        { "System.IO.FileLoadException", 0x80131621 }, { "System.IO.IOException", 0x80131620 },
        { "System.UnauthorizedAccessException", 0x80070005 }, { "System.OutOfMemoryException", 0x8007000E },
        { "System.ArrayTypeMismatchException", 0x80131503 }, { "System.RankException", 0x80131517 },
        { "System.TypeInitializationException", 0x80131534 }, { "System.Reflection.TargetInvocationException", 0x80131604 },
        { "System.Threading.SemaphoreFullException", 0x8013152B }, { "System.Threading.SynchronizationLockException", 0x80131518 },
        { "System.Threading.ThreadInterruptedException", 0x80131519 }, { "System.Threading.ThreadStateException", 0x80131520 },
        { "System.InsufficientExecutionStackException", 0x80131578 }, { "System.ApplicationException", 0x80131600 },
        { "System.SystemException", 0x80131501 }, { "System.Exception", 0x80131500 },
    };
    for (const csrt_MethodTable* mt = e->mt; mt; mt = mt->base)
        for (size_t i = 0; i < sizeof defaults / sizeof defaults[0]; i++)
            if (strcmp(mt->name, defaults[i].name) == 0) return (int32_t)defaults[i].hresult;
    return (int32_t)0x80131500;
}

void csrt_exception_set_hresult(void* exception, int32_t hresult) {
    if (!exception) csrt_throw_null_reference();
    ((csrt_Exception*)exception)->hresult = hresult;
}

void csrt_exception_set_token(void* exception, void* token) { ((csrt_Exception*)exception)->token = token; }
void* csrt_exception_get_token(void* exception) {
    if (!exception) csrt_throw_null_reference();
    return ((csrt_Exception*)exception)->token;
}

void csrt_exception_set_actual_value(void* exception, csrt_String* actual_value) {
    ((csrt_Exception*)exception)->actual_value = actual_value;
}

// ObjectDisposedException(objectName[, message]): Message is the message (null = the default text)
// plus "\nObject name: 'X'." when the name is not empty. Exceptions are immutable, so it is built once.
csrt_String* csrt_object_disposed_message(csrt_String* message, csrt_String* object_name) {
    if (!message) message = csrt_string_from_utf8("Cannot access a disposed object.");
    if (!object_name || object_name->length == 0) return message;
    message = concat_utf8(message, CSRT_NEWLINE "Object name: '");
    message = csrt_string_concat(message, object_name);
    return concat_utf8(message, "'.");
}

typedef csrt_String* (*csrt_MessageFn)(void*);

static csrt_String* virtual_message(void* exception) {
    const void* const* vtable = (const void* const*)(((csrt_Object*)exception)->mt + 1);
    return ((csrt_MessageFn)vtable[CSRT_SLOT_GET_MESSAGE])(exception);
}

// Exception.ToString(): "Type: Message\n ---> Inner\n   --- End of ... ---\n   at ..." (the stack
// trace only when the exception was thrown and the program records traces).
// The trace formatter, set by csrt_trace_init: only programs that record traces call that, so in the
// others (whose exception vtables still reach ToString) LTO drops the formatter and its tables.
static csrt_String* (*s_trace_text)(void* exception);

static csrt_String* (*s_aggregate_text)(csrt_Exception* e, csrt_String* s);

csrt_String* csrt_exception_to_string(void* exception) {
    csrt_Exception* e = exception;
    csrt_String* s = csrt_string_from_utf8(e->mt->name);
    csrt_String* message = virtual_message(exception);
    if (message && message->length > 0) s = csrt_string_concat(concat_utf8(s, ": "), message);
    if (is_file_not_found(e)) {
        // FileNotFoundException.ToString(): "File name: '...'", and the inner exception without the end line.
        if (e->param_name && e->param_name->length > 0)
            s = concat_utf8(csrt_string_concat(concat_utf8(s, CSRT_NEWLINE "File name: '"), e->param_name), "'");
        if (e->inner) s = csrt_string_concat(concat_utf8(s, CSRT_NEWLINE " ---> "), csrt_object_to_string(e->inner));
    } else if (e->inner) {
        s = csrt_string_concat(concat_utf8(s, CSRT_NEWLINE " ---> "), csrt_object_to_string(e->inner));
        s = concat_utf8(s, CSRT_NEWLINE "   --- End of inner exception stack trace ---");
    }
    csrt_String* trace = s_trace_text ? s_trace_text(exception) : NULL;
    if (trace) s = csrt_string_concat(concat_utf8(s, CSRT_NEWLINE), trace);
    if (e->inners && s_aggregate_text) s = s_aggregate_text(e, s);
    return s;
}

// ---------------------------------------------------------------------------------------------
// Stack traces (docs/adr/0012-stack-traces.md)
//
// csharpc gives every call site of the program's own code that may throw a landing pad of its own
// whose first catch clause is the site's descriptor (method, file, line; csrt_TraceSite), a
// compiler-emitted constant the personality tells from a method table by its magic number. During
// the search phase the personality walks the frames from the throw point up to the catching frame --
// exactly the frame list .NET records -- and appends each descriptor it meets to the exception;
// inlined code carries its callers' clauses too, so inlined frames are listed. Descriptors never
// select a handler and the pads have no cleanup of their own, so phase 2 doesn't stop in frames that
// only record. Programs that never look at a stack trace have neither the clauses nor the tables.
//
// An exception re-raised from a frame already recorded (finally blocks and non-matching handlers
// rethrow, `throw;`) must not list it again: the trace keeps the highest frame (CFA) recorded, and
// the search records only frames above it. An await, an iterator's MoveNext and
// ExceptionDispatchInfo continue on another part of the stack: csrt_trace_new_stack forgets it.
//
// wasm32 has no unwinder: the landing-pad code itself records (csrt_trace_frame), as
// ExceptionLowering branches to every pad on its way out.
//
// Frames: trace = an array of { count, frame, site*... }; a null site is the "--- End of stack trace
// from previous location ---" line ExceptionDispatchInfo.Throw() adds.
// ---------------------------------------------------------------------------------------------
#define CSRT_TRACE_SITE_MAGIC 0xC5A7AC5E7A1E5175ULL  // not a canonical address: no method table starts with it
typedef struct { uint64_t magic; int32_t method, file, line, unused; } csrt_TraceSite;  // file < 0: no location
typedef struct {
    const char* const* methods;  // "Namespace.Type.Method(Int32 x)", formatted like .NET's StackTrace
    const char* const* files;    // document paths, as the PDB records them
} csrt_TraceTables;

// preset: the frames are library frames set before the throw (csrt_trace_preset), kept by it.
typedef struct { int32_t count; int32_t preset; uint64_t frame; const csrt_TraceSite* sites[]; } TraceData;
enum { TRACE_HEADER = (int32_t)(sizeof(TraceData) / sizeof(void*)) };

static const csrt_TraceTables* s_trace;
#define TRACE_PREVIOUS_LOCATION ((const csrt_TraceSite*)0)

void csrt_trace_init(const void* tables) { s_trace = tables; s_trace_text = csrt_exception_get_stack_trace; }

static TraceData* trace_data(const csrt_Array* trace) { return trace ? (TraceData*)trace->data : NULL; }
static int32_t trace_count(const csrt_Array* trace) { return trace ? trace_data(trace)->count : 0; }

static csrt_Array* trace_copy(const csrt_Array* trace, int32_t capacity) {
    csrt_Array* copy = csrt_new_array_primitive(sizeof(void*), TRACE_HEADER + capacity);
    if (trace) memcpy(copy->data, trace->data, sizeof(TraceData) + sizeof(void*) * (size_t)trace_count(trace));
    return copy;
}

static void trace_append(csrt_Exception* e, const csrt_TraceSite* site) {
    int32_t count = trace_count(e->trace);
    if (!e->trace || TRACE_HEADER + count >= e->trace->length) e->trace = trace_copy(e->trace, 2 * count + 8);
    TraceData* data = trace_data(e->trace);
    data->sites[count] = site;
    data->count = count + 1;
}

/// `throw e`: a new list of frames (unless library frames were just preset for this throw).
static void trace_reset(csrt_Exception* e) {
    // s_trace is only written by csrt_trace_init: in programs without traces LTO folds it to null and
    // drops this from every throw site (which wasm32 inlines).
    if (!s_trace || !e->trace) return;
    TraceData* data = trace_data(e->trace);
    if (!data->preset) data->count = 0;
    data->preset = 0;
    data->frame = 0;
}

void csrt_trace_new_stack(void* exception) {
    csrt_Exception* e = exception;
    if (e && e->trace) trace_data(e->trace)->frame = 0;
}

/// The corelib's stand-in for the frames .NET's own library code lists below the program's (e.g.
/// "System.Collections.Generic.Dictionary`2.get_Item(TKey key)"): the next throw of the exception
/// starts its trace with them (CoreIntrinsics.LibraryFrames).
/// The same for an exception in flight (a corelib `when` filter): append them.
void csrt_trace_append_library(void* exception, const void* const* sites, int32_t count) {
    if (!s_trace || !exception) return;
    for (int32_t i = 0; i < count; i++) trace_append(exception, sites[i]);
}

void csrt_trace_preset(void* exception, const void* const* sites, int32_t count) {
    csrt_Exception* e = exception;
    if (!s_trace || !e) return;
    e->trace = trace_copy(NULL, count + 8);
    TraceData* data = trace_data(e->trace);
    memcpy(data->sites, sites, sizeof(void*) * (size_t)count);
    data->count = count;
    data->preset = 1;
}

typedef struct { char* data; size_t length, capacity; } TraceText;

static void text_append(TraceText* t, const char* s, size_t n) {
    if (t->length + n > t->capacity) {
        size_t capacity = t->capacity ? t->capacity : 256;
        while (capacity < t->length + n) capacity *= 2;
        char* grown = realloc(t->data, capacity);
        if (!grown) abort();
        t->data = grown;
        t->capacity = capacity;
    }
    memcpy(t->data + t->length, s, n);
    t->length += n;
}

static void text_append_str(TraceText* t, const char* s) { text_append(t, s, strlen(s)); }

/// Exception.StackTrace: null until the exception is thrown (or when no frame was recorded).
csrt_String* csrt_exception_get_stack_trace(void* exception) {
    csrt_Exception* e = exception;
    int32_t count = trace_count(e->trace);
    if (count == 0 || !s_trace) return NULL;
    const csrt_TraceSite* const* entries = trace_data(e->trace)->sites;
    TraceText text = { 0 };
    for (int32_t i = 0; i < count; i++) {
        if (i > 0) text_append_str(&text, CSRT_NEWLINE);
        const csrt_TraceSite* site = entries[i];
        if (site == TRACE_PREVIOUS_LOCATION) { text_append_str(&text, "--- End of stack trace from previous location ---"); continue; }
        text_append_str(&text, "   at ");
        text_append_str(&text, s_trace->methods[site->method]);
        if (site->file >= 0) {
            char number[24];
            char* digits = csrt_itoa_i64(site->line, number + sizeof number);
            text_append_str(&text, " in ");
            text_append_str(&text, s_trace->files[site->file]);
            text_append_str(&text, ":line ");
            text_append(&text, digits, (size_t)(number + sizeof number - digits));
        }
    }
    csrt_String* result = csrt_string_from_utf8_n(text.data, text.length);
    free(text.data);
    return result;
}

/// ExceptionDispatchInfo.Capture: a copy of the frames recorded so far (null: none).
void* csrt_exception_capture_trace(void* exception) {
    csrt_Array* trace = ((csrt_Exception*)exception)->trace;
    if (trace_count(trace) == 0) return NULL;
    return trace_copy(trace, trace_count(trace));
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
#ifndef __wasm__
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
#endif

#ifdef _WIN32
// The NullReferenceException last raised for a null dereference (csrt_throw_null_reference). .NET
// gets those from an access violation, and when one goes unhandled on Windows the process ends with
// the original exception code (0xC0000005) rather than the CLR's 0xE0434352.
static void* volatile s_implicit_null_reference;
static volatile DWORD s_exit_status = 0xE0434352;  // "CCR" | 0xE0000000: the CLR's SEH code
#endif

_Noreturn void csrt_fail_unhandled(void) {
    fflush(stdout);
    fflush(stderr);
#ifdef _WIN32
    TerminateProcess(GetCurrentProcess(), s_exit_status);
#endif
    abort();
}

_Noreturn static void unhandled(void* exception) {
    fflush(stdout);
    fputs("Unhandled exception. ", stderr);
    csrt_stderr_write_string(csrt_object_to_string(exception));
    fputs(CSRT_NEWLINE, stderr);
#ifdef _WIN32
    if (exception && exception == s_implicit_null_reference) s_exit_status = 0xC0000005;  // STATUS_ACCESS_VIOLATION
#endif
    csrt_fail_unhandled();
}

#ifndef __wasm__
static _Unwind_Reason_Code unhandled_stop(int version, _Unwind_Action actions, uint64_t exception_class,
                                          struct _Unwind_Exception* unwind, struct _Unwind_Context* context, void* parameter) {
    (void)version; (void)exception_class; (void)context; (void)parameter;
    if (actions & _UA_END_OF_STACK)
        unhandled(((csrt_Throw*)((char*)unwind - offsetof(csrt_Throw, unwind)))->exception);
    return _URC_NO_REASON;
}
#endif

static void* new_exception(const csrt_ExceptionMethodTable* mt, const char* message) {
    void* e = csrt_alloc(&mt->header, sizeof(csrt_Exception));
    csrt_exception_init(e, message ? csrt_string_from_utf8(message) : NULL, NULL, NULL);
    return e;
}

#ifndef __wasm__
// Exception traps (thread-pool workers): an exception no C# handler catches on this thread runs
// its finally blocks (forced unwind) and then returns to the innermost csrt_trap_call instead of
// terminating the process, so Parallel.For can rethrow it on the calling thread.
#ifdef _WIN32
// mingw-w64's longjmp unwinds the stack with RtlUnwindEx; the trap jumps from inside a forced unwind,
// so it uses the plain register-restoring builtins instead.
typedef struct { void* env[5]; uintptr_t frame; void* exception; } csrt_Trap;
#define TRAP_SETJMP(env) __builtin_setjmp(env)
#define TRAP_LONGJMP(env) __builtin_longjmp(env, 1)
#else
typedef struct { jmp_buf env; uintptr_t frame; void* exception; } csrt_Trap;
#define TRAP_SETJMP(env) setjmp(env)
#define TRAP_LONGJMP(env) longjmp(env, 1)
#endif
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
        TRAP_LONGJMP(trap->env);
    }
    return _URC_NO_REASON;
}

/// Runs fn(arg); returns NULL, or the exception it threw that nothing on this thread caught.
__attribute__((noinline)) void* csrt_trap_call(void (*fn)(void*), void* arg) {
    csrt_Trap trap;
    trap.exception = NULL;
    trap.frame = (uintptr_t)__builtin_frame_address(0);
    csrt_Trap* volatile outer = tl_trap;
    if (TRAP_SETJMP(trap.env) == 0) {
        tl_trap = &trap;
        fn(arg);
        tl_trap = outer;
        return NULL;
    }
    tl_trap = outer;
    return trap.exception;
}
#endif

/// An exception that escaped an `async void` method: crash like .NET (the finally blocks already ran).
void csrt_unhandled_exception(void* exception) { unhandled(exception); }

// AggregateException.ToString(): every inner exception other than InnerException (shown above) follows,
// " ---> (Inner Exception #i) <its ToString()><---" and a new line. Installed by whatever creates an
// aggregate with a list (csrt_aggregate_exception_new, csrt_exception_set_inners), so programs without
// one don't link it.
static csrt_String* aggregate_text(csrt_Exception* e, csrt_String* s) {
    if (!is_exception_of(e, &csrt_MT_System_AggregateException.header)) return s;
    void** list = (void**)e->inners->data;
    for (int64_t i = 0; i < e->inners->length; i++) {
        if (list[i] == e->inner) continue;
        char digits[24], *d = digits + sizeof digits; // no printf: it would link stdio's formatter
        *--d = 0;
        int64_t n = i;
        do *--d = (char)('0' + n % 10); while ((n /= 10) != 0);
        s = concat_utf8(concat_utf8(concat_utf8(s, CSRT_NEWLINE " ---> (Inner Exception #"), d), ") ");
        s = csrt_string_concat(s, csrt_object_to_string(list[i]));
        s = concat_utf8(s, "<---" CSRT_NEWLINE);
    }
    return s;
}

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
    csrt_Array* list = csrt_new_array(sizeof(void*), count);
    memcpy(list->data, inners, sizeof(void*) * (size_t)count);
    ((csrt_Exception*)e)->inners = list;
    s_aggregate_text = aggregate_text;
    return e;
}

/// new AggregateException(..., innerExceptions) from compiled code: the Exception[] it was given.
void csrt_exception_set_inners(void* exception, csrt_Array* inners) {
    ((csrt_Exception*)exception)->inners = inners;
    s_aggregate_text = aggregate_text;
}

/// AggregateException.InnerExceptions (corelib AggregateExceptionSupport): count and elements; an
/// aggregate without a list (none of csharpc's make one) reports its InnerException alone.
int32_t csrt_exception_inner_count(void* exception) {
    csrt_Exception* e = exception;
    return e->inners ? (int32_t)e->inners->length : e->inner ? 1 : 0;
}

void* csrt_exception_inner_at(void* exception, int32_t index) {
    csrt_Exception* e = exception;
    return e->inners ? ((void**)e->inners->data)[index] : e->inner;
}

/// An OperationCanceledException (or a subclass) escaping an async method cancels its task.
bool csrt_is_operation_canceled(void* exception) {
    return csrt_isinst_class(exception, &csrt_MT_System_OperationCanceledException.header) != NULL;
}

void* csrt_task_canceled_exception_new(void) {
    csrt_Exception* e = new_exception(&csrt_MT_System_Threading_Tasks_TaskCanceledException, "A task was canceled.");
    return e;
}

void* csrt_operation_canceled_exception_new(void* token) {
    csrt_Exception* e = new_exception(&csrt_MT_System_OperationCanceledException, "The operation was canceled.");
    e->token = token;
    return e;
}

void* csrt_object_disposed_exception_new(const char* message) {
    return new_exception(&csrt_MT_System_ObjectDisposedException, message);
}

#ifdef __wasm__
/// The exception being propagated (null: none). Every call that may throw is followed by a test of
/// this variable (ExceptionLowering), so it is read far more often than written. A static root.
void* csrt_pending_exception;

static CSRT_THROWS void raise_exception(void* exception) {
    csrt_pending_exception = exception;
    // Debugging aid: wasm has no unwinder to show where an exception came from, but a trap prints
    // the engine's backtrace (wasmtime: with function names unless the module was stripped).
    if (getenv("CSRT_TRAP_ON_THROW")) __builtin_trap();
}

/// The landing-pad code (wasm: reached by a branch after the call that threw): take the exception.
void* csrt_begin_catch(void* unused) {
    (void)unused;
    void* exception = csrt_pending_exception;
    csrt_pending_exception = NULL;
    return exception;
}

/// ExceptionLowering leaves the calls in these functions alone: they look at the pending
/// exception themselves (they are the only C code that catches).
void* csrt_trap_call(void (*fn)(void*), void* arg) {
    fn(arg);
    return csrt_begin_catch(NULL);
}

/// main's propagate block (ExceptionLowering): the exception reached the top of the stack. `used`:
/// nothing references it until that pass runs, after the runtime's dead code is dropped.
__attribute__((used)) _Noreturn void csrt_unhandled_pending(void) { unhandled(csrt_begin_catch(NULL)); }
#else
static CSRT_THROWS void raise_exception(void* exception) {
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

// Called first thing in a landing pad: unwrap the managed exception object.
void* csrt_begin_catch(void* unwind_exception) {
    struct _Unwind_Exception* unwind = unwind_exception;
    csrt_Throw* t = (csrt_Throw*)((char*)unwind - offsetof(csrt_Throw, unwind));
    void* exception = t->exception;
    free(t);
    s_in_flight = NULL;
    return exception;
}
#endif

// `throw e`: a new stack trace starts here, even for an exception thrown before (as in .NET).
void csrt_throw_object(void* exception) {
    if (!exception)  // `throw null;` throws a NullReferenceException
        exception = new_exception(&csrt_MT_System_NullReferenceException, "Object reference not set to an instance of an object.");
    trace_reset(exception);
    raise_exception(exception);
}

// `throw;`, an exception leaving a handler region, `await` of a faulted task: the frames recorded so
// far stay, and the frames it passes through next are appended.
void csrt_rethrow(void* exception) { raise_exception(exception); }

/// ExceptionDispatchInfo.Throw(): the frames captured by Capture(), then .NET's marker line, then the
/// frames from here on.
void csrt_exception_throw_captured(void* exception, void* captured) {
    csrt_Exception* e = exception;
    csrt_Array* frames = captured;
    if (frames) {
        e->trace = trace_copy(frames, trace_count(frames) + 8);
        trace_append(e, TRACE_PREVIOUS_LOCATION);
        trace_data(e->trace)->frame = 0;  // Throw() runs elsewhere on the stack than the capture
    } else trace_reset(e);
    raise_exception(exception);
}

#ifdef __wasm__
// wasm32: a site's landing-pad code ran, the exception is passing through (or caught in) this frame.
void csrt_trace_frame(void* unwind_exception, const void* site) {
    (void)unwind_exception;
    if (site && s_trace && csrt_pending_exception) trace_append(csrt_pending_exception, site);
}
#endif

// An exception leaves a finally block (or a using's Dispose) at `site`. Run on the normal path
// (parent not null), CoreCLR lists the method a second time, for its own frame below the funclet's.
void csrt_trace_finally(void* unwind_exception, const void* site, const void* parent) {
    if (!s_trace) return;
#ifdef __wasm__
    (void)unwind_exception;
    csrt_Exception* e = csrt_pending_exception;
    if (!e) return;
    trace_append(e, site);
    if (parent) trace_append(e, parent);
#else
    csrt_Exception* e = ((csrt_Throw*)((char*)unwind_exception - offsetof(csrt_Throw, unwind)))->exception;
    if (!parent) return;
    // The search phase has listed the whole stack by now: the parent goes right after this site.
    trace_append(e, parent);
    TraceData* data = trace_data(e->trace);
    int32_t at = data->count - 2;
    while (at >= 0 && data->sites[at] != site) at--;
    memmove(&data->sites[at + 2], &data->sites[at + 1], sizeof(void*) * (size_t)(data->count - 2 - at));
    data->sites[at + 1] = parent;
#endif
}

// Runtime-raised exceptions, with .NET's exact messages.
void csrt_throw_null_reference(void) {
    void* e = new_exception(&csrt_MT_System_NullReferenceException, "Object reference not set to an instance of an object.");
#ifdef _WIN32
    s_implicit_null_reference = e;
#endif
    csrt_throw_object(e);
}
void csrt_throw_index_out_of_range(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_IndexOutOfRangeException, "Index was outside the bounds of the array."));
}
/// Array.GetLength/GetUpperBound/GetLowerBound with a dimension the array doesn't have.
void csrt_throw_rank_out_of_range(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_IndexOutOfRangeException, "Array does not have that many dimensions."));
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
void csrt_throw_array_type_mismatch(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_ArrayTypeMismatchException, "Attempted to access an element as a type incompatible with the array."));
}
void csrt_throw_array_copy_mismatch(void) {
    csrt_throw_object(new_exception(&csrt_MT_System_ArrayTypeMismatchException, "Source array type cannot be assigned to destination array type."));
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
    &csrt_MT_System_NotSupportedException, &csrt_MT_System_PlatformNotSupportedException, &csrt_MT_System_NotImplementedException,
    &csrt_MT_System_NullReferenceException, &csrt_MT_System_IndexOutOfRangeException,
    &csrt_MT_System_ArithmeticException, &csrt_MT_System_DivideByZeroException, &csrt_MT_System_OverflowException,
    &csrt_MT_System_InvalidCastException, &csrt_MT_System_FormatException,
    &csrt_MT_System_Collections_Generic_KeyNotFoundException,
    &csrt_MT_System_Threading_ThreadStateException, &csrt_MT_System_Threading_SynchronizationLockException,
    &csrt_MT_System_Threading_ThreadInterruptedException,
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

#ifndef __wasm__
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

#ifdef _WIN32
// On Windows (MinGW, SEH unwind tables) LLVM registers the personality as each function's SEH
// language handler. libunwind's _GCC_specific_handler adapts the SEH callback to the Itanium
// personality protocol, as libgcc's __gxx_personality_seh0 does for C++.
EXCEPTION_DISPOSITION _GCC_specific_handler(PEXCEPTION_RECORD, void*, PCONTEXT, PDISPATCHER_CONTEXT, _Unwind_Personality_Fn);
static _Unwind_Reason_Code personality(int version, _Unwind_Action actions, uint64_t exception_class,
                                       struct _Unwind_Exception* unwind, struct _Unwind_Context* context);

EXCEPTION_DISPOSITION csrt_personality(PEXCEPTION_RECORD record, void* frame, PCONTEXT context, PDISPATCHER_CONTEXT dispatch) {
    return _GCC_specific_handler(record, frame, context, dispatch, personality);
}

static _Unwind_Reason_Code personality(int version, _Unwind_Action actions, uint64_t exception_class,
                                       struct _Unwind_Exception* unwind, struct _Unwind_Context* context) {
#else
_Unwind_Reason_Code csrt_personality(int version, _Unwind_Action actions, uint64_t exception_class,
                                     struct _Unwind_Exception* unwind, struct _Unwind_Context* context) {
#endif
    if (version != 1) return _URC_FATAL_PHASE1_ERROR;
    if (exception_class != CSRT_EXCEPTION_CLASS) return _URC_CONTINUE_UNWIND;  // foreign (e.g. C++)

    const uint8_t* lsda = (const uint8_t*)_Unwind_GetLanguageSpecificData(context);  // uintptr_t in libunwind
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
        // Stack traces: site clauses come first; record them unless this frame is listed already.
        csrt_Exception* e = exception;
        uint64_t frame = (uint64_t)_Unwind_GetCFA(context);
        bool record_sites = s_trace && frame > (e->trace ? trace_data(e->trace)->frame : 0);
        const uint8_t* record = action_table + action - 1;
        _Unwind_Reason_Code result = _URC_CONTINUE_UNWIND;
        for (;;) {
            intptr_t type_index = read_sleb128(&record);
            const uint8_t* displacement_at = record;
            intptr_t displacement = read_sleb128(&record);
            if (type_index > 0 && class_info) {
                const uint8_t* entry = class_info - (uintptr_t)type_index * encoded_size(ttype_encoding);
                const void* type = (const void*)read_encoded(&entry, ttype_encoding);
                if (type && *(const uint64_t*)type == CSRT_TRACE_SITE_MAGIC) {
                    if (record_sites) trace_append(e, type);
                } else if (!type || is_instance_of(exception, type)) { result = _URC_HANDLER_FOUND; break; }
            }
            if (displacement == 0) break;
            record = displacement_at + displacement;
        }
        if (record_sites && e->trace) trace_data(e->trace)->frame = frame;
        return result;
    }

    // Cleanup phase: enter the landing pad; the compiled code dispatches on the exception type.
    // Pads whose clauses only record stack-trace sites (no cleanup, no handler here) are skipped.
    if (action != 0 && !(actions & _UA_HANDLER_FRAME)) {
        bool cleanup = false;
        const uint8_t* record = action_table + action - 1;
        for (;;) {
            intptr_t type_index = read_sleb128(&record);
            const uint8_t* displacement_at = record;
            intptr_t displacement = read_sleb128(&record);
            if (type_index == 0) { cleanup = true; break; }
            if (type_index > 0 && class_info) {
                const uint8_t* entry = class_info - (uintptr_t)type_index * encoded_size(ttype_encoding);
                const void* type = (const void*)read_encoded(&entry, ttype_encoding);
                if (!type || *(const uint64_t*)type != CSRT_TRACE_SITE_MAGIC) { cleanup = true; break; }  // a catch: the code re-checks it
            }
            if (displacement == 0) break;
            record = displacement_at + displacement;
        }
        if (!cleanup) return _URC_CONTINUE_UNWIND;
    }
    _Unwind_SetGR(context, __builtin_eh_return_data_regno(0), (uintptr_t)unwind);
    _Unwind_SetGR(context, __builtin_eh_return_data_regno(1), 0);
    _Unwind_SetIP(context, lp_start + landing_pad);
    return _URC_INSTALL_CONTEXT;
}
#endif
