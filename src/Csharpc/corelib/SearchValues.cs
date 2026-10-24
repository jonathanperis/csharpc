// System.Buffers.SearchValues for bytes and chars (ASP.NET Core's HTTP parsers, System.Net.Http and
// Microsoft.Net.Http.Headers classify characters with them) and MemoryExtensions' searches over them.
// .NET picks a vectorized strategy per value set; the results are the same as this membership table.
#nullable disable
using System;
using System.Buffers;

namespace CsharpcCoreLib
{
    [Shadows("System.Buffers.SearchValues")]
    public static class SearchValuesFactory
    {
        public static SearchValues<byte> Create(ReadOnlySpan<byte> values)
        {
            var map = new bool[256];
            for (int i = 0; i < values.Length; i++) map[values[i]] = true;
            return (SearchValues<byte>)(object)new SearchValuesImpl<byte>(map);
        }

        public static SearchValues<char> Create(ReadOnlySpan<char> values)
        {
            int max = -1;
            for (int i = 0; i < values.Length; i++) if (values[i] > max) max = values[i];
            var map = new bool[max + 1];
            for (int i = 0; i < values.Length; i++) map[values[i]] = true;
            return (SearchValues<char>)(object)new SearchValuesImpl<char>(map);
        }
    }

    [Shadows("System.Buffers.SearchValues`1")]
    public class SearchValuesImpl<T> where T : IEquatable<T>
    {
        /// <summary>Membership by code unit (byte or char value); codes past the end are not members.</summary>
        internal readonly bool[] Map;

        internal SearchValuesImpl(bool[] map) => Map = map;

        public bool Contains(T value) => Has(Code(value));

        internal bool Has(int code) => (uint)code < (uint)Map.Length && Map[code];

        internal static int Code(T value) => typeof(T) == typeof(char) ? (char)(object)value : (byte)(object)value;

        internal static SearchValuesImpl<T> Of(SearchValues<T> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            return (SearchValuesImpl<T>)(object)values;
        }
    }

    [ShadowMembers("System.MemoryExtensions")]
    internal static class SearchValuesMemoryExtensions
    {
        public static int IndexOfAny<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOf(span, SearchValuesImpl<T>.Of(values), true);
        public static int IndexOfAny<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOf((ReadOnlySpan<T>)span, SearchValuesImpl<T>.Of(values), true);
        public static int IndexOfAnyExcept<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOf(span, SearchValuesImpl<T>.Of(values), false);
        public static int IndexOfAnyExcept<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOf((ReadOnlySpan<T>)span, SearchValuesImpl<T>.Of(values), false);
        public static int LastIndexOfAny<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T> => LastIndexOf(span, SearchValuesImpl<T>.Of(values), true);
        public static int LastIndexOfAny<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => LastIndexOf((ReadOnlySpan<T>)span, SearchValuesImpl<T>.Of(values), true);
        public static int LastIndexOfAnyExcept<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T> => LastIndexOf(span, SearchValuesImpl<T>.Of(values), false);
        public static int LastIndexOfAnyExcept<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => LastIndexOf((ReadOnlySpan<T>)span, SearchValuesImpl<T>.Of(values), false);
        public static bool ContainsAny<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOf(span, SearchValuesImpl<T>.Of(values), true) >= 0;
        public static bool ContainsAny<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOf((ReadOnlySpan<T>)span, SearchValuesImpl<T>.Of(values), true) >= 0;
        public static bool ContainsAnyExcept<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOf(span, SearchValuesImpl<T>.Of(values), false) >= 0;
        public static bool ContainsAnyExcept<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOf((ReadOnlySpan<T>)span, SearchValuesImpl<T>.Of(values), false) >= 0;

        private static int IndexOf<T>(ReadOnlySpan<T> span, SearchValuesImpl<T> values, bool member) where T : IEquatable<T>
        {
            for (int i = 0; i < span.Length; i++)
                if (values.Has(SearchValuesImpl<T>.Code(span[i])) == member) return i;
            return -1;
        }

        private static int LastIndexOf<T>(ReadOnlySpan<T> span, SearchValuesImpl<T> values, bool member) where T : IEquatable<T>
        {
            for (int i = span.Length - 1; i >= 0; i--)
                if (values.Has(SearchValuesImpl<T>.Code(span[i])) == member) return i;
            return -1;
        }
    }
}
