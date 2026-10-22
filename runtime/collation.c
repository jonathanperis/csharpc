// csrt collation: culture-sensitive string comparison approximating ICU's root collation, which
// is what .NET uses for string.Compare / string.CompareTo / Comparer<string>.Default (invariant or
// en-* cultures). Three levels, compared in turn over the whole string:
//   primary   — base character: whitespace < punctuation/symbols < digits < letters (case- and
//               accent-insensitive), then everything else by code point;
//   secondary — accents (a < á < à < â < ä ...);
//   tertiary  — case (lowercase before uppercase).
// Exact for ASCII and Latin-1 words; other scripts fall back to code-point order at the primary level.
#include "csrt.h"

#include <string.h>

// ICU root order of the ASCII non-alphanumerics (variable characters are not ignorable in .NET).
static const char k_punct_order[] = " _-,;:!?.'\"()[]{}@*/\\&#%`^+<=>|~$";

typedef struct { uint32_t primary; uint8_t secondary; uint8_t tertiary; } Key;

// Latin-1 letters with diacritics: base letter + accent rank (secondary).
static int latin1_fold(uint16_t c, uint16_t* base, uint8_t* accent) {
    static const struct { uint16_t c; char base; uint8_t accent; } table[] = {
        {0xC0,'a',2},{0xC1,'a',1},{0xC2,'a',3},{0xC3,'a',5},{0xC4,'a',4},{0xC5,'a',6},{0xC7,'c',7},
        {0xC8,'e',2},{0xC9,'e',1},{0xCA,'e',3},{0xCB,'e',4},{0xCC,'i',2},{0xCD,'i',1},{0xCE,'i',3},{0xCF,'i',4},
        {0xD1,'n',5},{0xD2,'o',2},{0xD3,'o',1},{0xD4,'o',3},{0xD5,'o',5},{0xD6,'o',4},{0xD8,'o',8},
        {0xD9,'u',2},{0xDA,'u',1},{0xDB,'u',3},{0xDC,'u',4},{0xDD,'y',1},
        {0xE0,'a',2},{0xE1,'a',1},{0xE2,'a',3},{0xE3,'a',5},{0xE4,'a',4},{0xE5,'a',6},{0xE7,'c',7},
        {0xE8,'e',2},{0xE9,'e',1},{0xEA,'e',3},{0xEB,'e',4},{0xEC,'i',2},{0xED,'i',1},{0xEE,'i',3},{0xEF,'i',4},
        {0xF1,'n',5},{0xF2,'o',2},{0xF3,'o',1},{0xF4,'o',3},{0xF5,'o',5},{0xF6,'o',4},{0xF8,'o',8},
        {0xF9,'u',2},{0xFA,'u',1},{0xFB,'u',3},{0xFC,'u',4},{0xFD,'y',1},{0xFF,'y',4},
    };
    for (size_t i = 0; i < sizeof table / sizeof table[0]; i++)
        if (table[i].c == c) { *base = (uint16_t)table[i].base; *accent = table[i].accent; return 1; }
    return 0;
}

static Key key_of(uint16_t c) {
    Key k = {0, 0, 0};
    uint16_t base = c;
    uint8_t accent = 0;
    bool upper = false;
    if (c >= 0xC0 && c <= 0xFF && c != 0xD7 && c != 0xF7 && latin1_fold(c, &base, &accent)) {
        upper = c < 0xE0;
    } else if (c >= 'A' && c <= 'Z') {
        base = (uint16_t)(c + 32);
        upper = true;
    }
    if (base >= 'a' && base <= 'z') { k.primary = 2000 + (base - 'a') * 10; }
    else if (base >= '0' && base <= '9') { k.primary = 1000 + (base - '0'); }
    else if (base < 128) {
        const char* p = strchr(k_punct_order, (char)base);
        k.primary = p && base ? 100 + (uint32_t)(p - k_punct_order) : 500 + base;  // controls/others after punct
    } else {
        k.primary = 100000 + base;  // beyond Latin-1: code-point order after the Latin letters
    }
    k.secondary = accent;
    k.tertiary = upper ? 1 : 0;
    return k;
}

int32_t csrt_string_compare_culture(csrt_String* a, csrt_String* b) {
    if (a == b) return 0;
    if (!a) return -1;
    if (!b) return 1;
    int32_t n = a->length < b->length ? a->length : b->length;
    for (int level = 0; level < 3; level++) {
        for (int32_t i = 0; i < n; i++) {
            Key ka = key_of(a->chars[i]), kb = key_of(b->chars[i]);
            uint32_t wa = level == 0 ? ka.primary : level == 1 ? ka.secondary : ka.tertiary;
            uint32_t wb = level == 0 ? kb.primary : level == 1 ? kb.secondary : kb.tertiary;
            if (wa != wb) return wa < wb ? -1 : 1;
        }
        if (level == 0 && a->length != b->length) return a->length < b->length ? -1 : 1;
    }
    // Canonically equal at all three levels: break ties by code point so the order is total.
    for (int32_t i = 0; i < n; i++)
        if (a->chars[i] != b->chars[i]) return a->chars[i] < b->chars[i] ? -1 : 1;
    return 0;
}
