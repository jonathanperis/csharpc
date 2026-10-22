// csrt format: .NET numeric format strings (invariant culture) and composite formatting.
//
//   Standard: C, D, E, F, G, N, P, R, X (+ precision), e.g. "F2", "X8", "N0", "E3".
//   Custom:   0 # . , % and literal text, e.g. "0.00", "#,##0.0", "000".
//   Composite: "{index[,alignment][:format]}" with "{{" / "}}" escapes.
//
// Rounding follows .NET Core 3.0+: digits come from the exact decimal expansion of the double
// and are rounded to nearest, ties to even (0.125.ToString("F2") == "0.12").
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static const csrt_MethodTable mt_format = { NULL, "System.FormatException", 0, 0 };

// ---------------------------------------------------------------------------------------------
// tiny growable UTF-8/ASCII buffer
// ---------------------------------------------------------------------------------------------
typedef struct { char* p; size_t n, cap; } Buf;

static void put(Buf* b, char c) {
    if (b->n + 1 >= b->cap) { b->cap = b->cap ? b->cap * 2 : 64; b->p = realloc(b->p, b->cap); }
    b->p[b->n++] = c;
}
static void puts_(Buf* b, const char* s) { while (*s) put(b, *s++); }

static csrt_String* buf_to_string(Buf* b) {
    csrt_String* s = csrt_string_alloc_zeroed((int64_t)b->n);
    s->length = (int32_t)b->n;
    for (size_t i = 0; i < b->n; i++) s->chars[i] = (uint8_t)b->p[i];
    free(b->p);
    return s;
}

static csrt_String* ascii(const char* text) { Buf b = {0}; puts_(&b, text); return buf_to_string(&b); }

_Noreturn static void format_error(void) {
    csrt_throw(&mt_format, ascii("Format specifier was invalid."));
}

// Format strings are UTF-16; specifiers are ASCII. Copy into a small C string.
static void spec_to_c(csrt_String* fmt, char* out, size_t cap) {
    size_t n = 0;
    if (fmt) for (int32_t i = 0; i < fmt->length && n + 1 < cap; i++) out[n++] = fmt->chars[i] < 128 ? (char)fmt->chars[i] : '?';
    out[n] = 0;
}

// ---------------------------------------------------------------------------------------------
// exact decimal digits
// ---------------------------------------------------------------------------------------------
// Digits of |v| as a string with decimal exponent: v = 0.d1d2d3... * 10^exp10 (d1 != 0).
typedef struct { char d[800]; int n; int exp10; bool neg; bool half_up; } Digits;  // half_up: decimal's rounding

static void exact_digits(double v, Digits* out) {
    out->neg = signbit(v);
    out->half_up = false;
    v = fabs(v);
    if (v == 0) { out->n = 0; out->exp10 = 1; return; }
    char tmp[900];
    snprintf(tmp, sizeof tmp, "%.760e", v);  // glibc prints the exact binary value
    char* e = strchr(tmp, 'e');
    int exp = atoi(e + 1);
    int n = 0;
    for (char* p = tmp; p < e; p++) if (*p >= '0' && *p <= '9') out->d[n++] = *p;
    while (n > 1 && out->d[n - 1] == '0') n--;
    out->n = n;
    out->exp10 = exp + 1;
}

// Round to keep `keep` digits total (may be <= 0), half away from zero. Updates exp on carry.
static void round_digits(Digits* g, int keep) {
    if (keep >= g->n) return;
    if (keep < 0) { g->n = 0; return; }
    // IEEE-correct rounding of the exact expansion, ties to even (what .NET Core 3.0+ does).
    bool up = g->half_up ? g->d[keep] >= '5' : g->d[keep] > '5' || (g->d[keep] == '5' &&
              (keep + 1 < g->n || (keep > 0 && (g->d[keep - 1] - '0') % 2 == 1)));
    g->n = keep;
    if (!up) { while (g->n > 0 && g->d[g->n - 1] == '0') g->n--; return; }
    int i = keep - 1;
    while (i >= 0 && g->d[i] == '9') { g->d[i] = '0'; i--; }
    if (i < 0) {  // 999 -> 1000
        memmove(g->d + 1, g->d, (size_t)keep);
        g->d[0] = '1';
        g->n = keep + 1;
        g->exp10++;
    } else {
        g->d[i]++;
    }
    while (g->n > 0 && g->d[g->n - 1] == '0') g->n--;
}

static char digit_at(const Digits* g, int i) { return i >= 0 && i < g->n ? g->d[i] : '0'; }

// Fixed notation with `decimals` digits after the point, optional thousands grouping.
static void emit_fixed(Buf* b, Digits g, int decimals, bool group) {
    round_digits(&g, g.exp10 + decimals);
    if (g.neg) put(b, '-');  // .NET keeps the sign: (-0.004).ToString("F2") == "-0.00"
    int int_digits = g.exp10 > 0 ? g.exp10 : 1;
    for (int i = 0; i < int_digits; i++) {
        put(b, g.exp10 > 0 ? digit_at(&g, i) : '0');
        int left = int_digits - 1 - i;
        if (group && left > 0 && left % 3 == 0) put(b, ',');
    }
    if (decimals > 0) {
        put(b, '.');
        for (int i = 0; i < decimals; i++) put(b, digit_at(&g, g.exp10 + i));
    }
}

static void emit_exponent(Buf* b, Digits g, int decimals, char e, int min_exp_digits) {
    if (g.n == 0) { g.exp10 = 1; }
    round_digits(&g, decimals + 1);
    if (g.neg && g.n) put(b, '-');
    put(b, g.n ? g.d[0] : '0');
    if (decimals > 0) { put(b, '.'); for (int i = 1; i <= decimals; i++) put(b, digit_at(&g, i)); }
    int exp = g.n ? g.exp10 - 1 : 0;
    char tmp[16];
    snprintf(tmp, sizeof tmp, "%c%c%0*d", e, exp < 0 ? '-' : '+', min_exp_digits, exp < 0 ? -exp : exp);
    puts_(b, tmp);
}

// ---------------------------------------------------------------------------------------------
// custom format strings ("0.00", "#,##0", "000", "0.##%")
// ---------------------------------------------------------------------------------------------
static bool is_custom(const char* f) {
    if (!f[0]) return false;
    bool standard = ((f[0] >= 'A' && f[0] <= 'Z') || (f[0] >= 'a' && f[0] <= 'z'));
    for (const char* p = f + 1; standard && *p; p++) if (*p < '0' || *p > '9') standard = false;
    return !standard;
}

static void emit_custom_digits(Buf* b, Digits g, const char* f);

static void emit_custom(Buf* b, double v, const char* f) {
    bool percent = strchr(f, '%') != NULL;
    Digits g; exact_digits(percent ? v * 100 : v, &g);
    emit_custom_digits(b, g, f);
}

static void emit_custom_section(Buf* b, Digits g, const char* f);

/// Custom format over exact digits (the caller applies any % scaling). Supports up to three
/// ';'-separated sections: positive;negative;zero (the negative section prints no '-').
static void emit_custom_digits(Buf* b, Digits g, const char* f) {
    char sections[3][64] = { "", "", "" };
    int count = 0, len = 0;
    char quote = 0;
    for (const char* p = f; *p && count < 3; p++) {
        if (quote) { if (*p == quote) quote = 0; }
        else if (*p == '\'' || *p == '"') quote = *p;
        else if (*p == ';') { sections[count][len] = 0; count++; len = 0; continue; }
        if (len < 63) sections[count][len++] = *p;
    }
    if (count < 3) { sections[count][len] = 0; count++; }
    if (count == 1) { emit_custom_section(b, g, f); return; }
    const char* chosen = sections[0];
    if (g.neg && g.n > 0 && sections[1][0]) { chosen = sections[1]; g.neg = false; }
    else if (g.n == 0 && count >= 3 && sections[2][0]) chosen = sections[2];
    emit_custom_section(b, g, chosen);
}

static void emit_custom_section(Buf* b, Digits g, const char* f) {
    // Analyse the pattern: integer placeholders, decimal placeholders, grouping, percent.
    int int_zeros = 0, dec_zeros = 0, dec_hashes = 0;
    bool group = false, percent = false, seen_point = false, seen_digit = false;
    const char* first = NULL; const char* last = NULL;
    for (const char* p = f; *p; p++) {
        char c = *p;
        if (c == '0' || c == '#') {
            if (!first) first = p;
            last = p;
            seen_digit = true;
            if (seen_point) { if (c == '0') { dec_zeros += 1 + dec_hashes; dec_hashes = 0; } else dec_hashes++; }
            else if (c == '0') int_zeros++;
            else if (int_zeros) int_zeros++;  // "0#" counts as required
        } else if (c == '.' && !seen_point) { seen_point = true; if (!first) first = p; last = p; }
        else if (c == ',' && !seen_point && seen_digit) group = true;
        else if (c == '%') percent = true;
    }
    (void)percent;
    int decimals = dec_zeros + dec_hashes;
    round_digits(&g, g.exp10 + decimals);
    // literal prefix
    for (const char* p = f; first && p < first; p++) if (*p != ',' && *p != '#' && *p != '0') put(b, *p);
    if (g.neg && g.n) put(b, '-');
    int int_digits = g.n && g.exp10 > 0 ? g.exp10 : 0;
    if (int_digits < int_zeros) int_digits = int_zeros;
    for (int i = 0; i < int_digits; i++) {
        int src = g.exp10 - int_digits + i;
        put(b, digit_at(&g, src));
        int left = int_digits - 1 - i;
        if (group && left > 0 && left % 3 == 0) put(b, ',');
    }
    int shown = decimals;
    while (shown > dec_zeros && digit_at(&g, g.exp10 + shown - 1) == '0') shown--;
    if (shown > 0) { put(b, '.'); for (int i = 0; i < shown; i++) put(b, digit_at(&g, g.exp10 + i)); }
    // literal suffix
    for (const char* p = last ? last + 1 : f; *p; p++) if (*p != ',' && *p != '#' && *p != '0') put(b, *p == '%' ? '%' : *p);
}

// ---------------------------------------------------------------------------------------------
// standard formats
// ---------------------------------------------------------------------------------------------
static int precision_of(const char* f, int dflt) { return f[1] ? atoi(f + 1) : dflt; }

static void format_integer(Buf* b, int64_t sv, uint64_t uv, bool is_signed, int bits, const char* f) {
    bool neg = is_signed && sv < 0;
    uint64_t mag = is_signed ? (neg ? (uint64_t)0 - (uint64_t)sv : (uint64_t)sv) : uv;
    char tmp[72];
    if (!f[0] || ((f[0] == 'G' || f[0] == 'g') && !f[1]) || f[0] == 'R' || f[0] == 'r') {
        snprintf(tmp, sizeof tmp, "%s%llu", neg ? "-" : "", (unsigned long long)mag);
        puts_(b, tmp);
        return;
    }
    if (is_custom(f)) { emit_custom(b, is_signed ? (double)sv : (double)uv, f); return; }
    switch (f[0]) {
        case 'D': case 'd': {
            int p = precision_of(f, 0);
            snprintf(tmp, sizeof tmp, "%s%0*llu", neg ? "-" : "", p, (unsigned long long)mag);
            puts_(b, tmp);
            return;
        }
        case 'X': case 'x': {
            uint64_t raw = is_signed ? (uint64_t)sv : uv;
            if (bits < 64) raw &= ((uint64_t)1 << bits) - 1;
            snprintf(tmp, sizeof tmp, f[0] == 'X' ? "%0*llX" : "%0*llx", precision_of(f, 0), (unsigned long long)raw);
            puts_(b, tmp);
            return;
        }
        default: {
            double v = is_signed ? (double)sv : (double)uv;
            // Exact digits of the integer (a double would lose precision above 2^53).
            Digits g = { .neg = neg };  // every field defined (half_up = false: ties-to-even)
            char tmp2[32];
            int nd = snprintf(tmp2, sizeof tmp2, "%llu", (unsigned long long)mag);
            g.n = mag == 0 ? 0 : nd;
            g.exp10 = mag == 0 ? 1 : nd;
            memcpy(g.d, tmp2, (size_t)nd);
            while (g.n > 0 && g.d[g.n - 1] == '0') g.n--;
            switch (f[0]) {
                case 'N': case 'n': emit_fixed(b, g, precision_of(f, 2), true); return;
                case 'F': case 'f': emit_fixed(b, g, precision_of(f, 2), false); return;
                case 'E': case 'e': emit_exponent(b, g, precision_of(f, 6), f[0], 3); return;
                case 'P': case 'p': exact_digits(v * 100, &g); emit_fixed(b, g, precision_of(f, 2), true); puts_(b, " %"); return;
                case 'C': case 'c': puts_(b, "\xA4"); emit_fixed(b, g, precision_of(f, 2), true); return;
                case 'G': case 'g': {
                    int p = precision_of(f, 0);
                    snprintf(tmp, sizeof tmp, "%s%llu", neg ? "-" : "", (unsigned long long)mag);
                    if (p == 0 || (int)strlen(tmp) - neg <= p) { puts_(b, tmp); return; }
                    emit_exponent(b, g, p - 1, f[0] == 'G' ? 'E' : 'e', 2);
                    return;
                }
            }
            format_error();
        }
    }
}


static void format_double(Buf* b, double v, const char* f, bool single) {
    if (isnan(v)) { puts_(b, "NaN"); return; }
    if (isinf(v)) { puts_(b, v > 0 ? "Infinity" : "-Infinity"); return; }
    if (!f[0] || ((f[0] == 'G' || f[0] == 'g' || f[0] == 'R' || f[0] == 'r') && !f[1])) {
        csrt_String* s = single ? csrt_string_from_f32((float)v) : csrt_string_from_f64(v);
        for (int32_t i = 0; i < s->length; i++) put(b, (char)s->chars[i]);
        return;
    }
    if (is_custom(f)) { emit_custom(b, v, f); return; }
    Digits g; exact_digits(v, &g);
    switch (f[0]) {
        case 'F': case 'f': emit_fixed(b, g, precision_of(f, 2), false); return;
        case 'N': case 'n': emit_fixed(b, g, precision_of(f, 2), true); return;
        case 'E': case 'e': emit_exponent(b, g, precision_of(f, 6), f[0], 3); return;
        case 'P': case 'p': exact_digits(v * 100, &g); emit_fixed(b, g, precision_of(f, 2), true); puts_(b, " %"); return;
        case 'C': case 'c': puts_(b, "\xA4"); emit_fixed(b, g, precision_of(f, 2), true); return;
        case 'G': case 'g': {
            int p = precision_of(f, 15);
            if (g.n == 0) { put(b, '0'); return; }
            round_digits(&g, p);
            int exp = g.exp10 - 1;
            if (exp >= -4 && exp < p) {
                // fixed, trailing zeros trimmed
                int decimals = g.n - g.exp10;
                emit_fixed(b, g, decimals > 0 ? decimals : 0, false);
            } else {
                emit_exponent(b, g, g.n - 1, f[0] == 'G' ? 'E' : 'e', 2);
            }
            return;
        }
    }
    format_error();
}

// ---------------------------------------------------------------------------------------------
// public entry points
// ---------------------------------------------------------------------------------------------
csrt_String* csrt_format_i64(int64_t v, csrt_String* fmt, int32_t bits) {
    char f[64]; spec_to_c(fmt, f, sizeof f);
    Buf b = {0}; format_integer(&b, v, 0, true, bits, f); return buf_to_string(&b);
}

csrt_String* csrt_format_u64(uint64_t v, csrt_String* fmt, int32_t bits) {
    char f[64]; spec_to_c(fmt, f, sizeof f);
    Buf b = {0}; format_integer(&b, 0, v, false, bits, f); return buf_to_string(&b);
}

csrt_String* csrt_format_f64(double v, csrt_String* fmt, bool single) {
    char f[64]; spec_to_c(fmt, f, sizeof f);
    Buf b = {0}; format_double(&b, v, f, single); return buf_to_string(&b);
}

/// Pads to |width| (positive: right-align, negative: left-align), like {x,width}.
csrt_String* csrt_string_align(csrt_String* s, int32_t width) {
    int32_t len = s ? s->length : 0;
    int32_t w = width < 0 ? -width : width;
    if (w <= len) return s ? s : ascii("");
    csrt_String* r = csrt_string_alloc_zeroed((int64_t)w);
    r->length = w;
    int32_t pad = w - len;
    for (int32_t i = 0; i < w; i++) r->chars[i] = ' ';
    if (len) memcpy(r->chars + (width < 0 ? 0 : pad), s->chars, 2 * (size_t)len);
    return r;
}

extern const csrt_BuiltinMethodTable csrt_Boxed_Decimal_MT;
csrt_String* csrt_format_decimal(const void* dp, csrt_String* fmt);

/// Formats any object with an optional format string (boxed primitives honour it).
csrt_String* csrt_format_object(void* obj, csrt_String* fmt) {
    if (!obj) return ascii("");
    const csrt_MethodTable* mt = ((csrt_Object*)obj)->mt;
    const void* p = (const char*)obj + 8;
    if (fmt && fmt->length) {
        if (mt == &csrt_Boxed_Int32_MT.header) return csrt_format_i64(*(const int32_t*)p, fmt, 32);
        if (mt == &csrt_Boxed_Int64_MT.header) return csrt_format_i64(*(const int64_t*)p, fmt, 64);
        if (mt == &csrt_Boxed_Int16_MT.header) return csrt_format_i64(*(const int16_t*)p, fmt, 16);
        if (mt == &csrt_Boxed_SByte_MT.header) return csrt_format_i64(*(const int8_t*)p, fmt, 8);
        if (mt == &csrt_Boxed_UInt32_MT.header) return csrt_format_u64(*(const uint32_t*)p, fmt, 32);
        if (mt == &csrt_Boxed_UInt64_MT.header) return csrt_format_u64(*(const uint64_t*)p, fmt, 64);
        if (mt == &csrt_Boxed_UInt16_MT.header) return csrt_format_u64(*(const uint16_t*)p, fmt, 16);
        if (mt == &csrt_Boxed_Byte_MT.header) return csrt_format_u64(*(const uint8_t*)p, fmt, 8);
        if (mt == &csrt_Boxed_Double_MT.header) return csrt_format_f64(*(const double*)p, fmt, false);
        if (mt == &csrt_Boxed_Single_MT.header) return csrt_format_f64(*(const float*)p, fmt, true);
        if (mt == &csrt_Boxed_Decimal_MT.header) return csrt_format_decimal(p, fmt);
    }
    return csrt_object_to_string(obj);
}

/// string.Format / Console.WriteLine(format, args): composite formatting.
csrt_String* csrt_string_format(csrt_String* fmt, csrt_Array* args) {
    if (!fmt) csrt_throw_null_reference();
    void** items = args ? (void**)args->data : NULL;
    int64_t count = args ? args->length : 0;
    // Build UTF-16 output directly.
    int32_t cap = fmt->length + 16, n = 0;
    uint16_t* out = malloc(sizeof(uint16_t) * (size_t)cap);
#define EMIT(c) do { if (n >= cap) { cap *= 2; out = realloc(out, sizeof(uint16_t) * (size_t)cap); } out[n++] = (c); } while (0)
    for (int32_t i = 0; i < fmt->length; i++) {
        uint16_t c = fmt->chars[i];
        if (c == '}') {
            if (i + 1 < fmt->length && fmt->chars[i + 1] == '}') { EMIT('}'); i++; continue; }
            free(out); csrt_throw(&mt_format, ascii("Input string was not in a correct format."));
        }
        if (c != '{') { EMIT(c); continue; }
        if (i + 1 < fmt->length && fmt->chars[i + 1] == '{') { EMIT('{'); i++; continue; }
        // {index[,alignment][:format]}
        i++;
        int64_t index = 0; bool any = false;
        while (i < fmt->length && fmt->chars[i] >= '0' && fmt->chars[i] <= '9') { index = index * 10 + (fmt->chars[i++] - '0'); any = true; }
        int32_t align = 0;
        if (i < fmt->length && fmt->chars[i] == ',') {
            i++;
            bool neg = false;
            while (i < fmt->length && fmt->chars[i] == ' ') i++;
            if (i < fmt->length && fmt->chars[i] == '-') { neg = true; i++; }
            while (i < fmt->length && fmt->chars[i] >= '0' && fmt->chars[i] <= '9') align = align * 10 + (fmt->chars[i++] - '0');
            if (neg) align = -align;
        }
        csrt_String* spec = NULL;
        if (i < fmt->length && fmt->chars[i] == ':') {
            int32_t start = ++i;
            while (i < fmt->length && fmt->chars[i] != '}') i++;
            spec = csrt_string_alloc_zeroed((int64_t)(i - start));
            spec->length = i - start;
            memcpy(spec->chars, fmt->chars + start, 2 * (size_t)(i - start));
        }
        if (!any || i >= fmt->length || fmt->chars[i] != '}') {
            free(out); csrt_throw(&mt_format, ascii("Input string was not in a correct format."));
        }
        if (index >= count) {
            free(out);
            csrt_throw(&mt_format, ascii("Index (zero based) must be greater than or equal to zero and less than the size of the argument list."));
        }
        csrt_String* piece = csrt_string_align(csrt_format_object(items[index], spec), align);
        for (int32_t k = 0; k < piece->length; k++) EMIT(piece->chars[k]);
    }
#undef EMIT
    csrt_String* s = csrt_string_alloc_zeroed((int64_t)n);
    s->length = n;
    memcpy(s->chars, out, 2 * (size_t)n);
    free(out);
    return s;
}

// ---------------------------------------------------------------------------------------------
// System.Decimal formatting: exact digits, rounded half away from zero (unlike double).
// ---------------------------------------------------------------------------------------------
typedef struct { int32_t flags; uint32_t hi; uint64_t lo; } csrt_DecimalLayout;
int csrt_decimal_digits(const void* d, char* buf);
csrt_String* csrt_decimal_to_string(const void* d);

csrt_String* csrt_format_decimal(const void* dp, csrt_String* fmt) {
    const csrt_DecimalLayout* d = dp;
    char f[64]; spec_to_c(fmt, f, sizeof f);
    if (!f[0] || ((f[0] == 'G' || f[0] == 'g') && !f[1])) return csrt_decimal_to_string(d);
    Digits g;
    char raw[40];
    int n = csrt_decimal_digits(d, raw);
    int scale = (int)(((uint32_t)d->flags >> 16) & 0xFF);
    bool zero = d->lo == 0 && d->hi == 0;
    g.neg = (((uint32_t)d->flags & 0x80000000u) != 0) && !zero;
    g.half_up = true;
    // normalize: strip leading zeros, compute exponent of 0.d1d2..
    int start = 0;
    while (start < n - 1 && raw[start] == '0') start++;
    g.n = 0;
    if (!zero) for (int i = start; i < n; i++) g.d[g.n++] = raw[i];
    while (g.n > 0 && g.d[g.n - 1] == '0') g.n--;
    g.exp10 = zero ? 1 : (n - start) - scale;
    Buf b = {0};
    if (is_custom(f)) {
        if (strchr(f, '%') && g.n) g.exp10 += 2;
        emit_custom_digits(&b, g, f);
        return buf_to_string(&b);
    }
    switch (f[0]) {
        case 'F': case 'f': emit_fixed(&b, g, precision_of(f, 2), false); break;
        case 'N': case 'n': emit_fixed(&b, g, precision_of(f, 2), true); break;
        case 'E': case 'e': emit_exponent(&b, g, precision_of(f, 6), f[0], 3); break;
        case 'P': case 'p': if (g.n) g.exp10 += 2; emit_fixed(&b, g, precision_of(f, 2), true); puts_(&b, " %"); break;
        case 'C': case 'c': puts_(&b, "\xA4"); emit_fixed(&b, g, precision_of(f, 2), true); break;
        case 'G': case 'g': {
            int p = precision_of(f, 29);
            round_digits(&g, p);
            int exp = g.exp10 - 1;
            if (g.n == 0) { put(&b, '0'); break; }
            if (exp >= -5 && exp < p) { int dec = g.n - g.exp10; emit_fixed(&b, g, dec > 0 ? dec : 0, false); }
            else emit_exponent(&b, g, g.n - 1, f[0] == 'G' ? 'E' : 'e', 2);
            break;
        }
        default: free(b.p); format_error();
    }
    return buf_to_string(&b);
}
