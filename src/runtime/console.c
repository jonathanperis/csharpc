// csrt console input, stderr output and environment helpers.
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// One validating UTF-8 decoder for the whole runtime (csrt.c): stdin is untrusted input.
static csrt_String* utf8_to_string(const char* p, size_t n) { return csrt_string_from_utf8_n(p, n); }

#ifdef _WIN32
/// getline(3) for the UCRT, which has none.
static long getline(char** line, size_t* cap, FILE* f) {
    size_t n = 0;
    int c;
    _lock_file(f);
    while ((c = _getc_nolock(f)) != EOF) {
        if (n + 2 > *cap) {
            size_t grown = *cap ? *cap * 2 : 128;
            char* p = realloc(*line, grown);
            if (!p) break;
            *line = p;
            *cap = grown;
        }
        (*line)[n++] = (char)c;
        if (c == '\n') break;
    }
    _unlock_file(f);
    if (n == 0) return -1;
    (*line)[n] = 0;
    return (long)n;
}
#endif

/// Console.ReadLine(): null at end of input; strips "\n" / "\r\n".
csrt_String* csrt_console_read_line(void) {
    fflush(stdout);  // interactive prompts written with Console.Write must appear first
    char* line = NULL;
    size_t cap = 0;
    long n = getline(&line, &cap, stdin);
    if (n < 0) { free(line); return NULL; }
    if (n > 0 && line[n - 1] == '\n') n--;
    if (n > 0 && line[n - 1] == '\r') n--;
    csrt_String* s = utf8_to_string(line, (size_t)n);
    free(line);
    return s;
}

/// Console.Read(): next byte or -1 (ASCII input).
int32_t csrt_console_read(void) {
    fflush(stdout);
    int c = getchar();
    return c == EOF ? -1 : c;
}

/// Console.In.ReadToEnd()
csrt_String* csrt_console_read_to_end(void) {
    size_t cap = 4096, n = 0;
    char* buf = malloc(cap);
    size_t got;
    while ((got = fread(buf + n, 1, cap - n, stdin)) > 0) {
        n += got;
        if (n == cap) { cap *= 2; buf = realloc(buf, cap); }
    }
    csrt_String* s = utf8_to_string(buf, n);
    free(buf);
    return s;
}

static void write_utf16_to(FILE* f, const csrt_String* s) {
    if (!s) return;
    for (int32_t i = 0; i < s->length; i++) {
        uint32_t cp = s->chars[i];
        if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < s->length) cp = 0x10000 + ((cp - 0xD800) << 10) + (s->chars[++i] - 0xDC00);
        if (cp < 0x80) fputc((int)cp, f);
        else if (cp < 0x800) { fputc(0xC0 | (int)(cp >> 6), f); fputc(0x80 | (int)(cp & 0x3F), f); }
        else if (cp < 0x10000) { fputc(0xE0 | (int)(cp >> 12), f); fputc(0x80 | (int)((cp >> 6) & 0x3F), f); fputc(0x80 | (int)(cp & 0x3F), f); }
        else { fputc(0xF0 | (int)(cp >> 18), f); fputc(0x80 | (int)((cp >> 12) & 0x3F), f); fputc(0x80 | (int)((cp >> 6) & 0x3F), f); fputc(0x80 | (int)(cp & 0x3F), f); }
    }
}

/// Console.Error.Write(string): stdout is flushed first so interleaving matches a terminal.
void csrt_console_error_write_string(csrt_String* s) {
    fflush(stdout);
    write_utf16_to(stderr, s);
}

void csrt_console_flush(void) { fflush(stdout); }
