// The string operations csharpc's code generator calls itself (CoreLibRuntime.cs: string.Join and Concat over arrays and
// spans, range and list-pattern slices, new string(c, n) / new string(char[]), ordinal comparison). String's and Char's
// library members come from CoreLib's own IL (src/Csharpc/upstream-types/System.String.txt).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class StringRuntime
    {
        /// <summary>string.Equals(object): equal when obj is a string with the same characters (ordinal), as .NET's override.</summary>
        public static bool EqualsObject(string s, object obj) => obj is string other && s == other;

        /// <summary>Length sums are computed in 64 bits so they can't wrap; too long is OutOfMemoryException, as in .NET.</summary>
        private static string AllocateString(long length)
        {
            if (length > int.MaxValue) throw new OutOfMemoryException();
            return CoreIntrinsics.FastAllocateString((int)length);
        }

        private static string Copy(ReadOnlySpan<char> chars)
        {
            string result = CoreIntrinsics.FastAllocateString(chars.Length);
            chars.CopyTo(CoreIntrinsics.StringChars(result));
            return result;
        }

        public static string Join(string separator, string[] value)
        {
            if (value == null) throw new ArgumentNullException("value");
            // .NET's JoinCore (Concat(string[]) too): no copy for zero or one element; string.Empty or the element itself (#492).
            if (value.Length <= 1) return value.Length == 0 ? "" : value[0] ?? "";
            int separatorLength = separator == null ? 0 : separator.Length;
            long total = 0;
            for (int i = 0; i < value.Length; i++) total += (value[i] == null ? 0 : value[i].Length) + (i > 0 ? separatorLength : 0);
            if (total == 0) return ""; // several empty pieces: string.Empty too, as .NET's JoinCore
            string result = AllocateString(total);
            Span<char> dst = CoreIntrinsics.StringChars(result);
            int at = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (i > 0 && separatorLength > 0) { separator.AsSpan().CopyTo(dst.Slice(at)); at += separatorLength; }
                string item = value[i];
                if (item != null) { item.AsSpan().CopyTo(dst.Slice(at)); at += item.Length; }
            }
            return result;
        }

        public static int CompareOrdinal(string strA, string strB)
        {
            if ((object)strA == strB) return 0;
            if (strA == null) return -1;
            if (strB == null) return 1;
            ReadOnlySpan<char> a = strA.AsSpan(), b = strB.AsSpan();
            int n = a.Length < b.Length ? a.Length : b.Length;
            for (int i = 0; i < n; i++)
                if (a[i] != b[i]) return a[i] - b[i];
            return a.Length - b.Length;
        }

        /// <summary>new string(c, count).</summary>
        internal static string CreateRepeated(char c, int count)
        {
            ThrowHelper.IfNegative(count, "count");
            if (count == 0) return ""; // string.Empty, as .NET's constructor returns (#492)
            string result = CoreIntrinsics.FastAllocateString(count);
            CoreIntrinsics.StringChars(result).Fill(c);
            return result;
        }

        /// <summary>new string(char[]): null or no characters give string.Empty (#492), as .NET's constructor.</summary>
        internal static string CreateFromChars(char[] value) => value == null || value.Length == 0 ? "" : Copy(value);

        /// <summary>
        /// s[start..end] and list-pattern slices. Roslyn lowers s[a..b] to s.Substring(a, b - a) and codegen passes the range unchecked
        /// (FunctionCodeGen.Ranges), so this is String.Substring itself, from CoreLib's IL: its checks, messages and stack frames.
        /// </summary>
        internal static string Slice(string s, int startIndex, int length) => s.Substring(startIndex, length);
    }
}
