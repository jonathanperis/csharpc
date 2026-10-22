// csrt console input, stderr output and environment helpers.
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static csrt_String* utf8_to_string(const char* p, size_t n) {
    // Decode UTF-8 (count, then fill), same rules as csrt.c's string_from_utf8.
    size_t units = 0;
    for (size_t i = 0; i < n;) {
        unsigned char c = (unsigned char)p[i];
        size_t len = c < 0x80 ? 1 : c < 0xE0 ? 2 : c < 0xF0 ? 3 : 4;
        units += len == 4 ? 2 : 1;
        i += len;
    }
    csrt_String* s = csrt_alloc(&csrt_String_MT.header, (int64_t)(sizeof(csrt_String) + 2 * units));
    s->length = (int32_t)units;
    size_t o = 0;
    for (size_t i = 0; i < n;) {
        unsigned char c = (unsigned char)p[i];
        uint32_t cp;
        if (c < 0x80) { cp = c; i += 1; }
        else if (c < 0xE0) { cp = (c & 0x1Fu) << 6 | ((unsigned char)p[i + 1] & 0x3Fu); i += 2; }
        else if (c < 0xF0) { cp = (c & 0x0Fu) << 12 | ((unsigned char)p[i + 1] & 0x3Fu) << 6 | ((unsigned char)p[i + 2] & 0x3Fu); i += 3; }
        else { cp = (c & 0x07u) << 18 | ((unsigned char)p[i + 1] & 0x3Fu) << 12 | ((unsigned char)p[i + 2] & 0x3Fu) << 6 | ((unsigned char)p[i + 3] & 0x3Fu); i += 4; }
        if (cp >= 0x10000) { cp -= 0x10000; s->chars[o++] = (uint16_t)(0xD800 + (cp >> 10)); s->chars[o++] = (uint16_t)(0xDC00 + (cp & 0x3FF)); }
        else s->chars[o++] = (uint16_t)cp;
    }
    return s;
}

/// Console.ReadLine(): null at end of input; strips "\n" / "\r\n".
csrt_String* csrt_console_read_line(void) {
    fflush(stdout);  // interactive prompts written with Console.Write must appear first
    char* line = NULL;
    size_t cap = 0;
    ssize_t n = getline(&line, &cap, stdin);
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

/// Math.Round(double, int digits), MidpointRounding.ToEven. .NET rounds the exact decimal value of
/// the double (2.355 is really 2.35499999... -> 2.35), which is precisely what glibc's %.*f does.
double csrt_math_round_digits(double value, int32_t digits) {
    if (digits < 0 || digits > 15 || !isfinite(value) || fabs(value) >= 1e16) return value;
    char buf[400];
    snprintf(buf, sizeof buf, "%.*f", digits, value);
    return strtod(buf, NULL);
}
