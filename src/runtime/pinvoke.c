// csrt P/Invoke support: library resolution (dlopen/dlsym with .NET's probing rules), last-error
// capture, and the few legacy [DllImport] marshalling helpers codegen needs. Modern [LibraryImport]
// marshalling is C# (source-generated stubs + corelib marshallers), not this file.
#define _GNU_SOURCE
#include "csrt.h"

#include <dlfcn.h>
#include <errno.h>
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

extern const csrt_ExceptionMethodTable csrt_MT_System_DllNotFoundException;
extern const csrt_ExceptionMethodTable csrt_MT_System_EntryPointNotFoundException;

static _Thread_local int s_last_pinvoke_error;

void csrt_pinvoke_clear_errno(void) { errno = 0; }
void csrt_pinvoke_save_errno(void) { s_last_pinvoke_error = errno; }
int32_t csrt_get_last_pinvoke_error(void) { return s_last_pinvoke_error; }
void csrt_set_last_pinvoke_error(int32_t error) { s_last_pinvoke_error = error; }
int32_t csrt_get_last_system_error(void) { return errno; }
void csrt_set_last_system_error(int32_t error) { errno = error; }

// ---------------------------------------------------------------------------------------------
// Library loading. .NET on Linux probes, for DllImport("foo"): foo.so, libfoo.so, foo, libfoo
// (names containing a path or a suffix are tried as given first).
// ---------------------------------------------------------------------------------------------
typedef struct { char* name; void* handle; } Library;
static Library* s_libraries;
static int s_library_count, s_library_cap;
static pthread_mutex_t s_library_lock = PTHREAD_MUTEX_INITIALIZER;

static csrt_String* message(const char* format, const char* a, const char* b) {
    char buffer[1024];
    snprintf(buffer, sizeof buffer, format, a, b);
    return csrt_string_from_utf8(buffer);
}

static void* try_open(const char* name) { return dlopen(name, RTLD_LAZY | RTLD_GLOBAL); }

void* csrt_pinvoke_load_library(const char* name, bool throw_on_error) {
    pthread_mutex_lock(&s_library_lock);
    for (int i = 0; i < s_library_count; i++)
        if (strcmp(s_libraries[i].name, name) == 0) { void* h = s_libraries[i].handle; pthread_mutex_unlock(&s_library_lock); return h; }
    void* handle = NULL;
    char candidate[1024];
    bool has_suffix = strstr(name, ".so") != NULL;
    if (strchr(name, '/') || has_suffix) handle = try_open(name);
    if (!handle) { snprintf(candidate, sizeof candidate, "%s.so", name); handle = try_open(candidate); }
    if (!handle && strncmp(name, "lib", 3) != 0) { snprintf(candidate, sizeof candidate, "lib%s.so", name); handle = try_open(candidate); }
    if (!handle) handle = try_open(name);
    if (!handle && strncmp(name, "lib", 3) != 0) { snprintf(candidate, sizeof candidate, "lib%s", name); handle = try_open(candidate); }
    if (handle) {
        if (s_library_count == s_library_cap) {
            s_library_cap = s_library_cap ? s_library_cap * 2 : 8;
            s_libraries = realloc(s_libraries, sizeof *s_libraries * (size_t)s_library_cap);
        }
        s_libraries[s_library_count++] = (Library){ strdup(name), handle };
    }
    const char* error = handle ? NULL : dlerror();
    pthread_mutex_unlock(&s_library_lock);
    if (!handle && throw_on_error)
        csrt_throw(&csrt_MT_System_DllNotFoundException.header,
                   message("Unable to load shared library '%s' or one of its dependencies. In order to help diagnose loading "
                           "problems, consider using a tool like strace. If you're using glibc, consider setting the "
                           "LD_DEBUG environment variable: \n%s", name, error ? error : ""));
    return handle;
}

/// Resolves `entry` in `library` (DllImport lazy binding); throws like .NET on failure.
void* csrt_pinvoke_resolve(const char* library, const char* entry) {
    void* handle = csrt_pinvoke_load_library(library, true);
    void* symbol = dlsym(handle, entry);
    if (!symbol)
        csrt_throw(&csrt_MT_System_EntryPointNotFoundException.header,
                   message("Unable to find an entry point named '%s' in shared library '%s'.", entry, library));
    return symbol;
}

/// A statically linked (weak) P/Invoke target that the linker didn't find.
_Noreturn void csrt_pinvoke_missing_entry(const char* library, const char* entry) {
    csrt_throw(&csrt_MT_System_EntryPointNotFoundException.header,
               message("Unable to find an entry point named '%s' in shared library '%s'.", entry, library));
}

void* csrt_native_library_get_export(void* handle, const char* entry, bool throw_on_error) {
    void* symbol = handle ? dlsym(handle, entry) : NULL;
    if (!symbol && throw_on_error)
        csrt_throw(&csrt_MT_System_EntryPointNotFoundException.header,
                   message("Unable to find an entry point named '%s' in shared library.%s", entry, ""));
    return symbol;
}

void csrt_native_library_free(void* handle) { (void)handle; /* libraries stay loaded, like .NET's default */ }

// ---------------------------------------------------------------------------------------------
// Legacy [DllImport] string marshalling: NUL-terminated native copies (UTF-8 for Ansi/UTF8,
// UTF-16 for Unicode), freed by the stub after the call. Returned native strings are copied and
// freed with free(), which is what .NET does on Unix (CoTaskMemFree == free).
// ---------------------------------------------------------------------------------------------
void* csrt_pinvoke_string_to_native(csrt_String* s, bool unicode) {
    if (!s) return NULL;
    if (unicode) {
        uint16_t* p = malloc(sizeof(uint16_t) * ((size_t)s->length + 1));
        memcpy(p, s->chars, sizeof(uint16_t) * (size_t)s->length);
        p[s->length] = 0;
        return p;
    }
    // UTF-8: at most 3 bytes per UTF-16 unit.
    unsigned char* out = malloc((size_t)s->length * 3 + 1);
    size_t n = 0;
    for (int32_t i = 0; i < s->length; i++) {
        uint32_t cp = s->chars[i];
        if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < s->length && s->chars[i + 1] >= 0xDC00 && s->chars[i + 1] <= 0xDFFF)
            cp = 0x10000 + ((cp - 0xD800) << 10) + (s->chars[++i] - 0xDC00);
        else if (cp >= 0xD800 && cp <= 0xDFFF) cp = 0xFFFD;
        if (cp < 0x80) out[n++] = (unsigned char)cp;
        else if (cp < 0x800) { out[n++] = (unsigned char)(0xC0 | (cp >> 6)); out[n++] = (unsigned char)(0x80 | (cp & 0x3F)); }
        else if (cp < 0x10000) {
            out[n++] = (unsigned char)(0xE0 | (cp >> 12)); out[n++] = (unsigned char)(0x80 | ((cp >> 6) & 0x3F));
            out[n++] = (unsigned char)(0x80 | (cp & 0x3F));
        } else {
            out[n++] = (unsigned char)(0xF0 | (cp >> 18)); out[n++] = (unsigned char)(0x80 | ((cp >> 12) & 0x3F));
            out[n++] = (unsigned char)(0x80 | ((cp >> 6) & 0x3F)); out[n++] = (unsigned char)(0x80 | (cp & 0x3F));
        }
    }
    out[n] = 0;
    return out;
}

void csrt_pinvoke_free(void* p) { free(p); }

csrt_String* csrt_pinvoke_string_from_native(void* p, bool unicode, bool release) {
    if (!p) return NULL;
    csrt_String* s;
    if (unicode) {
        const uint16_t* u = p;
        int32_t n = 0;
        while (u[n]) n++;
        s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)n));
        s->length = n;
        memcpy(s->chars, u, 2 * (size_t)n);
    } else {
        s = csrt_string_from_utf8(p);
    }
    if (release) free(p);
    return s;
}

/// string from a char*/length (new string(char*, int, int) and Marshal.PtrToStringUni).
csrt_String* csrt_string_new_from_utf16(const uint16_t* chars, int32_t length) {
    if (length < 0) length = 0;
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * (size_t)length));
    s->length = length;
    if (length) memcpy(s->chars, chars, 2 * (size_t)length);
    return s;
}

/// Address of a string's first char (pinning: the GC never moves objects).
const uint16_t* csrt_string_chars(csrt_String* s) { return s ? s->chars : NULL; }

// Environment.ExitCode: the process exit status when Main returns void (main shim reads it).
static int s_exit_code;
int32_t csrt_get_exit_code(void) { return s_exit_code; }
void csrt_set_exit_code(int32_t code) { s_exit_code = code; }

// Console.Error / Console.In as TextWriter/TextReader objects (corelib) go through these.
void csrt_console_error_write_string(csrt_String* s);
void csrt_console_error_write_utf8(const char* text) {
    csrt_console_error_write_string(csrt_string_from_utf8(text));
}

/// Console.In.ReadToEnd() through a stored TextReader: all of stdin as a malloc'd NUL-terminated UTF-8 buffer.
char* csrt_console_read_to_end_utf8(void) {
    size_t cap = 4096, n = 0, got;
    char* buf = malloc(cap);
    fflush(stdout);
    while ((got = fread(buf + n, 1, cap - 1 - n, stdin)) > 0) {
        n += got;
        if (n == cap - 1) { cap *= 2; buf = realloc(buf, cap); }
    }
    buf[n] = 0;
    return buf;
}

/// new string(char[] value, int startIndex, int length)
csrt_String* csrt_string_new_from_char_range(csrt_Array* chars, int32_t start, int32_t length) {
    if (!chars) csrt_throw_null_reference();
    if (start < 0 || length < 0 || (int64_t)start + length > chars->length) csrt_throw_index_out_of_range();
    return csrt_string_new_from_utf16((const uint16_t*)chars->data + start, length);
}
