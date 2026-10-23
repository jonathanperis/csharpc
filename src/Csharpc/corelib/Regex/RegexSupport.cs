// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Support types for the System.Text.RegularExpressions port (see Regex.cs): the resource strings and
// small stand-ins for internal .NET helpers (ValueStringBuilder, ValueListBuilder, StructListBuilder,
// HexConverter, StackHelper, Hashtable, SearchValues) and for the parts of CultureInfo the engine reads.
// Ported from dotnet/runtime (release/11.0, commit bc123d0bd173); see THIRD-PARTY-NOTICES.md.
#nullable enable annotations

using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib.RegularExpressions
{
    /// <summary>Resources/Strings.resx of System.Text.RegularExpressions (the English messages .NET prints).</summary>
    internal static class SR
    {
        public static string Format(string format, object? arg0) => string.Format(format, arg0);
        public static string Format(string format, object? arg0, object? arg1) => string.Format(format, arg0, arg1);
        public static string Format(string format, object? arg0, object? arg1, object? arg2) => string.Format(format, arg0, arg1, arg2);

        public const string Generic = "Regular expression parser error '{0}' at offset {1}.";
        public const string AlternationHasNamedCapture = "Alternation conditions do not capture and cannot be named.";
        public const string AlternationHasComment = "Alternation conditions cannot be comments.";
        public const string Arg_ArrayPlusOffTooSmall = "Destination array is not long enough to copy all the items in the collection. Check array index and length.";
        public const string ShorthandClassInCharacterRange = "Cannot include class \\{0} in character range.";
        public const string BeginIndexNotNegative = "Start index cannot be less than 0 or greater than input length.";
        public const string QuantifierOrCaptureGroupOutOfRange = "Quantifier and capture group numbers must be less than or equal to Int32.MaxValue.";
        public const string CaptureGroupOfZero = "Capture number cannot be zero.";
        public const string CountTooSmall = "Count cannot be less than -1.";
        public const string EnumNotStarted = "Enumeration has either not started or has already finished.";
        public const string AlternationHasMalformedCondition = "Illegal conditional (?(...)) expression.";
        public const string IllegalDefaultRegexMatchTimeoutInAppDomain = "AppDomain data '{0}' contains the invalid value or object '{1}' for specifying a default matching timeout for System.Text.RegularExpressions.Regex.";
        public const string UnescapedEndingBackslash = "Illegal \\ at end of pattern.";
        public const string ReversedQuantifierRange = "Illegal {x,y} with x > y.";
        public const string InvalidUnicodePropertyEscape = "Incomplete \\p{X} character escape.";
        public const string CaptureGroupNameInvalid = "Invalid group name: Group names must begin with a word character.";
        public const string LengthNotNegative = "Length cannot be less than 0 or exceed input length.";
        public const string MalformedNamedReference = "Malformed \\k<...> named back reference.";
        public const string AlternationHasMalformedReference = "Conditional alternation is missing a closing parenthesis after the group number {0}.";
        public const string MalformedUnicodePropertyEscape = "Malformed \\p{X} character escape.";
        public const string MakeException = "Invalid pattern '{0}' at offset {1}. {2}";
        public const string MissingControlCharacter = "Missing control character.";
        public const string NestedQuantifiersNotParenthesized = "Nested quantifier '{0}'.";
        public const string NoResultOnFailed = "Result cannot be called on a failed Match.";
        public const string InsufficientClosingParentheses = "Not enough )'s.";
        public const string NotSupported_ReadOnlyCollection = "Collection is read-only.";
        public const string PlatformNotSupported_CompileToAssembly = "This platform does not support writing compiled regular expressions to an assembly. Use GeneratedRegexAttribute with the regular expression source generator instead.";
        public const string QuantifierAfterNothing = "Quantifier '{0}' following nothing.";
        public const string RegexMatchTimeoutException_Occurred = "The Regex engine has timed out while trying to match a pattern to an input string. This can occur for many reasons, including very large inputs or excessive backtracking caused by nested quantifiers, back-references and other factors.";
        public const string ReversedCharacterRange = "[x-y] range in reverse order.";
        public const string ExclusionGroupNotLast = "A subtraction must be the last element in a character class.";
        public const string InsufficientOrInvalidHexDigits = "Insufficient or invalid hexadecimal digits.";
        public const string AlternationHasTooManyConditions = "Too many | in (?()|).";
        public const string InsufficientOpeningParentheses = "Too many )'s.";
        public const string UndefinedNumberedReference = "Reference to undefined group number {0}.";
        public const string UndefinedNamedReference = "Reference to undefined group name '{0}'.";
        public const string AlternationHasUndefinedReference = "Conditional alternation refers to an undefined group number {0}.";
        public const string UnrecognizedUnicodeProperty = "Unknown property '{0}'.";
        public const string UnrecognizedControlCharacter = "Unrecognized control character.";
        public const string UnrecognizedEscape = "Unrecognized escape sequence \\{0}.";
        public const string InvalidGroupingConstruct = "Unrecognized grouping construct.";
        public const string UnterminatedBracket = "Unterminated [] set.";
        public const string UnterminatedComment = "Unterminated (?#...) comment.";
    }

    /// <summary>
    /// Debug.Assert/Fail compiled out: the corelib is parsed with the user's DEBUG symbol, and the
    /// asserts only document upstream's invariants. (Type lookup finds this before System.Diagnostics.Debug.)
    /// </summary>
    internal static class Debug
    {
        [System.Diagnostics.Conditional("CSHARPC_REGEX_ASSERTS")]
        public static void Assert(bool condition) { }

        [System.Diagnostics.Conditional("CSHARPC_REGEX_ASSERTS")]
        public static void Assert(bool condition, string? message) { }

        [System.Diagnostics.Conditional("CSHARPC_REGEX_ASSERTS")]
        public static void Fail(string? message) { }
    }

    /// <summary>
    /// The engine reads only the culture's name, to pick the case-equivalence behavior (invariant, Turkish
    /// or other): the current culture's (corelib Globalization), or the name [GeneratedRegex] gives.
    /// </summary>
    internal sealed class CultureInfo
    {
        private static CultureInfo? s_invariant;
        private readonly string _name;

        private CultureInfo(string name) => _name = name;

        public static CultureInfo InvariantCulture => s_invariant ??= new CultureInfo("");
        public static CultureInfo CurrentCulture
        {
            get
            {
                string name = System.Globalization.CultureInfo.CurrentCulture.Name;
                return name.Length == 0 ? InvariantCulture : new CultureInfo(name);
            }
        }
        public static CultureInfo FromName(string name) => name.Length == 0 ? InvariantCulture : new CultureInfo(name.ToLowerInvariant());
        public string Name => _name;
        public override string ToString() => _name;
    }

    /// <summary>The subset of System.Collections.Hashtable the engine uses (int or string keys, int values).</summary>
    internal sealed class Hashtable
    {
        private readonly Dictionary<object, object> _table = new Dictionary<object, object>();

        public int Count => _table.Count;

        public object? this[object key]
        {
            get => _table.TryGetValue(key, out object? value) ? value : null;
            set => _table[key] = value!;
        }

        public bool ContainsKey(object key) => _table.ContainsKey(key);

        public void Add(object key, object value) => _table.Add(key, value);

        public bool TryGetValue(object key, out int value)
        {
            if (_table.TryGetValue(key, out object? obj))
            {
                value = (int)obj;
                return true;
            }

            value = 0;
            return false;
        }

        // The IDictionaryEnumerator subset the engine uses (Key, Value), as a struct of its own: csharpc's
        // boxed structs (Dictionary's enumerator as IDictionaryEnumerator) have no interface maps.
        public Enumerator GetEnumerator() => new Enumerator(_table.GetEnumerator());

        public struct Enumerator
        {
            private Dictionary<object, object>.Enumerator _inner;

            public Enumerator(Dictionary<object, object>.Enumerator inner) => _inner = inner;

            public bool MoveNext() => _inner.MoveNext();
            public object Key => _inner.Current.Key;
            public object? Value => _inner.Current.Value;
        }
    }

    /// <summary>
    /// Stand-in for System.WeakReference&lt;T&gt; (the parsed-replacement cache of a Regex): a strong
    /// reference, which only keeps the last parsed replacement pattern alive as long as its Regex.
    /// </summary>
    internal sealed class WeakReference<T> where T : class?
    {
        private T _target;

        public WeakReference(T target) => _target = target;

        public bool TryGetTarget(out T target)
        {
            target = _target;
            return target != null;
        }

        public void SetTarget(T target) => _target = target;
    }

    internal static class StackHelper
    {
        // The optimizer stops recursing when the stack runs low; csharpc's threads have large stacks and
        // patterns nest only as deep as their parentheses, so keep optimizing.
        public static bool TryEnsureSufficientExecutionStack() => true;
    }

    internal static class HexConverter
    {
        public static int FromChar(int c) =>
            c >= '0' && c <= '9' ? c - '0' :
            c >= 'a' && c <= 'f' ? c - 'a' + 10 :
            c >= 'A' && c <= 'F' ? c - 'A' + 10 :
            0xFF;
    }

    internal struct StructListBuilder<T>
    {
        private T[] _array;
        private int _count;

        public int Count => _count;

        public Span<T> AsSpan() => _array == null ? default : new Span<T>(_array, 0, _count);

        public void Add(T item)
        {
            if (_array == null || _count == _array.Length)
            {
                T[] newArray = new T[_array == null ? 256 : _array.Length * 2];
                if (_array != null)
                {
                    Array.Copy(_array, newArray, _count);
                }
                _array = newArray;
            }

            _array[_count++] = item;
        }

        public void Dispose()
        {
            _array = null!;
            _count = 0;
        }
    }

    /// <summary>Stand-in for System.Buffers.SearchValues: a set of chars or (ordinal / ordinal-ignore-case) strings.</summary>
    internal static class SearchValues
    {
        public static SearchValues<char> Create(string values) => new SearchValues<char>(values);

        public static SearchValues<string> Create(string[] values, StringComparison comparison) =>
            new SearchValues<string>(values, comparison == StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class SearchValues<T>
    {
        internal readonly string? Chars;
        internal readonly string[]? Strings;
        internal readonly bool IgnoreCase;

        internal SearchValues(string chars) => Chars = chars;

        internal SearchValues(string[] strings, bool ignoreCase)
        {
            Strings = strings;
            IgnoreCase = ignoreCase;
        }
    }

    /// <summary>
    /// Stand-in for System.ReadOnlyMemory&lt;char&gt; over a string (the only use here: Replace's result
    /// segments), so segment lists hold a string reference rather than an interior pointer.
    /// </summary>
    internal readonly struct ReadOnlyMemory<T>
    {
        private readonly string? _text;
        private readonly int _start;
        public readonly int Length;

        public ReadOnlyMemory(string? text, int start, int length)
        {
            _text = text;
            _start = start;
            Length = length;
        }

        public static ReadOnlyMemory<T> Empty => default;

        public bool IsEmpty => Length == 0;

        public ReadOnlySpan<char> Span => _text == null ? default : _text.AsSpan(_start, Length);

        public ReadOnlyMemory<T> Slice(int start) => Slice(start, Length - start);

        public ReadOnlyMemory<T> Slice(int start, int length)
        {
            if ((uint)start > (uint)Length || (uint)length > (uint)(Length - start))
            {
                throw new ArgumentOutOfRangeException();
            }
            return new ReadOnlyMemory<T>(_text, _start + start, length);
        }

        public void CopyTo(char[] destination, int destinationIndex)
        {
            if (Length != 0)
            {
                _text!.CopyTo(_start, destination, destinationIndex, Length);
            }
        }

        public override string ToString() => _text == null ? "" : _text.Substring(_start, Length);
    }

    internal static class RegexSpanExtensions
    {
        public static ReadOnlyMemory<char> AsMemory(this string? text) =>
            new ReadOnlyMemory<char>(text, 0, text == null ? 0 : text.Length);

        public static ReadOnlyMemory<char> AsMemory(this string? text, int start, int length)
        {
            if (text == null)
            {
                if (start != 0 || length != 0) throw new ArgumentOutOfRangeException();
                return default;
            }
            if ((uint)start > (uint)text.Length || (uint)length > (uint)(text.Length - start))
            {
                throw new ArgumentOutOfRangeException();
            }
            return new ReadOnlyMemory<char>(text, start, length);
        }

        public static int IndexOfAny(this ReadOnlySpan<char> span, SearchValues<char> values)
        {
            string chars = values.Chars!;
            for (int i = 0; i < span.Length; i++)
            {
                if (chars.IndexOf(span[i]) >= 0)
                {
                    return i;
                }
            }
            return -1;
        }

        public static int IndexOfAny(this ReadOnlySpan<char> span, SearchValues<string> values)
        {
            // The earliest position where any of the strings starts.
            string[] strings = values.Strings!;
            for (int i = 0; i < span.Length; i++)
            {
                foreach (string s in strings)
                {
                    if (s.Length <= span.Length - i && StartsWith(span.Slice(i), s, values.IgnoreCase))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        private static bool StartsWith(ReadOnlySpan<char> span, string value, bool ignoreCase)
        {
            for (int j = 0; j < value.Length; j++)
            {
                char a = span[j], b = value[j];
                if (a != b && (!ignoreCase || char.ToUpperInvariant(a) != char.ToUpperInvariant(b)))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
