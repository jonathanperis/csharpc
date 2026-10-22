// csrt bigint: System.Numerics.BigInteger.
//
// A BigInteger value is a pointer to an immutable GC-heap object (null means zero, so default(T)
// and zeroed arrays are correct): { mt, int32 sign (-1/0/1), int32 length, uint32 limbs[] }
// little-endian magnitude without leading zero limbs. Schoolbook multiply, Knuth algorithm D.
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { const csrt_MethodTable* mt; int32_t sign; int32_t length; uint32_t limbs[]; } BigInt;

static const csrt_MethodTable mt_format = { NULL, "System.FormatException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_overflow = { NULL, "System.OverflowException", 0, 0, NULL, 0, 0 };
static const csrt_MethodTable mt_range = { NULL, "System.ArgumentOutOfRangeException", 0, 0, NULL, 0, 0 };

static csrt_String* ascii(const char* text) {
    int32_t n = (int32_t)strlen(text);
    csrt_String* s = csrt_string_alloc_zeroed((int64_t)n);
    s->length = n;
    for (int32_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    return s;
}

static csrt_String* box_to_string(void* o);
static bool box_equals(void* a, void* b);
static int32_t box_hash(void* a);
const csrt_BuiltinMethodTable csrt_BigInt_MT = { { &csrt_Object_MT.header, "System.Numerics.BigInteger.Data", 0, 0, NULL, 0, 0 }, { NULL, NULL, NULL } };
const csrt_BuiltinMethodTable csrt_Boxed_BigInteger_MT = {
    { &csrt_Object_MT.header, "System.Numerics.BigInteger", 16, 0, NULL, 0, 0 },
    { (void*)box_to_string, (void*)box_equals, (void*)box_hash },
};

// ---- construction ---------------------------------------------------------------------------
static BigInt* make(int32_t sign, const uint32_t* limbs, int32_t length) {
    while (length > 0 && limbs[length - 1] == 0) length--;
    if (length == 0) return NULL;
    BigInt* r = csrt_alloc(&csrt_BigInt_MT.header, (int64_t)(sizeof(BigInt) + 4 * (size_t)length));
    r->sign = sign;
    r->length = length;
    memcpy(r->limbs, limbs, 4 * (size_t)length);
    return r;
}

static int32_t sign_of(const BigInt* a) { return a ? a->sign : 0; }
static int32_t len_of(const BigInt* a) { return a ? a->length : 0; }
static const uint32_t* limbs_of(const BigInt* a) { static const uint32_t zero[1] = { 0 }; return a ? a->limbs : zero; }

BigInt* csrt_bigint_from_u64(uint64_t v) {
    uint32_t l[2] = { (uint32_t)v, (uint32_t)(v >> 32) };
    return make(1, l, 2);
}
BigInt* csrt_bigint_from_i64(int64_t v) {
    uint64_t mag = v < 0 ? (uint64_t)0 - (uint64_t)v : (uint64_t)v;
    uint32_t l[2] = { (uint32_t)mag, (uint32_t)(mag >> 32) };
    return make(v < 0 ? -1 : 1, l, 2);
}
BigInt* csrt_bigint_from_f64(double v) {
    if (isnan(v) || isinf(v)) csrt_throw(&mt_overflow, ascii("Value was either too large or too small for a BigInteger."));
    v = trunc(v);
    int sign = v < 0 ? -1 : 1;
    v = fabs(v);
    if (v < 1) return NULL;
    int exp;
    double m = frexp(v, &exp);  // v = m * 2^exp, 0.5 <= m < 1
    uint64_t mant = (uint64_t)ldexp(m, 53);
    int shift = exp - 53;
    int32_t n = (exp + 31) / 32 + 2;
    uint32_t* l = calloc((size_t)n, 4);
    if (shift >= 0) {
        int ws = shift / 32, bs = shift % 32;
        unsigned __int128 t = (unsigned __int128)mant << bs;
        for (int i = 0; i < 4 && ws + i < n; i++) l[ws + i] = (uint32_t)(t >> (32 * i));
    } else {
        uint64_t t = mant >> (-shift);
        l[0] = (uint32_t)t; l[1] = (uint32_t)(t >> 32);
    }
    BigInt* r = make(sign, l, n);
    free(l);
    return r;
}

// ---- magnitude helpers ----------------------------------------------------------------------
static int cmp_mag(const uint32_t* a, int32_t na, const uint32_t* b, int32_t nb) {
    if (na != nb) return na < nb ? -1 : 1;
    for (int32_t i = na - 1; i >= 0; i--) if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
    return 0;
}

static int32_t add_mag(uint32_t* r, const uint32_t* a, int32_t na, const uint32_t* b, int32_t nb) {
    if (na < nb) { const uint32_t* t = a; a = b; b = t; int32_t tn = na; na = nb; nb = tn; }
    uint64_t carry = 0;
    for (int32_t i = 0; i < na; i++) {
        uint64_t s = (uint64_t)a[i] + (i < nb ? b[i] : 0) + carry;
        r[i] = (uint32_t)s;
        carry = s >> 32;
    }
    r[na] = (uint32_t)carry;
    return na + 1;
}

static void sub_mag(uint32_t* r, const uint32_t* a, int32_t na, const uint32_t* b, int32_t nb) {  // a >= b
    int64_t borrow = 0;
    for (int32_t i = 0; i < na; i++) {
        int64_t d = (int64_t)a[i] - (i < nb ? b[i] : 0) - borrow;
        borrow = d < 0;
        r[i] = (uint32_t)(d + (borrow ? ((int64_t)1 << 32) : 0));
    }
}

static BigInt* add_signed(const BigInt* a, const BigInt* b, bool negate_b) {
    int32_t sa = sign_of(a), sb = sign_of(b) * (negate_b ? -1 : 1);
    if (sb == 0) return sa == 0 ? NULL : make(sa, limbs_of(a), len_of(a));
    if (sa == 0) return make(sb, limbs_of(b), len_of(b));
    int32_t na = len_of(a), nb = len_of(b);
    int32_t n = (na > nb ? na : nb) + 1;
    uint32_t* r = calloc((size_t)n, 4);
    BigInt* result;
    if (sa == sb) {
        add_mag(r, limbs_of(a), na, limbs_of(b), nb);
        result = make(sa, r, n);
    } else {
        int c = cmp_mag(limbs_of(a), na, limbs_of(b), nb);
        if (c == 0) { free(r); return NULL; }
        if (c > 0) { sub_mag(r, limbs_of(a), na, limbs_of(b), nb); result = make(sa, r, na); }
        else { sub_mag(r, limbs_of(b), nb, limbs_of(a), na); result = make(sb, r, nb); }
    }
    free(r);
    return result;
}

BigInt* csrt_bigint_add(BigInt* a, BigInt* b) { return add_signed(a, b, false); }
BigInt* csrt_bigint_sub(BigInt* a, BigInt* b) { return add_signed(a, b, true); }
BigInt* csrt_bigint_negate(BigInt* a) { return a ? make(-a->sign, a->limbs, a->length) : NULL; }
BigInt* csrt_bigint_abs(BigInt* a) { return a ? make(1, a->limbs, a->length) : NULL; }

BigInt* csrt_bigint_mul(BigInt* a, BigInt* b) {
    if (!a || !b) return NULL;
    int32_t n = a->length + b->length;
    uint32_t* r = calloc((size_t)n, 4);
    for (int32_t i = 0; i < a->length; i++) {
        uint64_t carry = 0;
        for (int32_t j = 0; j < b->length; j++) {
            uint64_t t = (uint64_t)a->limbs[i] * b->limbs[j] + r[i + j] + carry;
            r[i + j] = (uint32_t)t;
            carry = t >> 32;
        }
        r[i + b->length] = (uint32_t)carry;
    }
    BigInt* result = make(a->sign * b->sign, r, n);
    free(r);
    return result;
}

/// Knuth algorithm D on magnitudes: q = u / v, rem = u % v (v nonzero). q/rem may be NULL.
static void divmod_mag(const uint32_t* u, int32_t m, const uint32_t* v, int32_t n, uint32_t* q, uint32_t* rem) {
    if (n == 1) {
        uint64_t r = 0;
        for (int32_t i = m - 1; i >= 0; i--) {
            uint64_t cur = (r << 32) | u[i];
            if (q) q[i] = (uint32_t)(cur / v[0]);
            r = cur % v[0];
        }
        if (rem) rem[0] = (uint32_t)r;
        return;
    }
    int s = __builtin_clz(v[n - 1]);
    uint32_t* vn = malloc(4 * (size_t)n);
    uint32_t* un = malloc(4 * (size_t)(m + 1));
    for (int32_t i = n - 1; i > 0; i--) vn[i] = (v[i] << s) | (s ? (uint32_t)((uint64_t)v[i - 1] >> (32 - s)) : 0);
    vn[0] = v[0] << s;
    un[m] = s ? (uint32_t)((uint64_t)u[m - 1] >> (32 - s)) : 0;
    for (int32_t i = m - 1; i > 0; i--) un[i] = (u[i] << s) | (s ? (uint32_t)((uint64_t)u[i - 1] >> (32 - s)) : 0);
    un[0] = u[0] << s;
    for (int32_t j = m - n; j >= 0; j--) {
        uint64_t num = ((uint64_t)un[j + n] << 32) | un[j + n - 1];
        uint64_t qhat = num / vn[n - 1], rhat = num % vn[n - 1];
        while (qhat >= ((uint64_t)1 << 32) || qhat * vn[n - 2] > ((rhat << 32) | un[j + n - 2])) {
            qhat--;
            rhat += vn[n - 1];
            if (rhat >= ((uint64_t)1 << 32)) break;
        }
        int64_t borrow = 0;
        uint64_t carry = 0;
        for (int32_t i = 0; i < n; i++) {
            uint64_t p = qhat * vn[i] + carry;
            carry = p >> 32;
            int64_t t = (int64_t)un[i + j] - (int64_t)(uint32_t)p - borrow;
            un[i + j] = (uint32_t)t;
            borrow = t < 0;
        }
        int64_t t = (int64_t)un[j + n] - (int64_t)carry - borrow;
        un[j + n] = (uint32_t)t;
        if (t < 0) {
            qhat--;
            uint64_t c = 0;
            for (int32_t i = 0; i < n; i++) {
                uint64_t sum = (uint64_t)un[i + j] + vn[i] + c;
                un[i + j] = (uint32_t)sum;
                c = sum >> 32;
            }
            un[j + n] += (uint32_t)c;
        }
        if (q) q[j] = (uint32_t)qhat;
    }
    if (rem) {
        for (int32_t i = 0; i < n - 1; i++) rem[i] = (un[i] >> s) | (s ? (uint32_t)((uint64_t)un[i + 1] << (32 - s)) : 0);
        rem[n - 1] = un[n - 1] >> s;
    }
    free(vn);
    free(un);
}

static void divrem(BigInt* a, BigInt* b, BigInt** quotient, BigInt** remainder) {
    if (!b) csrt_throw_divide_by_zero();
    if (!a || cmp_mag(a->limbs, a->length, b->limbs, b->length) < 0) {
        if (quotient) *quotient = NULL;
        if (remainder) *remainder = a;
        return;
    }
    uint32_t* q = calloc((size_t)a->length, 4);
    uint32_t* r = calloc((size_t)b->length, 4);
    divmod_mag(a->limbs, a->length, b->limbs, b->length, q, r);
    if (quotient) *quotient = make(a->sign * b->sign, q, a->length);
    if (remainder) *remainder = make(a->sign, r, b->length);  // remainder takes the dividend's sign
    free(q);
    free(r);
}

BigInt* csrt_bigint_div(BigInt* a, BigInt* b) { BigInt* q; divrem(a, b, &q, NULL); return q; }
BigInt* csrt_bigint_rem(BigInt* a, BigInt* b) { BigInt* r; divrem(a, b, NULL, &r); return r; }
BigInt* csrt_bigint_divrem(BigInt* a, BigInt* b, BigInt** remainder) { BigInt* q; divrem(a, b, &q, remainder); return q; }

int32_t csrt_bigint_compare(BigInt* a, BigInt* b) {
    int32_t sa = sign_of(a), sb = sign_of(b);
    if (sa != sb) return sa < sb ? -1 : 1;
    if (sa == 0) return 0;
    int c = cmp_mag(a->limbs, a->length, b->limbs, b->length);
    return sa > 0 ? c : -c;
}

BigInt* csrt_bigint_pow(BigInt* base, int32_t exponent) {
    if (exponent < 0) csrt_throw(&mt_range, ascii("The number must be greater than or equal to zero. (Parameter 'exponent')"));
    BigInt* result = csrt_bigint_from_i64(1);
    BigInt* b = base;
    while (exponent) {
        if (exponent & 1) result = csrt_bigint_mul(result, b);
        exponent >>= 1;
        if (exponent) b = csrt_bigint_mul(b, b);
    }
    return result;
}

BigInt* csrt_bigint_mod_pow(BigInt* value, BigInt* exponent, BigInt* modulus) {
    if (sign_of(exponent) < 0) csrt_throw(&mt_range, ascii("The number must be greater than or equal to zero. (Parameter 'exponent')"));
    BigInt* result = csrt_bigint_rem(csrt_bigint_from_i64(1), modulus);
    BigInt* b = csrt_bigint_rem(value, modulus);
    int32_t bits = len_of(exponent) * 32;
    for (int32_t i = 0; i < bits; i++) {
        if ((limbs_of(exponent)[i / 32] >> (i % 32)) & 1) result = csrt_bigint_rem(csrt_bigint_mul(result, b), modulus);
        b = csrt_bigint_rem(csrt_bigint_mul(b, b), modulus);
    }
    return result;
}

BigInt* csrt_bigint_gcd(BigInt* a, BigInt* b) {
    a = csrt_bigint_abs(a);
    b = csrt_bigint_abs(b);
    while (b) { BigInt* t = csrt_bigint_rem(a, b); a = b; b = t; }
    return a;
}

/// Explicit conversion to an integer type: kind 7 long, 5 int, 8 ulong, 6 uint (overflow-checked).
int64_t csrt_bigint_to_integer(BigInt* a, int32_t kind) {
    if (!a) return 0;
    uint64_t mag = a->limbs[0] | (a->length > 1 ? (uint64_t)a->limbs[1] << 32 : 0);
    bool ok = a->length <= 2;
    const char* name = kind == 5 ? "an Int32" : kind == 6 ? "a UInt32" : kind == 8 ? "a UInt64" : "an Int64";
    if (ok) switch (kind) {
        case 5: ok = a->sign > 0 ? mag <= INT32_MAX : mag <= (uint64_t)INT32_MAX + 1; break;
        case 6: ok = a->sign > 0 && mag <= UINT32_MAX; break;
        case 8: ok = a->sign > 0; break;
        default: ok = a->sign > 0 ? mag <= INT64_MAX : mag <= (uint64_t)INT64_MAX + 1; break;
    }
    if (!ok) {
        char msg[96];
        snprintf(msg, sizeof msg, "Value was either too large or too small for %s.", name);
        csrt_throw(&mt_overflow, ascii(msg));
    }
    return a->sign < 0 ? (int64_t)((uint64_t)0 - mag) : (int64_t)mag;
}

double csrt_bigint_to_f64(BigInt* a) {
    if (!a) return 0;
    double r = 0;
    for (int32_t i = a->length - 1; i >= 0; i--) r = r * 4294967296.0 + a->limbs[i];
    return a->sign < 0 ? -r : r;
}

bool csrt_bigint_is_even(BigInt* a) { return !a || (a->limbs[0] & 1) == 0; }
int32_t csrt_bigint_sign(BigInt* a) { return sign_of(a); }

// ---- text -----------------------------------------------------------------------------------
static char* to_decimal(BigInt* a) {
    int32_t n = len_of(a);
    uint32_t* t = malloc(4 * (size_t)(n ? n : 1));
    memcpy(t, limbs_of(a), 4 * (size_t)(n ? n : 1));
    size_t cap = (size_t)n * 10 + 16, len = 0;
    char* out = malloc(cap);
    // repeatedly divide by 10^9
    while (n > 0) {
        uint64_t r = 0;
        for (int32_t i = n - 1; i >= 0; i--) {
            uint64_t cur = (r << 32) | t[i];
            t[i] = (uint32_t)(cur / 1000000000u);
            r = cur % 1000000000u;
        }
        while (n > 0 && t[n - 1] == 0) n--;
        for (int k = 0; k < 9; k++) { out[len++] = (char)('0' + r % 10); r /= 10; if (n == 0 && r == 0) break; }
    }
    if (len == 0) out[len++] = '0';
    if (a && a->sign < 0) out[len++] = '-';
    for (size_t i = 0; i < len / 2; i++) { char c = out[i]; out[i] = out[len - 1 - i]; out[len - 1 - i] = c; }
    out[len] = 0;
    free(t);
    return out;
}

csrt_String* csrt_bigint_to_string(BigInt* a) {
    char* d = to_decimal(a);
    csrt_String* s = ascii(d);
    free(d);
    return s;
}

/// ToString(format): "D[n]", "N[n]" (n decimals of zeros), "G", "R", "X" (two's complement, minimal).
csrt_String* csrt_bigint_format(BigInt* a, csrt_String* fmt) {
    char f[16] = { 0 };
    for (int32_t i = 0; fmt && i < fmt->length && i < 15; i++) f[i] = (char)fmt->chars[i];
    char* d = to_decimal(a);
    if (!f[0] || f[0] == 'G' || f[0] == 'g' || f[0] == 'R' || f[0] == 'r') { csrt_String* s = ascii(d); free(d); return s; }
    bool neg = d[0] == '-';
    const char* digits = neg ? d + 1 : d;
    size_t nd = strlen(digits);
    int prec = f[1] ? atoi(f + 1) : -1;
    char* out = malloc(nd * 2 + 64);
    size_t o = 0;
    if (f[0] == 'D' || f[0] == 'd') {
        if (neg) out[o++] = '-';
        for (int i = (int)nd; i < prec; i++) out[o++] = '0';
        memcpy(out + o, digits, nd); o += nd;
    } else if (f[0] == 'N' || f[0] == 'n') {
        if (neg) out[o++] = '-';
        for (size_t i = 0; i < nd; i++) {
            out[o++] = digits[i];
            size_t left = nd - 1 - i;
            if (left > 0 && left % 3 == 0) out[o++] = ',';
        }
        int dec = prec < 0 ? 2 : prec;
        if (dec > 0) { out[o++] = '.'; for (int i = 0; i < dec; i++) out[o++] = '0'; }
    } else if ((f[0] == 'X' || f[0] == 'x') && (!a || a->sign > 0)) {
        int32_t n = len_of(a);
        char hex[16];
        bool started = false;
        for (int32_t i = n - 1; i >= 0; i--) {
            snprintf(hex, sizeof hex, f[0] == 'X' ? "%08X" : "%08x", a->limbs[i]);
            for (int k = 0; k < 8; k++) { if (!started && hex[k] == '0' && !(i == 0 && k == 7)) continue; if (!started && hex[k] > '7') out[o++] = '0'; started = true; out[o++] = hex[k]; }
        }
        if (!started) out[o++] = '0';
    } else {
        free(out); free(d);
        csrt_throw(&mt_format, ascii("Format specifier was invalid."));
    }
    out[o] = 0;
    csrt_String* s = ascii(out);
    free(out);
    free(d);
    return s;
}

static bool parse(csrt_String* s, BigInt** out) {
    if (!s) return false;
    int32_t i = 0, n = s->length;
    while (i < n && (s->chars[i] == ' ' || s->chars[i] == '\t')) i++;
    int sign = 1;
    if (i < n && (s->chars[i] == '-' || s->chars[i] == '+')) { if (s->chars[i] == '-') sign = -1; i++; }
    int32_t end = n;
    while (end > i && (s->chars[end - 1] == ' ' || s->chars[end - 1] == '\t')) end--;
    if (i >= end) return false;
    int32_t cap = (end - i) / 9 + 2, len = 0;
    uint32_t* l = calloc((size_t)cap, 4);
    for (; i < end; i++) {
        uint16_t c = s->chars[i];
        if (c < '0' || c > '9') { free(l); return false; }
        uint64_t carry = (uint64_t)(c - '0');
        for (int32_t k = 0; k < len; k++) {
            uint64_t t = (uint64_t)l[k] * 10 + carry;
            l[k] = (uint32_t)t;
            carry = t >> 32;
        }
        if (carry) l[len++] = (uint32_t)carry;
    }
    *out = make(sign, l, len);
    free(l);
    return true;
}

BigInt* csrt_bigint_parse(csrt_String* s) {
    BigInt* r;
    if (!parse(s, &r)) csrt_throw(&mt_format, ascii("The value could not be parsed."));
    return r;
}

bool csrt_bigint_try_parse(csrt_String* s, BigInt** result) {
    BigInt* r = NULL;
    bool ok = parse(s, &r);
    *result = ok ? r : NULL;
    return ok;
}

int32_t csrt_bigint_get_hash_code(BigInt* a) {
    uint32_t h = (uint32_t)sign_of(a);
    for (int32_t i = 0; i < len_of(a); i++) h = h * 31 + a->limbs[i];
    return (int32_t)h;
}

static csrt_String* box_to_string(void* o) { return csrt_bigint_to_string(*(BigInt**)((char*)o + 8)); }
static bool box_equals(void* a, void* b) {
    return b && ((csrt_Object*)b)->mt == ((csrt_Object*)a)->mt &&
           csrt_bigint_compare(*(BigInt**)((char*)a + 8), *(BigInt**)((char*)b + 8)) == 0;
}
static int32_t box_hash(void* a) { return csrt_bigint_get_hash_code(*(BigInt**)((char*)a + 8)); }
