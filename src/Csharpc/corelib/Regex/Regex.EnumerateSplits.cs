// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib.RegularExpressions
{
    public partial class Regex
    {
        public static ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern) =>
            RegexCache.GetOrAdd(pattern).EnumerateSplits(input);

        public static ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options) =>
            RegexCache.GetOrAdd(pattern, options, s_defaultMatchTimeout).EnumerateSplits(input);

        public static ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options, TimeSpan matchTimeout) =>
            RegexCache.GetOrAdd(pattern, options, matchTimeout).EnumerateSplits(input);

        public ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<char> input) =>
            EnumerateSplits(input, count: 0);

        public ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<char> input, int count) =>
            EnumerateSplits(input, count, startat: RightToLeft ? input.Length : 0);

        public ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<char> input, int count, int startat)
        {
            if (count < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count, ExceptionResource.CountTooSmall);
            }

            if ((uint)startat > (uint)input.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.startat, ExceptionResource.BeginIndexNotNegative);
            }

            return new ValueSplitEnumerator(this, input, count, startat, RightToLeft);
        }

        [StructLayout(LayoutKind.Auto)]
        // csharpc: the IEnumerator<Range> implementation is omitted (ref struct interfaces need C# 13);
        // foreach binds to the pattern members.
        [Shadows("System.Text.RegularExpressions.Regex+ValueSplitEnumerator")]
        public ref struct ValueSplitEnumerator
        {
            private readonly Regex _regex;
            private readonly ReadOnlySpan<char> _input;

            private int _startAt;
            private (int Index, int Length) _lastMatch;
            private Range _currentSplit;
            private int _remainingCount;

            internal ValueSplitEnumerator(Regex regex, ReadOnlySpan<char> input, int count, int startAt, bool rtl)
            {
                _regex = regex;
                _input = input;
                _startAt = startAt;
                _lastMatch = (rtl ? input.Length : 0, -1);
                _remainingCount = count != 0 ? count : int.MaxValue; // Maintain same behavior as Split(..., count: 0, ...), which treats it as effectively infinite.
            }

            public readonly ValueSplitEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                // If we've already found all the splits, we're done.
                if (_remainingCount == 0)
                {
                    return false;
                }

                if (_remainingCount == 1)
                {
                    // If we've reached the last split, include everything that remains.
                    _currentSplit = !_regex.RightToLeft ? (_lastMatch.Index + _lastMatch.Length).._input.Length : 0.._lastMatch.Index;
                }
                else
                {
                    // Perform the next match.
                    (bool Success, int Index, int Length, int TextPosition) match = _regex.RunSingleMatch(RegexRunnerMode.BoundsRequired, _lastMatch.Length, _input, _startAt);

                    // If the match was successful, update the current result to be the input between the last match and this one.
                    // Otherwise, update the current result to be the input between the last match and the end of the input.
                    if (!_regex.RightToLeft)
                    {
                        int start = _lastMatch.Index + Math.Max(_lastMatch.Length, 0);
                        if (match.Success)
                        {
                            _currentSplit = start..match.Index;
                            _lastMatch = (match.Index, match.Length);
                        }
                        else
                        {
                            _currentSplit = start.._input.Length;
                            _remainingCount = 1;
                        }
                    }
                    else
                    {
                        if (match.Success)
                        {
                            int start = _lastMatch.Index;
                            _currentSplit = (match.Index + match.Length)..start;
                            _lastMatch = (match.Index, match.Length);
                        }
                        else
                        {
                            _currentSplit = 0.._lastMatch.Index;
                            _remainingCount = 1;
                        }
                    }

                    // Update the position from which to perform the next match.
                    _startAt = match.TextPosition;
                }

                // Decrement the remaining count now that we're successfully yielding the next split.
                _remainingCount--;
                return true;
            }

            public readonly Range Current => _currentSplit;

            public void Dispose() { }
        }
    }
}
