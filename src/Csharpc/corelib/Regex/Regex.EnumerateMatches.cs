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

namespace CsharpcCoreLib.RegularExpressions
{
    public partial class Regex
    {
        public static ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern) =>
            RegexCache.GetOrAdd(pattern).EnumerateMatches(input);

        public static ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options) =>
            RegexCache.GetOrAdd(pattern, options, s_defaultMatchTimeout).EnumerateMatches(input);

        public static ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options, TimeSpan matchTimeout) =>
            RegexCache.GetOrAdd(pattern, options, matchTimeout).EnumerateMatches(input);

        public ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input) =>
            new ValueMatchEnumerator(this, input, RightToLeft ? input.Length : 0);

        public ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input, int startat) =>
            new ValueMatchEnumerator(this, input, startat);

        // csharpc: the IEnumerator<ValueMatch> implementation is omitted (ref struct interfaces need C# 13);
        // foreach binds to the pattern members.
        [Shadows("System.Text.RegularExpressions.Regex+ValueMatchEnumerator")]
        public ref struct ValueMatchEnumerator
        {
            private readonly Regex _regex;
            private readonly ReadOnlySpan<char> _input;
            private ValueMatch _current;
            private int _startAt;
            private int _prevLen;

            internal ValueMatchEnumerator(Regex regex, ReadOnlySpan<char> input, int startAt)
            {
                _regex = regex;
                _input = input;
                _current = default;
                _startAt = startAt;
                _prevLen = -1;
            }

            public readonly ValueMatchEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                (bool Success, int Index, int Length, int TextPosition) match = _regex.RunSingleMatch(RegexRunnerMode.BoundsRequired, _prevLen, _input, _startAt);
                if (match.Success)
                {
                    _current = new ValueMatch(match.Index, match.Length);
                    _startAt = match.TextPosition;
                    _prevLen = match.Length;
                    return true;
                }

                return false;
            }

            public readonly ValueMatch Current => _current;

            public void Dispose() { }
        }
    }
}
