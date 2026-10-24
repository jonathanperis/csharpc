// StringComparison.OrdinalIgnoreCase, as .NET's Ordinal.CompareStringIgnoreCase / OrdinalCasing:
// code points (valid surrogate pairs decoded) are upper-cased with the invariant simple mapping and
// compared; the result is the difference of the first differing upper-cased code points, else the
// difference of the lengths. Matches .NET's own Unicode data (what .NET uses in invariant
// globalization mode, which is csharpc's culture model).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class OrdinalCasing
    {
        /// <summary>Simple upper-case mapping of a code point for ordinal ignore-case operations.</summary>
        public static int ToUpper(int cp)
        {
            if (cp < 0x10000)
            {
                // .NET's ordinal casing leaves U+017F (long s) alone, unlike some ToUpperInvariant data.
                if (cp == 0x017F) return cp;
                return CharOps.ToUpperTable((char)cp);
            }
            // Supplementary-plane case pairs (Deseret, Osage, Vithkuqi, Old Hungarian, Garay,
            // Warang Citi, Medefaidrin, Adlam), from .NET 11's Unicode data.
            if (cp >= 0x10428 && cp <= 0x1044F) return cp - 40;
            if (cp >= 0x104D8 && cp <= 0x104FB) return cp - 40;
            if (cp >= 0x10597 && cp <= 0x105BC && cp != 0x105A2 && cp != 0x105B2 && cp != 0x105BA) return cp - 39;
            if (cp >= 0x10CC0 && cp <= 0x10CF2) return cp - 64;
            if (cp >= 0x10D70 && cp <= 0x10D85) return cp - 32;
            if (cp >= 0x118C0 && cp <= 0x118DF) return cp - 32;
            if (cp >= 0x16E60 && cp <= 0x16E7F) return cp - 32;
            if (cp >= 0x1E922 && cp <= 0x1E943) return cp - 34;
            return cp;
        }

        /// <summary>Simple lower-case mapping of a code point (the inverse of the supplementary pairs above).</summary>
        public static int ToLower(int cp)
        {
            if (cp < 0x10000) return CharOps.ToLowerTable((char)cp);
            if (cp >= 0x10400 && cp <= 0x10427) return cp + 40;
            if (cp >= 0x104B0 && cp <= 0x104D3) return cp + 40;
            if (cp >= 0x10570 && cp <= 0x10595 && cp != 0x1057B && cp != 0x1058B && cp != 0x10593) return cp + 39;
            if (cp >= 0x10C80 && cp <= 0x10CB2) return cp + 64;
            if (cp >= 0x10D50 && cp <= 0x10D65) return cp + 32;
            if (cp >= 0x118A0 && cp <= 0x118BF) return cp + 32;
            if (cp >= 0x16E40 && cp <= 0x16E5F) return cp + 32;
            if (cp >= 0x1E900 && cp <= 0x1E921) return cp + 34;
            return cp;
        }

        /// <summary>Code point at i (a valid surrogate pair decoded, anything else as is) and its length in chars.</summary>
        private static int CodePointAt(ReadOnlySpan<char> s, int i, out int width)
        {
            char c = s[i];
            if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
            {
                width = 2;
                return 0x10000 + ((c - 0xD800) << 10) + (s[i + 1] - 0xDC00);
            }
            width = 1;
            return c;
        }

        public static int Compare(string strA, string strB)
        {
            if ((object)strA == strB) return 0;
            if (strA == null) return -1;
            if (strB == null) return 1;
            ReadOnlySpan<char> a = strA.AsSpan(), b = strB.AsSpan();
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char x = a[i], y = b[j];
                if (x < 0x80 && y < 0x80)
                {
                    if ((uint)(x - 'a') <= 'z' - 'a') x = (char)(x - 32);
                    if ((uint)(y - 'a') <= 'z' - 'a') y = (char)(y - 32);
                    if (x != y) return x - y;
                    i++; j++;
                    continue;
                }
                int p = ToUpper(CodePointAt(a, i, out int wa)), q = ToUpper(CodePointAt(b, j, out int wb));
                if (p != q) return p - q;
                i += wa; j += wb;
            }
            return a.Length - b.Length;
        }

        /// <summary>string.Equals(s, ascii, OrdinalIgnoreCase) with a non-null ASCII constant (BclValueTypes).</summary>
        public static bool EqualsAsciiIgnoreCase(string s, string ascii)
        {
            if (s == null || s.Length != ascii.Length) return false;
            for (int i = 0; i < s.Length; i++)
            {
                int x = s[i], y = ascii[i];
                if (x == y) continue;
                if ((uint)((x | 0x20) - 'a') > 'z' - 'a' || (x | 0x20) != (y | 0x20)) return false;
            }
            return true;
        }

        public static bool Equals(string a, string b)
        {
            if ((object)a == b) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            return Compare(a, b) == 0;
        }

        /// <summary>The string upper-cased the way Compare sees it (for ignore-case hash codes).</summary>
        public static string ToUpperOrdinal(string s)
        {
            ReadOnlySpan<char> src = s.AsSpan();
            string result = CoreIntrinsics.FastAllocateString(src.Length);
            Span<char> dst = CoreIntrinsics.StringChars(result);
            for (int i = 0; i < src.Length;)
            {
                int cp = ToUpper(CodePointAt(src, i, out int width));
                if (width == 2)
                {
                    dst[i] = (char)(0xD800 + ((cp - 0x10000) >> 10));
                    dst[i + 1] = (char)(0xDC00 + ((cp - 0x10000) & 0x3FF));
                }
                else dst[i] = (char)cp;
                i += width;
            }
            return result;
        }

        /// <summary>string.Compare/Equals(a, b, StringComparison): ordinal ignore-case here, the rest in csrt.</summary>
        public static int CompareWith(string a, string b, StringComparison comparisonType)
        {
            if ((uint)comparisonType > (uint)StringComparison.OrdinalIgnoreCase)
                throw new ArgumentException("The string comparison type passed in is currently not supported.", "comparisonType");
            if (comparisonType == StringComparison.OrdinalIgnoreCase) return Compare(a, b);
            return CoreIntrinsics.CompareStringsWith(a, b, (int)comparisonType);
        }

        public static bool EqualsWith(string a, string b, StringComparison comparisonType)
        {
            if ((uint)comparisonType > (uint)StringComparison.OrdinalIgnoreCase)
                throw new ArgumentException("The string comparison type passed in is currently not supported.", "comparisonType");
            if (comparisonType == StringComparison.OrdinalIgnoreCase) return Equals(a, b);
            return CoreIntrinsics.CompareStringsWith(a, b, (int)comparisonType) == 0;
        }
    }
}
