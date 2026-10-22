// csrt decimal: System.Decimal with .NET's exact layout and semantics.
//
// Layout (matches the CLR): { int32 flags; uint32 hi32; uint64 lo64 } where flags holds the scale
// (0..28) in bits 16-23 and the sign in bit 31. The value is (-1)^sign * (hi32:lo64) / 10^scale.
//
// Every operation computes the *exact* result with a small fixed-size bignum and then fits it into
// 96 bits / scale <= 28 by dividing by a power of ten with round-half-to-even — the same contract
// as the CLR's DecCalc (ScaleResult). Scales follow .NET: a+b keeps max(scale), a*b uses the sum
// of scales, a/b uses the smallest scale that represents the quotient exactly (up to 28 digits).
#define _POSIX_C_SOURCE 200809L
#include "csrt.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { int32_t flags; uint32_t hi; uint64_t lo; } csrt_Decimal;

#define DEC_SIGN 0x80000000u
#define DEC_SCALE(d) ((int)(((uint32_t)(d)->flags >> 16) & 0xFF))
#define DEC_NEG(d) (((uint32_t)(d)->flags & DEC_SIGN) != 0)

static const csrt_MethodTable mt_overflow = { NULL, "System.OverflowException", 0, 0 };
static const csrt_MethodTable mt_format = { NULL, "System.FormatException", 0, 0 };
static const csrt_MethodTable mt_argument_null = { NULL, "System.ArgumentNullException", 0, 0 };

static csrt_String* ascii(const char* text) {
    int32_t n = (int32_t)strlen(text);
    csrt_String* s = csrt_string_alloc_zeroed((int64_t)n);
    s->length = n;
    for (int32_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    return s;
}

_Noreturn static void decimal_overflow(void) {
    csrt_throw(&mt_overflow, ascii("Value was either too large or too small for a Decimal."));
}

// ---------------------------------------------------------------------------------------------
// Fixed-size unsigned bignum (little-endian 32-bit limbs). 12 limbs = 384 bits is plenty:
// the largest intermediate is a 96x96-bit product or a 96-bit value scaled by 10^28.
// ---------------------------------------------------------------------------------------------
#define LIMBS 12
typedef struct { uint32_t w[LIMBS]; } Big;

static Big big_from(const csrt_Decimal* d) {
    Big b = {{0}};
    b.w[0] = (uint32_t)d->lo;
    b.w[1] = (uint32_t)(d->lo >> 32);
    b.w[2] = d->hi;
    return b;
}

static Big big_u64(uint64_t v) { Big b = {{0}}; b.w[0] = (uint32_t)v; b.w[1] = (uint32_t)(v >> 32); return b; }

static bool big_is_zero(const Big* a) { for (int i = 0; i < LIMBS; i++) if (a->w[i]) return false; return true; }
static bool big_fits96(const Big* a) { for (int i = 3; i < LIMBS; i++) if (a->w[i]) return false; return true; }

static int big_cmp(const Big* a, const Big* b) {
    for (int i = LIMBS - 1; i >= 0; i--) if (a->w[i] != b->w[i]) return a->w[i] < b->w[i] ? -1 : 1;
    return 0;
}

static void big_mul_small(Big* a, uint32_t m) {
    uint64_t carry = 0;
    for (int i = 0; i < LIMBS; i++) {
        uint64_t t = (uint64_t)a->w[i] * m + carry;
        a->w[i] = (uint32_t)t;
        carry = t >> 32;
    }
}

static void big_mul_pow10(Big* a, int k) {
    while (k >= 9) { big_mul_small(a, 1000000000u); k -= 9; }
    static const uint32_t p[9] = { 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000 };
    if (k > 0) big_mul_small(a, p[k]);
}

static Big big_pow10(int k) { Big b = big_u64(1); big_mul_pow10(&b, k); return b; }

static void big_add(Big* a, const Big* b) {
    uint64_t carry = 0;
    for (int i = 0; i < LIMBS; i++) {
        uint64_t t = (uint64_t)a->w[i] + b->w[i] + carry;
        a->w[i] = (uint32_t)t;
        carry = t >> 32;
    }
}

static void big_sub(Big* a, const Big* b) {  // requires a >= b
    int64_t borrow = 0;
    for (int i = 0; i < LIMBS; i++) {
        int64_t t = (int64_t)a->w[i] - b->w[i] - borrow;
        borrow = t < 0;
        a->w[i] = (uint32_t)(t + (borrow ? ((int64_t)1 << 32) : 0));
    }
}

static Big big_mul(const Big* a, const Big* b) {
    Big r = {{0}};
    for (int i = 0; i < LIMBS; i++) {
        if (!a->w[i]) continue;
        uint64_t carry = 0;
        for (int j = 0; i + j < LIMBS; j++) {
            uint64_t t = (uint64_t)a->w[i] * b->w[j] + r.w[i + j] + carry;
            r.w[i + j] = (uint32_t)t;
            carry = t >> 32;
        }
    }
    return r;
}

static uint32_t big_divmod_small(Big* a, uint32_t d) {
    uint64_t rem = 0;
    for (int i = LIMBS - 1; i >= 0; i--) {
        uint64_t cur = (rem << 32) | a->w[i];
        a->w[i] = (uint32_t)(cur / d);
        rem = cur % d;
    }
    return (uint32_t)rem;
}

static int big_bitlen(const Big* a) {
    for (int i = LIMBS - 1; i >= 0; i--)
        if (a->w[i]) return i * 32 + (32 - __builtin_clz(a->w[i]));
    return 0;
}

static void big_shl1(Big* a) {
    for (int i = LIMBS - 1; i > 0; i--) a->w[i] = (a->w[i] << 1) | (a->w[i - 1] >> 31);
    a->w[0] <<= 1;
}

/// q = a / b, r = a % b (b != 0). Small divisors use limb division; otherwise shift-subtract.
static void big_divmod(const Big* a, const Big* b, Big* q, Big* r) {
    if (big_bitlen(b) <= 32) {
        *q = *a;
        uint32_t rem = big_divmod_small(q, b->w[0]);
        *r = big_u64(rem);
        return;
    }
    Big quo = {{0}}, rem = {{0}};
    for (int bit = big_bitlen(a) - 1; bit >= 0; bit--) {
        big_shl1(&rem);
        rem.w[0] |= (a->w[bit / 32] >> (bit % 32)) & 1;
        if (big_cmp(&rem, b) >= 0) {
            big_sub(&rem, b);
            quo.w[bit / 32] |= 1u << (bit % 32);
        }
    }
    *q = quo;
    *r = rem;
}

static bool big_is_odd(const Big* a) { return a->w[0] & 1; }

// ---------------------------------------------------------------------------------------------
// Packing
// ---------------------------------------------------------------------------------------------
static void pack(csrt_Decimal* r, const Big* m, int scale, bool negative) {
    r->lo = (uint64_t)m->w[0] | ((uint64_t)m->w[1] << 32);
    r->hi = m->w[2];
    bool zero = r->lo == 0 && r->hi == 0;
    r->flags = (int32_t)(((uint32_t)scale << 16) | (negative && !zero ? DEC_SIGN : 0));
}

/// Fits an exact (m, scale) into 96 bits and scale <= 28, rounding half-to-even. Throws on overflow.
static void fit(csrt_Decimal* r, Big m, int scale, bool negative) {
    if (scale <= 28 && big_fits96(&m)) { pack(r, &m, scale, negative); return; }
    int k = scale > 28 ? scale - 28 : 0;
    // Estimate the digits to drop from the bit length (10^k grows ~3.32 bits per digit).
    if (k == 0) k = 1;
    int excess = big_bitlen(&m) - 96;  // each decimal digit is ~3.32 bits: skip hopeless tries
    if (excess > 0 && excess * 30103 / 100000 > k) k = excess * 30103 / 100000;
    for (;; k++) {
        if (k > scale) decimal_overflow();
        Big divisor = big_pow10(k), q, rem;
        big_divmod(&m, &divisor, &q, &rem);
        // round half to even: compare 2*rem with divisor
        Big twice = rem;
        big_shl1(&twice);
        int c = big_cmp(&twice, &divisor);
        if (c > 0 || (c == 0 && big_is_odd(&q))) { Big one = big_u64(1); big_add(&q, &one); }
        if (big_fits96(&q)) { pack(r, &q, scale - k, negative); return; }
    }
}

// ---------------------------------------------------------------------------------------------
// Arithmetic
// ---------------------------------------------------------------------------------------------
static void align(const csrt_Decimal* a, const csrt_Decimal* b, Big* ma, Big* mb, int* scale) {
    int sa = DEC_SCALE(a), sb = DEC_SCALE(b);
    *ma = big_from(a);
    *mb = big_from(b);
    if (sa < sb) big_mul_pow10(ma, sb - sa);
    else big_mul_pow10(mb, sa - sb);
    *scale = sa > sb ? sa : sb;
}

static void add_signed(csrt_Decimal* r, const csrt_Decimal* a, const csrt_Decimal* b, bool negate_b) {
    Big ma, mb;
    int scale;
    align(a, b, &ma, &mb, &scale);
    bool na = DEC_NEG(a), nb = DEC_NEG(b) ^ negate_b;
    if (na == nb) {
        big_add(&ma, &mb);
        fit(r, ma, scale, na);
    } else if (big_cmp(&ma, &mb) >= 0) {
        big_sub(&ma, &mb);
        fit(r, ma, scale, na);
    } else {
        big_sub(&mb, &ma);
        fit(r, mb, scale, nb);
    }
}

void csrt_decimal_add(csrt_Decimal* r, const csrt_Decimal* a, const csrt_Decimal* b) { add_signed(r, a, b, false); }
void csrt_decimal_sub(csrt_Decimal* r, const csrt_Decimal* a, const csrt_Decimal* b) { add_signed(r, a, b, true); }

void csrt_decimal_mul(csrt_Decimal* r, const csrt_Decimal* a, const csrt_Decimal* b) {
    Big ma = big_from(a), mb = big_from(b);
    Big p = big_mul(&ma, &mb);
    fit(r, p, DEC_SCALE(a) + DEC_SCALE(b), DEC_NEG(a) != DEC_NEG(b));
}

static const Big MAX96 = {{ 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0, 0, 0, 0, 0, 0, 0, 0, 0 }};

void csrt_decimal_div(csrt_Decimal* r, const csrt_Decimal* a, const csrt_Decimal* b) {
    Big ma = big_from(a), mb = big_from(b);
    if (big_is_zero(&mb)) csrt_throw_divide_by_zero();
    bool negative = DEC_NEG(a) != DEC_NEG(b);
    int scale = DEC_SCALE(a) - DEC_SCALE(b);
    if (scale < 0) { big_mul_pow10(&ma, -scale); scale = 0; }
    if (big_is_zero(&ma)) { pack(r, &ma, scale, false); return; }

    Big q, rem;
    big_divmod(&ma, &mb, &q, &rem);
    // Extend the quotient digit by digit while it's inexact, the scale allows and it still fits.
    while (!big_is_zero(&rem) && scale < 28) {
        Big q10 = q;
        big_mul_small(&q10, 10);
        Big r10 = rem;
        big_mul_small(&r10, 10);
        Big digit, nrem;
        big_divmod(&r10, &mb, &digit, &nrem);
        big_add(&q10, &digit);
        if (big_cmp(&q10, &MAX96) > 0) break;
        q = q10;
        rem = nrem;
        scale++;
    }
    if (!big_is_zero(&rem)) {
        Big twice = rem;
        big_shl1(&twice);
        int c = big_cmp(&twice, &mb);
        if (c > 0 || (c == 0 && big_is_odd(&q))) { Big one = big_u64(1); big_add(&q, &one); }
    }
    if (big_is_zero(&q)) scale = 0;  // .NET: 1e-28m / 3 == 0 (scale 0)
    fit(r, q, scale, negative);
}

void csrt_decimal_rem(csrt_Decimal* r, const csrt_Decimal* a, const csrt_Decimal* b) {
    Big ma, mb;
    int scale;
    align(a, b, &ma, &mb, &scale);
    if (big_is_zero(&mb)) csrt_throw_divide_by_zero();
    Big q, rem;
    big_divmod(&ma, &mb, &q, &rem);
    fit(r, rem, scale, DEC_NEG(a));
}

int32_t csrt_decimal_compare(const csrt_Decimal* a, const csrt_Decimal* b) {
    Big ma, mb;
    int scale;
    align(a, b, &ma, &mb, &scale);
    bool za = big_is_zero(&ma), zb = big_is_zero(&mb);
    bool na = DEC_NEG(a) && !za, nb = DEC_NEG(b) && !zb;
    if (na != nb) return na ? -1 : 1;
    int c = big_cmp(&ma, &mb);
    return na ? -c : c;
}

void csrt_decimal_negate(csrt_Decimal* r, const csrt_Decimal* a) {
    *r = *a;
    if (a->lo || a->hi) r->flags = (int32_t)((uint32_t)a->flags ^ DEC_SIGN);
}

int32_t csrt_decimal_get_hash_code(const csrt_Decimal* d) {
    // Equal values (1.0m == 1.00m) must hash alike: hash the normalized value.
    Big m = big_from(d);
    int scale = DEC_SCALE(d);
    while (scale > 0) {
        Big t = m;
        if (big_divmod_small(&t, 10) != 0) break;
        m = t;
        scale--;
    }
    uint32_t h = m.w[0] ^ m.w[1] ^ m.w[2] ^ (uint32_t)scale;
    if (DEC_NEG(d) && !big_is_zero(&m)) h ^= 0x80000000u;
    return (int32_t)h;
}

// ---------------------------------------------------------------------------------------------
// Rounding: Math.Round / decimal.Round / Floor / Ceiling / Truncate
// mode = System.MidpointRounding: ToEven 0, AwayFromZero 1, ToZero 2, ToNegativeInfinity 3,
// ToPositiveInfinity 4.
// ---------------------------------------------------------------------------------------------
void csrt_decimal_round(csrt_Decimal* r, const csrt_Decimal* a, int32_t decimals, int32_t mode) {
    int scale = DEC_SCALE(a);
    if (decimals < 0 || decimals > 28) {
        static const csrt_MethodTable mt_range = { NULL, "System.ArgumentOutOfRangeException", 0, 0 };
        csrt_throw(&mt_range, ascii("Decimal can only round to between 0 and 28 digits of precision. (Parameter 'decimals')"));
    }
    if (scale <= decimals) { *r = *a; return; }
    int k = scale - decimals;
    Big m = big_from(a), divisor = big_pow10(k), q, rem;
    big_divmod(&m, &divisor, &q, &rem);
    bool negative = DEC_NEG(a);
    bool up = false;
    if (!big_is_zero(&rem)) {
        Big twice = rem;
        big_shl1(&twice);
        int c = big_cmp(&twice, &divisor);
        switch (mode) {
            case 0: up = c > 0 || (c == 0 && big_is_odd(&q)); break;
            case 1: up = c >= 0; break;
            case 2: up = false; break;
            case 3: up = negative; break;
            case 4: up = !negative; break;
            default: up = c > 0 || (c == 0 && big_is_odd(&q)); break;
        }
    }
    if (up) { Big one = big_u64(1); big_add(&q, &one); }
    pack(r, &q, decimals, negative);
}

int csrt_decimal_digits(const csrt_Decimal* d, char* buf);

// ---------------------------------------------------------------------------------------------
// Conversions
// ---------------------------------------------------------------------------------------------
void csrt_decimal_from_i64(csrt_Decimal* r, int64_t v) {
    uint64_t mag = v < 0 ? (uint64_t)0 - (uint64_t)v : (uint64_t)v;
    r->lo = mag;
    r->hi = 0;
    r->flags = v < 0 ? (int32_t)DEC_SIGN : 0;
}

void csrt_decimal_from_u64(csrt_Decimal* r, uint64_t v) { r->lo = v; r->hi = 0; r->flags = 0; }

/// .NET 11 converts doubles exactly: the binary value's decimal expansion, rounded (half-even) to
/// what fits in a decimal, trailing zeros removed. (decimal)0.1 == 0.1000000000000000055511151231.
void csrt_decimal_from_f64(csrt_Decimal* r, double v) {
    if (isnan(v) || isinf(v) || fabs(v) >= 7.9228162514264338e28) decimal_overflow();
    if (v == 0) { r->lo = 0; r->hi = 0; r->flags = 0; return; }
    char buf[1200];
    snprintf(buf, sizeof buf, "%.1100f", fabs(v));  // exact expansion (glibc)
    char* point = strchr(buf, '.');
    // Build the integer of all digits, then trim: scale = number of fractional digits used.
    Big m = {{0}};
    int scale = 0;
    bool dropped_nonzero = false;
    int sticky_first = -1;  // first dropped digit
    for (char* p = buf; *p; p++) {
        if (*p == '.') continue;
        bool fractional = point && p > point;
        int digit = *p - '0';
        if (fractional) {
            // Keep a digit only if scale stays <= 28 and the mantissa keeps fitting.
            if (scale >= 28) {
                if (sticky_first < 0) sticky_first = digit;
                else if (digit) dropped_nonzero = true;
                continue;
            }
            Big t = m;
            big_mul_small(&t, 10);
            Big dg = big_u64((uint64_t)digit);
            big_add(&t, &dg);
            if (!big_fits96(&t)) {
                if (sticky_first < 0) sticky_first = digit;
                else if (digit) dropped_nonzero = true;
                scale = scale < 28 ? scale : 28;
                // stop accepting further fractional digits
                for (char* q = p + 1; *q; q++) if (*q != '0') dropped_nonzero = true;
                break;
            }
            m = t;
            scale++;
        } else {
            big_mul_small(&m, 10);
            Big dg = big_u64((uint64_t)digit);
            big_add(&m, &dg);
        }
    }
    if (sticky_first >= 0) {
        bool up = sticky_first > 5 || (sticky_first == 5 && (dropped_nonzero || big_is_odd(&m)));
        if (up) { Big one = big_u64(1); big_add(&m, &one); }
    }
    // Strip trailing zeros.
    while (scale > 0) {
        Big t = m;
        if (big_divmod_small(&t, 10) != 0) break;
        m = t;
        scale--;
    }
    fit(r, m, scale, v < 0);
}

void csrt_decimal_from_f32(csrt_Decimal* r, float v) { csrt_decimal_from_f64(r, (double)v); }

/// (double)decimal, correctly rounded (what .NET 11 produces: (double)(1m/3m) == 0.3333333333333333).
double csrt_decimal_to_f64(const csrt_Decimal* d) {
    char digits[40], text[64];
    int n = csrt_decimal_digits(d, digits);
    snprintf(text, sizeof text, "%s%se-%d", DEC_NEG(d) ? "-" : "", digits, DEC_SCALE(d));
    (void)n;
    return strtod(text, NULL);
}

float csrt_decimal_to_f32(const csrt_Decimal* d) { return (float)csrt_decimal_to_f64(d); }

/// Truncating conversion to an integer type; kind: 1 sbyte, 2 byte, 3 short, 4 ushort, 5 int,
/// 6 uint, 7 long, 8 ulong, 9 char. Out of range throws OverflowException (always checked).
int64_t csrt_decimal_to_integer(const csrt_Decimal* d, int32_t kind) {
    Big m = big_from(d);
    for (int s = DEC_SCALE(d); s > 0; s--) big_divmod_small(&m, 10);
    bool negative = DEC_NEG(d) && !big_is_zero(&m);
    static const char* names[] = { "", "an SByte", "a Byte", "an Int16", "a UInt16", "an Int32", "a UInt32", "an Int64", "a UInt64", "a Char" };
    static const int64_t mins[] = { 0, -128, 0, -32768, 0, INT32_MIN, 0, INT64_MIN, 0, 0 };
    static const uint64_t maxs[] = { 0, 127, 255, 32767, 65535, INT32_MAX, UINT32_MAX, INT64_MAX, UINT64_MAX, 65535 };
    bool ok = m.w[2] == 0 && big_fits96(&m);
    uint64_t mag = (uint64_t)m.w[0] | ((uint64_t)m.w[1] << 32);
    if (ok) {
        if (negative) ok = mag <= (uint64_t)0 - (uint64_t)mins[kind] && (mins[kind] < 0 || mag == 0);
        else ok = mag <= maxs[kind];
    }
    if (!ok) {
        char msg[96];
        snprintf(msg, sizeof msg, "Value was either too large or too small for %s.", names[kind]);
        csrt_throw(&mt_overflow, ascii(msg));
    }
    return negative ? (int64_t)((uint64_t)0 - mag) : (int64_t)mag;
}

// ---------------------------------------------------------------------------------------------
// Text
// ---------------------------------------------------------------------------------------------
/// Digits of the mantissa (no sign/point) into buf; returns the digit count.
int csrt_decimal_digits(const csrt_Decimal* d, char* buf) {
    Big m = big_from(d);
    char tmp[40];
    int n = 0;
    do { tmp[n++] = (char)('0' + big_divmod_small(&m, 10)); } while (!big_is_zero(&m));
    for (int i = 0; i < n; i++) buf[i] = tmp[n - 1 - i];
    buf[n] = 0;
    return n;
}

csrt_String* csrt_decimal_to_string(const csrt_Decimal* d) {
    char digits[40], out[48];
    int n = csrt_decimal_digits(d, digits);
    int scale = DEC_SCALE(d);
    bool zero = d->lo == 0 && d->hi == 0;
    int o = 0;
    if (DEC_NEG(d) && !zero) out[o++] = '-';
    if (scale >= n) {
        out[o++] = '0';
        out[o++] = '.';
        for (int i = 0; i < scale - n; i++) out[o++] = '0';
        for (int i = 0; i < n; i++) out[o++] = digits[i];
    } else {
        for (int i = 0; i < n; i++) {
            if (i == n - scale && scale > 0) out[o++] = '.';
            out[o++] = digits[i];
        }
    }
    out[o] = 0;
    return ascii(out);
}

/// decimal.Parse with NumberStyles.Number (invariant): whitespace, sign, thousands separators,
/// decimal point. Returns false on a format error; *overflow set when out of range.
static bool parse(csrt_String* s, csrt_Decimal* r, bool* overflow) {
    *overflow = false;
    if (!s) return false;
    int32_t i = 0, n = s->length;
    while (i < n && (s->chars[i] == ' ' || (s->chars[i] >= 9 && s->chars[i] <= 13))) i++;
    bool negative = false;
    if (i < n && (s->chars[i] == '-' || s->chars[i] == '+')) { negative = s->chars[i] == '-'; i++; }
    Big m = {{0}};
    int scale = 0, digits = 0;
    bool point = false, any = false, round_up_pending = false, dropped = false;
    int first_dropped = -1;
    for (; i < n; i++) {
        uint16_t c = s->chars[i];
        if (c == ',' && !point && any) continue;
        if (c == '.' && !point) { point = true; continue; }
        if (c < '0' || c > '9') break;
        any = true;
        int dg = c - '0';
        Big t = m;
        big_mul_small(&t, 10);
        Big dgb = big_u64((uint64_t)dg);
        big_add(&t, &dgb);
        if (big_fits96(&t) && (!point || scale < 28)) {
            m = t;
            if (point) scale++;
            if (dg || digits) digits++;
        } else if (point) {
            if (first_dropped < 0) first_dropped = dg; else if (dg) dropped = true;
        } else {
            *overflow = true;
            return false;
        }
    }
    (void)round_up_pending;
    while (i < n && (s->chars[i] == ' ' || (s->chars[i] >= 9 && s->chars[i] <= 13))) i++;
    if (!any || i != n) return false;
    if (first_dropped >= 0 && (first_dropped > 5 || (first_dropped == 5 && (dropped || big_is_odd(&m))))) {
        Big one = big_u64(1);
        big_add(&m, &one);
        if (!big_fits96(&m)) { *overflow = true; return false; }
    }
    pack(r, &m, scale, negative);
    return true;
}

void csrt_decimal_parse(csrt_Decimal* r, csrt_String* s) {
    if (!s) csrt_throw(&mt_argument_null, ascii("Value cannot be null. (Parameter 's')"));
    bool overflow;
    if (parse(s, r, &overflow)) return;
    if (overflow) decimal_overflow();
    csrt_throw(&mt_format, ascii("The input string was not in a correct format."));
}

bool csrt_decimal_try_parse(csrt_String* s, csrt_Decimal* r) {
    bool overflow;
    if (parse(s, r, &overflow)) return true;
    r->lo = 0; r->hi = 0; r->flags = 0;
    return false;
}

// ---------------------------------------------------------------------------------------------
// Boxed decimal: { mt, payload @ 8 }
// ---------------------------------------------------------------------------------------------
static csrt_String* box_to_string(void* o) { return csrt_decimal_to_string((const csrt_Decimal*)((char*)o + 8)); }
static bool box_equals(void* self, void* other) {
    return other && ((csrt_Object*)other)->mt == ((csrt_Object*)self)->mt &&
           csrt_decimal_compare((const csrt_Decimal*)((char*)self + 8), (const csrt_Decimal*)((char*)other + 8)) == 0;
}
static int32_t box_hash(void* self) { return csrt_decimal_get_hash_code((const csrt_Decimal*)((char*)self + 8)); }

const csrt_BuiltinMethodTable csrt_Boxed_Decimal_MT = {
    { &csrt_Object_MT.header, "System.Decimal", 8 + sizeof(csrt_Decimal), 0 },
    { (void*)box_to_string, (void*)box_equals, (void*)box_hash },
};
