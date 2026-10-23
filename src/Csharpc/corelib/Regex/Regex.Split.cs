// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CsharpcCoreLib.RegularExpressions
{
    public partial class Regex
    {
        public static string[] Split(string input, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern) =>
            RegexCache.GetOrAdd(pattern).Split(input);

        public static string[] Split(string input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options) =>
            RegexCache.GetOrAdd(pattern, options, s_defaultMatchTimeout).Split(input);

        public static string[] Split(string input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options, TimeSpan matchTimeout) =>
            RegexCache.GetOrAdd(pattern, options, matchTimeout).Split(input);

        public string[] Split(string input)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }

            return Split(this, input, 0, RightToLeft ? input.Length : 0);
        }

        public string[] Split(string input, int count)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }

            return Split(this, input, count, RightToLeft ? input.Length : 0);
        }

        public string[] Split(string input, int count, int startat)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }

            return Split(this, input, count, startat);
        }

        private static string[] Split(Regex regex, string input, int count, int startat)
        {
            if (count < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count, ExceptionResource.CountTooSmall);
            }
            if ((uint)startat > (uint)input.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.startat, ExceptionResource.BeginIndexNotNegative);
            }

            if (count == 1)
            {
                return new string[] { input };
            }

            count--;
            var state = (results: new List<string>(), prevat: 0, input, count);

            if (!regex.RightToLeft)
            {
                regex.RunAllMatchesWithCallback(input, startat, ref state, static (ref (List<string> results, int prevat, string input, int count) state, Match match) =>
                {
                    state.results.Add(state.input.Substring(state.prevat, match.Index - state.prevat));
                    state.prevat = match.Index + match.Length;

                    // add all matched capture groups to the list.
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        if (match.IsMatched(i))
                        {
                            state.results.Add(match.Groups[i].Value);
                        }
                    }

                    return --state.count != 0;
                }, RegexRunnerMode.FullMatchRequired, reuseMatchObject: true);

                if (state.results.Count == 0)
                {
                    return new string[] { input };
                }

                state.results.Add(input.Substring(state.prevat));
            }
            else
            {
                state.prevat = input.Length;

                regex.RunAllMatchesWithCallback(input, startat, ref state, static (ref (List<string> results, int prevat, string input, int count) state, Match match) =>
                {
                    state.results.Add(state.input.Substring(match.Index + match.Length, state.prevat - match.Index - match.Length));
                    state.prevat = match.Index;

                    // add all matched capture groups to the list.
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        if (match.IsMatched(i))
                        {
                            state.results.Add(match.Groups[i].Value);
                        }
                    }

                    return --state.count != 0;
                }, RegexRunnerMode.FullMatchRequired, reuseMatchObject: true);

                if (state.results.Count == 0)
                {
                    return new string[] { input };
                }

                state.results.Add(input.Substring(0, state.prevat));
                state.results.Reverse(0, state.results.Count);
            }

            return state.results.ToArray();
        }
    }
}
