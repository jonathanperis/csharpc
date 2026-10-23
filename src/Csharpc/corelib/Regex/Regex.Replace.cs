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
    // csharpc: MatchEvaluator is the real System.Text.RegularExpressions delegate (user lambdas bind to it);
    // it takes the public Match type, which the shadow Match becomes at run time (Match.AsPublic).

    internal delegate bool MatchCallback<TState>(ref TState state, Match match);

    public partial class Regex
    {
        public static string Replace(string input, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern, string replacement) =>
            RegexCache.GetOrAdd(pattern).Replace(input, replacement);

        public static string Replace(string input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, string replacement, RegexOptions options) =>
            RegexCache.GetOrAdd(pattern, options, s_defaultMatchTimeout).Replace(input, replacement);

        public static string Replace(string input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, string replacement, RegexOptions options, TimeSpan matchTimeout) =>
            RegexCache.GetOrAdd(pattern, options, matchTimeout).Replace(input, replacement);

        public string Replace(string input, string replacement)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }

            return Replace(input, replacement, -1, RightToLeft ? input.Length : 0);
        }

        public string Replace(string input, string replacement, int count)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }

            return Replace(input, replacement, count, RightToLeft ? input.Length : 0);
        }

        public string Replace(string input, string replacement, int count, int startat)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }
            if (replacement is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.replacement);
            }

            // Gets the weakly cached replacement helper or creates one if there isn't one already,
            // then uses it to perform the replace.
            return
                RegexReplacement.GetOrCreate(RegexReplacementWeakReference, replacement, caps!, capsize, capnames!, roptions).
                Replace(this, input, count, startat);
        }

        public static string Replace(string input, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern, MatchEvaluator evaluator) =>
            RegexCache.GetOrAdd(pattern).Replace(input, evaluator);

        public static string Replace(string input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, MatchEvaluator evaluator, RegexOptions options) =>
            RegexCache.GetOrAdd(pattern, options, s_defaultMatchTimeout).Replace(input, evaluator);

        public static string Replace(string input, [StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, MatchEvaluator evaluator, RegexOptions options, TimeSpan matchTimeout) =>
            RegexCache.GetOrAdd(pattern, options, matchTimeout).Replace(input, evaluator);

        public string Replace(string input, MatchEvaluator evaluator)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }

            return Replace(evaluator, this, input, -1, RightToLeft ? input.Length : 0);
        }

        public string Replace(string input, MatchEvaluator evaluator, int count)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }

            return Replace(evaluator, this, input, count, RightToLeft ? input.Length : 0);
        }

        public string Replace(string input, MatchEvaluator evaluator, int count, int startat)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }

            return Replace(evaluator, this, input, count, startat);
        }

        private static string Replace(MatchEvaluator evaluator, Regex regex, string input, int count, int startat)
        {
            if (evaluator is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.evaluator);
            }
            if (count < -1)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count, ExceptionResource.CountTooSmall);
            }
            if ((uint)startat > (uint)input.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.startat, ExceptionResource.BeginIndexNotNegative);
            }

            if (count == 0)
            {
                return input;
            }

            var state = (segments: new StructListBuilder<ReadOnlyMemory<char>>(), evaluator, prevat: 0, input, count);

            if (!regex.RightToLeft)
            {
                regex.RunAllMatchesWithCallback(input, startat, ref state, static (ref (StructListBuilder<ReadOnlyMemory<char>> segments, MatchEvaluator evaluator, int prevat, string input, int count) state, Match match) =>
                {
                    state.segments.Add(state.input.AsMemory(state.prevat, match.Index - state.prevat));
                    state.prevat = match.Index + match.Length;
                    state.segments.Add(state.evaluator(match.AsPublic()).AsMemory());
                    return --state.count != 0;
                }, RegexRunnerMode.FullMatchRequired, reuseMatchObject: false);

                if (state.segments.Count == 0)
                {
                    return input;
                }

                state.segments.Add(input.AsMemory(state.prevat, input.Length - state.prevat));
            }
            else
            {
                state.prevat = input.Length;

                regex.RunAllMatchesWithCallback(input, startat, ref state, static (ref (StructListBuilder<ReadOnlyMemory<char>> segments, MatchEvaluator evaluator, int prevat, string input, int count) state, Match match) =>
                {
                    state.segments.Add(state.input.AsMemory(match.Index + match.Length, state.prevat - match.Index - match.Length));
                    state.prevat = match.Index;
                    state.segments.Add(state.evaluator(match.AsPublic()).AsMemory());
                    return --state.count != 0;
                }, RegexRunnerMode.FullMatchRequired, reuseMatchObject: false);

                if (state.segments.Count == 0)
                {
                    return input;
                }

                state.segments.Add(input.AsMemory(0, state.prevat));
                state.segments.AsSpan().Reverse();
            }

            return SegmentsToStringAndDispose(ref state.segments);
        }

        internal static string SegmentsToStringAndDispose(ref StructListBuilder<ReadOnlyMemory<char>> segments)
        {
            Span<ReadOnlyMemory<char>> span = segments.AsSpan();

            int length = 0;
            for (int i = 0; i < span.Length; i++)
            {
                length += span[i].Length;
            }

            // csharpc: compose into a char[] (string.Create with a span state needs C# 13).
            char[] dest = new char[length];
            int destPos = 0;
            for (int i = 0; i < span.Length; i++)
            {
                span[i].CopyTo(dest, destPos);
                destPos += span[i].Length;
            }
            string result = new string(dest);

            segments.Dispose();

            return result;
        }
    }
}
