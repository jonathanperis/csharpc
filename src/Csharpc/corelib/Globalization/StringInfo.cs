// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/
// Text/Unicode/{TextSegmentationUtility,GraphemeClusterBreakType}.cs and Globalization/{StringInfo,
// TextElementEnumerator}.cs; see THIRD-PARTY-NOTICES.md. Text elements are UAX #29 extended grapheme
// clusters, with the break property of every code point from .NET's own data (GraphemeData.g.cs).
#nullable enable annotations
#nullable disable warnings

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Buffers;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.InteropServices;
    using System.Text;

    // ---- TextSegmentationUtility.cs ----
    /// <summary>
    /// Provides helper utilities for computing text segmentation boundaries
    /// as specified in UAX#29 (https://www.unicode.org/reports/tr29/).
    /// </summary>
    /// <remarks>
    /// The current implementation is compliant per Rev. 35, https://www.unicode.org/reports/tr29/tr29-35.html.
    /// </remarks>
    internal static class TextSegmentationUtility
    {
        private delegate OperationStatus DecodeFirstRune<T>(ReadOnlySpan<T> input, out Rune rune, out int elementsConsumed);

        private static readonly DecodeFirstRune<char> _utf16Decoder = Rune.DecodeFromUtf16;

        private static int GetLengthOfFirstExtendedGraphemeCluster<T>(ReadOnlySpan<T> input, DecodeFirstRune<T> decoder)
        {
            // Algorithm given at https://www.unicode.org/reports/tr29/#Grapheme_Cluster_Boundary_Rules.

            Processor<T> processor = new Processor<T>(input, decoder);
            processor.MoveNext();

            // First, consume as many Prepend scalars as we can (rule GB9b).

            while (processor.CurrentType == GraphemeClusterBreakType.Prepend)
            {
                processor.MoveNext();
            }

            // Next, make sure we're not about to violate control character restrictions.
            // Essentially, if we saw Prepend data, we can't have Control | CR | LF data afterward (rule GB5).

            if (processor.CurrentCodeUnitOffset > 0)
            {
                if (processor.CurrentType == GraphemeClusterBreakType.Control
                    || processor.CurrentType == GraphemeClusterBreakType.CR
                    || processor.CurrentType == GraphemeClusterBreakType.LF)
                {
                    goto Return;
                }
            }

            // Now begin the main state machine.

            GraphemeClusterBreakType previousClusterBreakType = processor.CurrentType;
            processor.MoveNext();

            switch (previousClusterBreakType)
            {
                case GraphemeClusterBreakType.CR:
                    if (processor.CurrentType != GraphemeClusterBreakType.LF)
                    {
                        goto Return; // rules GB3 & GB4 (only <LF> can follow <CR>)
                    }

                    processor.MoveNext();
                    goto case GraphemeClusterBreakType.LF;

                case GraphemeClusterBreakType.Control:
                case GraphemeClusterBreakType.LF:
                    goto Return; // rule GB4 (no data after Control | LF)

                case GraphemeClusterBreakType.L:
                    if (processor.CurrentType == GraphemeClusterBreakType.L)
                    {
                        processor.MoveNext(); // rule GB6 (L x L)
                        goto case GraphemeClusterBreakType.L;
                    }
                    else if (processor.CurrentType == GraphemeClusterBreakType.V)
                    {
                        processor.MoveNext(); // rule GB6 (L x V)
                        goto case GraphemeClusterBreakType.V;
                    }
                    else if (processor.CurrentType == GraphemeClusterBreakType.LV)
                    {
                        processor.MoveNext(); // rule GB6 (L x LV)
                        goto case GraphemeClusterBreakType.LV;
                    }
                    else if (processor.CurrentType == GraphemeClusterBreakType.LVT)
                    {
                        processor.MoveNext(); // rule GB6 (L x LVT)
                        goto case GraphemeClusterBreakType.LVT;
                    }
                    else
                    {
                        break;
                    }

                case GraphemeClusterBreakType.LV:
                case GraphemeClusterBreakType.V:
                    if (processor.CurrentType == GraphemeClusterBreakType.V)
                    {
                        processor.MoveNext(); // rule GB7 (LV | V x V)
                        goto case GraphemeClusterBreakType.V;
                    }
                    else if (processor.CurrentType == GraphemeClusterBreakType.T)
                    {
                        processor.MoveNext(); // rule GB7 (LV | V x T)
                        goto case GraphemeClusterBreakType.T;
                    }
                    else
                    {
                        break;
                    }

                case GraphemeClusterBreakType.LVT:
                case GraphemeClusterBreakType.T:
                    if (processor.CurrentType == GraphemeClusterBreakType.T)
                    {
                        processor.MoveNext(); // rule GB8 (LVT | T x T)
                        goto case GraphemeClusterBreakType.T;
                    }
                    else
                    {
                        break;
                    }

                case GraphemeClusterBreakType.Extended_Pictograph:
                    // Attempt processing extended pictographic (rules GB11, GB9).
                    // First, drain any Extend scalars that might exist

                    while (processor.CurrentType == GraphemeClusterBreakType.Extend)
                    {
                        processor.MoveNext();
                    }

                    // Now see if there's a ZWJ + extended pictograph again.

                    if (processor.CurrentType != GraphemeClusterBreakType.ZWJ)
                    {
                        break;
                    }

                    processor.MoveNext();
                    if (processor.CurrentType != GraphemeClusterBreakType.Extended_Pictograph)
                    {
                        break;
                    }

                    processor.MoveNext();
                    goto case GraphemeClusterBreakType.Extended_Pictograph;

                case GraphemeClusterBreakType.Regional_Indicator:
                    // We've consumed a single RI scalar. Try to consume another (to make it a pair).

                    if (processor.CurrentType == GraphemeClusterBreakType.Regional_Indicator)
                    {
                        processor.MoveNext();
                    }

                    // Standalone RI scalars (or a single pair of RI scalars) can only be followed by trailers.

                    break; // nothing but trailers after the final RI

                default:
                    break;
            }

            // rules GB9, GB9a
            while (processor.CurrentType == GraphemeClusterBreakType.Extend
                || processor.CurrentType == GraphemeClusterBreakType.ZWJ
                || processor.CurrentType == GraphemeClusterBreakType.SpacingMark)
            {
                processor.MoveNext();
            }

        Return:

            return processor.CurrentCodeUnitOffset; // rules GB2, GB999
        }

        /// <summary>
        /// Given UTF-16 input text, returns the length (in chars) of the first extended grapheme cluster.
        /// The slice [0..length] represents the first standalone extended grapheme cluster in the text.
        /// If the input is empty, returns 0.
        /// </summary>
        public static int GetLengthOfFirstUtf16ExtendedGraphemeCluster(ReadOnlySpan<char> input)
        {
            return GetLengthOfFirstExtendedGraphemeCluster(input, _utf16Decoder);
        }

        [StructLayout(LayoutKind.Auto)]
        private ref struct Processor<T>
        {
            private readonly ReadOnlySpan<T> _buffer;
            private readonly DecodeFirstRune<T> _decoder;
            private int _codeUnitLengthOfCurrentScalar;

            internal Processor(ReadOnlySpan<T> buffer, DecodeFirstRune<T> decoder)
            {
                _buffer = buffer;
                _decoder = decoder;
                _codeUnitLengthOfCurrentScalar = 0;
                CurrentType = GraphemeClusterBreakType.Other;
                CurrentCodeUnitOffset = 0;
            }

            public int CurrentCodeUnitOffset { get; private set; }

            /// <summary>
            /// Will be <see cref="GraphemeClusterBreakType.Other"/> if invalid data or EOF reached.
            /// Caller shouldn't need to special-case this since the normal rules will halt on this condition.
            /// </summary>
            public GraphemeClusterBreakType CurrentType { get; private set; }

            public void MoveNext()
            {
                // For ill-formed subsequences (like unpaired UTF-16 surrogate code points), we rely on
                // the decoder's default behavior of interpreting these ill-formed subsequences as
                // equivalent to U+FFFD REPLACEMENT CHARACTER. This code point has a boundary property
                // of Other (XX), which matches the modifications made to UAX#29, Rev. 35.
                // See: https://www.unicode.org/reports/tr29/tr29-35.html#Modifications
                // This change is also reflected in the UCD files. For example, Unicode 11.0's UCD file
                // https://www.unicode.org/Public/11.0.0/ucd/auxiliary/GraphemeBreakProperty.txt
                // has the line "D800..DFFF    ; Control # Cs [2048] <surrogate-D800>..<surrogate-DFFF>",
                // but starting with Unicode 12.0 that line has been removed.
                //
                // If a later version of the Unicode Standard further modifies this guidance we should reflect
                // that here.

                CurrentCodeUnitOffset += _codeUnitLengthOfCurrentScalar;
                _decoder(_buffer.Slice(CurrentCodeUnitOffset), out Rune thisRune, out _codeUnitLengthOfCurrentScalar);
                CurrentType = GraphemeBreaks.Of(thisRune);
            }
        }
    }

    // ---- GraphemeClusterBreakType.cs ----
    // Grapheme cluster break property values, as specified in
    // https://www.unicode.org/reports/tr29/#Grapheme_Cluster_Boundaries, Sec. 3.1.
    internal enum GraphemeClusterBreakType
    {
        Other,
        CR,
        LF,
        Control,
        Extend,
        ZWJ,
        Regional_Indicator,
        Prepend,
        SpacingMark,
        L,
        V,
        T,
        LV,
        LVT,
        Extended_Pictograph,
    }

    // ---- StringInfo.cs ----
    /// <summary>
    /// This class defines behaviors specific to a writing system.
    /// A writing system is the collection of scripts and orthographic rules
    /// required to represent a language as text.
    /// </summary>
    [Shadows("System.Globalization.StringInfo")]
    public class StringInfoImpl
    {
        private string _str;

        private int[]? _indexes;

        public StringInfoImpl() : this(string.Empty)
        {
        }

        public StringInfoImpl(string value)
        {
            this.String = value;
        }

        public override bool Equals(object? value)
        {
            return value is StringInfo otherStringInfo
                && _str.Equals(((StringInfoImpl)(object)otherStringInfo)._str);
        }

        public override int GetHashCode() => _str.GetHashCode();

        /// <summary>
        /// Our zero-based array of index values into the string. Initialize if
        /// our private array is not yet, in fact, initialized.
        /// </summary>
        private int[]? Indexes
        {
            get
            {
                if (_indexes == null && String.Length > 0)
                {
                    _indexes = ParseCombiningCharacters(String);
                }

                return _indexes;
            }
        }

        public string String
        {
            get => _str;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _str = value;
                _indexes = null;
            }
        }

        public int LengthInTextElements => Indexes?.Length ?? 0;

        public string SubstringByTextElements(int startingTextElement)
        {
            return SubstringByTextElements(startingTextElement, (Indexes?.Length ?? 0) - startingTextElement);
        }

        public string SubstringByTextElements(int startingTextElement, int lengthInTextElements)
        {
            int[] indexes = Indexes ?? [];

            if ((uint)startingTextElement >= (uint)indexes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startingTextElement), startingTextElement, "Specified argument was out of the range of valid values.");
            }
            if ((uint)lengthInTextElements > (uint)(indexes.Length - startingTextElement))
            {
                throw new ArgumentOutOfRangeException(nameof(lengthInTextElements), lengthInTextElements, "Specified argument was out of the range of valid values.");
            }

            int start = indexes[startingTextElement];
            Index end = ^0; // assume reading to end of the string unless the caller told us to stop early

            if ((uint)(startingTextElement + lengthInTextElements) < (uint)indexes.Length)
            {
                end = indexes[startingTextElement + lengthInTextElements];
            }

            return String[start..end];
        }

        /// <summary>
        /// Returns the first text element (extended grapheme cluster) that occurs in the input string.
        /// </summary>
        /// <remarks>
        /// A grapheme cluster is a sequence of one or more Unicode code points that should be treated as a single unit.
        /// </remarks>
        /// <param name="str">The input string to analyze.</param>
        /// <returns>The substring corresponding to the first text element within <paramref name="str"/>,
        /// or the empty string if <paramref name="str"/> is empty.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="str"/> is null.</exception>
        public static string GetNextTextElement(string str) => GetNextTextElement(str, 0);

        /// <summary>
        /// Returns the first text element (extended grapheme cluster) that occurs in the input string
        /// starting at the specified index.
        /// </summary>
        /// <remarks>
        /// A grapheme cluster is a sequence of one or more Unicode code points that should be treated as a single unit.
        /// </remarks>
        /// <param name="str">The input string to analyze.</param>
        /// <param name="index">The char offset in <paramref name="str"/> at which to begin analysis.</param>
        /// <returns>The substring corresponding to the first text element within <paramref name="str"/> starting
        /// at index <paramref name="index"/>, or the empty string if <paramref name="index"/> corresponds to
        /// the end of <paramref name="str"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="str"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or beyond the end of <paramref name="str"/>.</exception>
        public static string GetNextTextElement(string str, int index)
        {
            int nextTextElementLength = GetNextTextElementLength(str, index);
            return str.Substring(index, nextTextElementLength);
        }

        /// <summary>
        /// Returns the length of the first text element (extended grapheme cluster) that occurs in the input string.
        /// </summary>
        /// <remarks>
        /// A grapheme cluster is a sequence of one or more Unicode code points that should be treated as a single unit.
        /// </remarks>
        /// <param name="str">The input string to analyze.</param>
        /// <returns>The length (in chars) of the substring corresponding to the first text element within <paramref name="str"/>,
        /// or 0 if <paramref name="str"/> is empty.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="str"/> is null.</exception>
        public static int GetNextTextElementLength(string str) => GetNextTextElementLength(str, 0);

        /// <summary>
        /// Returns the length of the first text element (extended grapheme cluster) that occurs in the input string
        /// starting at the specified index.
        /// </summary>
        /// <remarks>
        /// A grapheme cluster is a sequence of one or more Unicode code points that should be treated as a single unit.
        /// </remarks>
        /// <param name="str">The input string to analyze.</param>
        /// <param name="index">The char offset in <paramref name="str"/> at which to begin analysis.</param>
        /// <returns>The length (in chars) of the substring corresponding to the first text element within <paramref name="str"/> starting
        /// at index <paramref name="index"/>, or 0 if <paramref name="index"/> corresponds to the end of <paramref name="str"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="str"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or beyond the end of <paramref name="str"/>.</exception>
        public static int GetNextTextElementLength(string str, int index)
        {
            if (str is null)
            {
                throw new ArgumentNullException("str");
            }
            if ((uint)index > (uint)str.Length)
            {
                throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            }

            return GetNextTextElementLength(str.AsSpan(index));
        }

        /// <summary>
        /// Returns the length of the first text element (extended grapheme cluster) that occurs in the input span.
        /// </summary>
        /// <remarks>
        /// A grapheme cluster is a sequence of one or more Unicode code points that should be treated as a single unit.
        /// </remarks>
        /// <param name="str">The input span to analyze.</param>
        /// <returns>The length (in chars) of the substring corresponding to the first text element within <paramref name="str"/>,
        /// or 0 if <paramref name="str"/> is empty.</returns>
        public static int GetNextTextElementLength(ReadOnlySpan<char> str) => TextSegmentationUtility.GetLengthOfFirstUtf16ExtendedGraphemeCluster(str);

        public static TextElementEnumerator GetTextElementEnumerator(string str) => GetTextElementEnumerator(str, 0);

        public static TextElementEnumerator GetTextElementEnumerator(string str, int index)
        {
            if (str is null)
            {
                throw new ArgumentNullException("str");
            }
            if ((uint)index > (uint)str.Length)
            {
                throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            }

            return (TextElementEnumerator)(object)new TextElementEnumeratorImpl(str, index);
        }

        /// <summary>
        /// Returns the indices of each base character or properly formed surrogate
        /// pair  within the str. It recognizes a base character plus one or more
        /// combining characters or a properly formed surrogate pair as a text
        /// element and returns the index of the base character or high surrogate.
        /// Each index is the beginning of a text element within a str. The length
        /// of each element is easily computed as the difference between successive
        /// indices. The length of the array will always be less than or equal to
        /// the length of the str. For example, given the str
        /// \u4f00\u302a\ud800\udc00\u4f01, this method would return the indices:
        /// 0, 2, 4.
        /// </summary>
        public static unsafe int[] ParseCombiningCharacters(string str)
        {
            if (str is null)
            {
                throw new ArgumentNullException("str");
            }

            var builder = new List<int>();
            ReadOnlySpan<char> remaining = str;

            while (!remaining.IsEmpty)
            {
                builder.Add(str.Length - remaining.Length); // a new extended grapheme cluster begins at this offset
                remaining = remaining.Slice(GetNextTextElementLength(remaining)); // consume this cluster
            }

            int[] retVal = builder.ToArray();

            return retVal;
        }
    }

    // ---- TextElementEnumerator.cs ----
    [Shadows("System.Globalization.TextElementEnumerator")]
    public class TextElementEnumeratorImpl : IEnumerator
    {
        private readonly string _str;
        private readonly int _strStartIndex; // where in _str the enumeration should begin

        private int _currentTextElementOffset;
        private int _currentTextElementLength;
        private string? _currentTextElementSubstr;

        internal TextElementEnumeratorImpl(string str, int startIndex)
        {

            _str = str;
            _strStartIndex = startIndex;

            Reset();
        }

        public bool MoveNext()
        {
            _currentTextElementSubstr = null; // clear any cached substr

            int newOffset = _currentTextElementOffset + _currentTextElementLength;
            _currentTextElementOffset = newOffset; // advance
            _currentTextElementLength = 0; // prevent future calls to MoveNext() or get_Current from succeeding if we've hit end of data

            if (newOffset >= _str.Length)
            {
                return false; // reached the end of the data
            }

            _currentTextElementLength = TextSegmentationUtility.GetLengthOfFirstUtf16ExtendedGraphemeCluster(_str.AsSpan(newOffset));
            return true;
        }

        public object Current => GetTextElement();

        public string GetTextElement()
        {
            // Returned the cached substr if we've already generated it.
            // Otherwise perform the substr operation now.

            string? currentSubstr = _currentTextElementSubstr;
            if (currentSubstr is null)
            {
                if (_currentTextElementOffset >= _str.Length)
                {
                    throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                }

                currentSubstr = _str.Substring(_currentTextElementOffset, _currentTextElementLength);
                _currentTextElementSubstr = currentSubstr;
            }

            return currentSubstr;
        }

        public int ElementIndex
        {
            get
            {
                if (_currentTextElementOffset >= _str.Length)
                {
                    throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                }

                return _currentTextElementOffset - _strStartIndex;
            }
        }

        public void Reset()
        {
            // These first two fields are set to intentionally out-of-range values.
            // They'll be fixed up once the enumerator starts.

            _currentTextElementOffset = _str.Length;
            _currentTextElementLength = _strStartIndex - _str.Length;
            _currentTextElementSubstr = null;
        }
    }

    /// <summary>CharUnicodeInfo.GetGraphemeClusterBreakType over the generated breakpoints.</summary>
    internal static class GraphemeBreaks
    {
        internal static GraphemeClusterBreakType Of(Rune rune)
        {
            int codePoint = rune.Value;
            ReadOnlySpan<byte> table = GraphemeData.Breakpoints;
            int lo = 0, hi = GraphemeData.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (Entry(table, mid) >> 4 <= codePoint) lo = mid; else hi = mid - 1;
            }
            return (GraphemeClusterBreakType)(Entry(table, lo) & 15);
        }

        private static int Entry(ReadOnlySpan<byte> table, int i) =>
            ((table[5 * i] - '0') << 24) | ((table[5 * i + 1] - '0') << 18) | ((table[5 * i + 2] - '0') << 12) | ((table[5 * i + 3] - '0') << 6) | (table[5 * i + 4] - '0');
    }
}
