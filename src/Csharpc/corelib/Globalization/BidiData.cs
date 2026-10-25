// CharUnicodeInfo.GetBidiCategory (internal in .NET; IdnMapping's bidi rule) over the generated table.
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib.Globalization
{
    internal enum StrongBidiCategory
    {
        Other = 0x00,
        StrongLeftToRight = 0x20,
        StrongRightToLeft = 0x40,
    }

    internal static partial class BidiData
    {
        internal static StrongBidiCategory GetBidiCategory(ReadOnlySpan<char> s, int index) => Of(CodePointAt(s[index], index + 1 < s.Length ? s[index + 1] : '\0'));

        internal static StrongBidiCategory GetBidiCategory(StringBuilder s, int index) => Of(CodePointAt(s[index], index + 1 < s.Length ? s[index + 1] : '\0'));

        // .NET's GetCodePointFromString: a surrogate pair's scalar, else the char itself (a lone surrogate too).
        private static int CodePointAt(char c, char next) =>
            char.IsHighSurrogate(c) && char.IsLowSurrogate(next) ? char.ConvertToUtf32(c, next) : c;

        private static int Entry(int i)
        {
            ReadOnlySpan<byte> t = Breakpoints;
            int at = i * 4;
            return ((t[at] - '0') << 18) | ((t[at + 1] - '0') << 12) | ((t[at + 2] - '0') << 6) | (t[at + 3] - '0');
        }

        private static StrongBidiCategory Of(int codePoint)
        {
            int lo = 0, hi = Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if ((Entry(mid) >> 2) <= codePoint) lo = mid;
                else hi = mid - 1;
            }
            return (Entry(lo) & 3) switch { 1 => StrongBidiCategory.StrongLeftToRight, 2 => StrongBidiCategory.StrongRightToLeft, _ => StrongBidiCategory.Other };
        }
    }
}
