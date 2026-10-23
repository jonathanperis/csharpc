// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Diagnostics.CodeAnalysis;

namespace CsharpcCoreLib.RegularExpressions
{
    public partial class Regex
    {
        public int Count(string input)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }

            int count = 0;

            RunAllMatchesWithCallback(input, RightToLeft ? input.Length : 0, ref count, static (ref int count, Match match) =>
            {
                count++;
                return true;
            }, RegexRunnerMode.BoundsRequired, reuseMatchObject: true);

            return count;
        }

        public int Count(ReadOnlySpan<char> input) =>
            Count(input, RightToLeft ? input.Length : 0);

        public int Count(ReadOnlySpan<char> input, int startat)
        {
            int count = 0;

            RunAllMatchesWithCallback(input, startat, ref count, static (ref int count, Match match) =>
            {
                count++;
                return true;
            }, RegexRunnerMode.BoundsRequired, reuseMatchObject: true);

            return count;
        }

        public static int Count(string input, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern) =>
            RegexCache.GetOrAdd(pattern).Count(input);

        public static int Count(string input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options) =>
            RegexCache.GetOrAdd(pattern, options, s_defaultMatchTimeout).Count(input);

        public static int Count(string input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options, TimeSpan matchTimeout) =>
            RegexCache.GetOrAdd(pattern, options, matchTimeout).Count(input);

        public static int Count(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern) =>
            RegexCache.GetOrAdd(pattern).Count(input);

        public static int Count(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options) =>
            RegexCache.GetOrAdd(pattern, options, s_defaultMatchTimeout).Count(input);

        public static int Count(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options, TimeSpan matchTimeout) =>
            RegexCache.GetOrAdd(pattern, options, matchTimeout).Count(input);
    }
}
