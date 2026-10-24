// System.Buffers.SearchValues: an immutable set of values to search spans for (SearchValues.Create for
// bytes, chars, and strings compared ordinally or ordinally ignoring case), Contains, and the
// MemoryExtensions searches that take one. The lookups are plain loops and hash sets: .NET's
// vectorized strategies only change speed, not results.
#nullable disable
using System;
using System.Buffers;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Buffers.SearchValues`1")]
    public class SearchValuesImpl<T> where T : IEquatable<T>
    {
        internal SearchValuesImpl() { }

        public bool Contains(T value) => ContainsCore(value);

        internal virtual bool ContainsCore(T value) => false;
    }

    internal sealed class ValueSetSearchValues<T> : SearchValuesImpl<T> where T : IEquatable<T>
    {
        private readonly T[] _values;
        internal ValueSetSearchValues(ReadOnlySpan<T> values) { _values = values.ToArray(); }

        internal override bool ContainsCore(T value)
        {
            foreach (T v in _values)
                if (v.Equals(value)) return true;
            return false;
        }
    }

    internal sealed class StringSearchValues : SearchValuesImpl<string>
    {
        private readonly HashSet<string> _set;
        private readonly string[] _values;
        internal readonly StringComparison Comparison;

        internal StringSearchValues(ReadOnlySpan<string> values, StringComparison comparison)
        {
            Comparison = comparison;
            _set = new HashSet<string>(comparison == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (string value in values)
            {
                if (value == null) throw new ArgumentNullException("values");
                _set.Add(value);
            }
            _values = new string[_set.Count];
            _set.CopyTo(_values);
        }

        internal override bool ContainsCore(string value) => value != null && _set.Contains(value);

        // The first position where any of the strings starts, as SearchValues<string>.IndexOfAny reports it.
        internal int IndexOfAny(ReadOnlySpan<char> span)
        {
            for (int i = 0; i < span.Length; i++)
                foreach (string value in _values)
                    if (value.Length <= span.Length - i && span.Slice(i, value.Length).Equals(value.AsSpan(), Comparison)) return i;
            foreach (string value in _values) if (value.Length == 0) return 0;
            return -1;
        }
    }

    [ShadowMembers("System.Buffers.SearchValues")]
    internal static class SearchValuesFactory
    {
        public static SearchValues<byte> Create(ReadOnlySpan<byte> values) => (SearchValues<byte>)(object)new ValueSetSearchValues<byte>(values);
        public static SearchValues<char> Create(ReadOnlySpan<char> values) => (SearchValues<char>)(object)new ValueSetSearchValues<char>(values);

        public static SearchValues<string> Create(ReadOnlySpan<string> values, StringComparison comparisonType)
        {
            if (comparisonType != StringComparison.Ordinal && comparisonType != StringComparison.OrdinalIgnoreCase)
                throw new ArgumentException("SearchValues<string> supports only StringComparison.Ordinal and StringComparison.OrdinalIgnoreCase.", "comparisonType");
            return (SearchValues<string>)(object)new StringSearchValues(values, comparisonType);
        }
    }

    [ShadowMembers("System.MemoryExtensions")]
    internal static class SearchValuesSpanSearches
    {
        private static SearchValuesImpl<T> Check<T>(SearchValues<T> values) where T : IEquatable<T>
        {
            if (values == null) throw new ArgumentNullException("values");
            return (SearchValuesImpl<T>)(object)values;
        }

        public static int IndexOfAny<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T>
        {
            var set = Check(values);
            for (int i = 0; i < span.Length; i++) if (set.ContainsCore(span[i])) return i;
            return -1;
        }

        public static int IndexOfAnyExcept<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T>
        {
            var set = Check(values);
            for (int i = 0; i < span.Length; i++) if (!set.ContainsCore(span[i])) return i;
            return -1;
        }

        public static int LastIndexOfAny<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T>
        {
            var set = Check(values);
            for (int i = span.Length - 1; i >= 0; i--) if (set.ContainsCore(span[i])) return i;
            return -1;
        }

        public static int LastIndexOfAnyExcept<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T>
        {
            var set = Check(values);
            for (int i = span.Length - 1; i >= 0; i--) if (!set.ContainsCore(span[i])) return i;
            return -1;
        }

        public static bool ContainsAny<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOfAny(span, values) >= 0;
        public static bool ContainsAnyExcept<T>(ReadOnlySpan<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOfAnyExcept(span, values) >= 0;

        public static int IndexOfAny<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOfAny((ReadOnlySpan<T>)span, values);
        public static int IndexOfAnyExcept<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOfAnyExcept((ReadOnlySpan<T>)span, values);
        public static int LastIndexOfAny<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => LastIndexOfAny((ReadOnlySpan<T>)span, values);
        public static int LastIndexOfAnyExcept<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => LastIndexOfAnyExcept((ReadOnlySpan<T>)span, values);
        public static bool ContainsAny<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOfAny((ReadOnlySpan<T>)span, values) >= 0;
        public static bool ContainsAnyExcept<T>(Span<T> span, SearchValues<T> values) where T : IEquatable<T> => IndexOfAnyExcept((ReadOnlySpan<T>)span, values) >= 0;

        public static int IndexOfAny(ReadOnlySpan<char> span, SearchValues<string> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            return ((StringSearchValues)(object)values).IndexOfAny(span);
        }

        public static int IndexOfAny(Span<char> span, SearchValues<string> values) => IndexOfAny((ReadOnlySpan<char>)span, values);
        public static bool ContainsAny(ReadOnlySpan<char> span, SearchValues<string> values) => IndexOfAny(span, values) >= 0;
        public static bool ContainsAny(Span<char> span, SearchValues<string> values) => IndexOfAny((ReadOnlySpan<char>)span, values) >= 0;
    }
}
