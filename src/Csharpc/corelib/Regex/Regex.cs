// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CsharpcCoreLib.RegularExpressions
{
    [Shadows("System.Text.RegularExpressions.Regex")]
    public partial class Regex
    {
        [StringSyntax(StringSyntaxAttribute.Regex)]
        protected internal string? pattern;

        protected internal RegexOptions roptions;

        protected internal RegexRunnerFactory? factory;

        internal Hashtable? caps;

        internal Hashtable? capnames;

        protected internal string[]? capslist;

        protected internal int capsize;

        private volatile RegexRunner? _runner;                // cached runner

        protected Regex()
        {
            internalMatchTimeout = s_defaultMatchTimeout;
        }

        public Regex([StringSyntax(StringSyntaxAttribute.Regex)] string pattern) :
            this(pattern, culture: null)
        {
        }

        public Regex([StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options) :
            this(pattern, options, s_defaultMatchTimeout, culture: null)
        {
        }

        public Regex([StringSyntax(StringSyntaxAttribute.Regex, nameof(options))] string pattern, RegexOptions options, TimeSpan matchTimeout) :
            this(pattern, options, matchTimeout, culture: null)
        {
        }

        internal Regex(string pattern, CultureInfo? culture)
        {
            // Validate arguments.
            ValidatePattern(pattern);

            // Parse and store the argument information.
            RegexTree tree = Init(pattern, RegexOptions.None, s_defaultMatchTimeout, ref culture);

            // Create the interpreter factory.
            factory = new RegexInterpreterFactory(tree);

            // NOTE: This overload _does not_ delegate to the one that takes options, in order
            // to avoid unnecessarily rooting the support for RegexOptions.NonBacktracking/Compiler
            // if no options are ever used.
        }

        /// <summary>
        /// csharpc: the instance behind a [GeneratedRegex] member (Frontend/GeneratedRegex.cs emits the call).
        /// .NET's generator bakes the pattern into a Regex-derived type, parsed with the attribute's culture
        /// (invariant if none) and without validating the option combination; this builds the interpreter
        /// for the same tree. Patterns the generator can't handle (NonBacktracking) become `new Regex(...)`.
        /// </summary>
        internal static Regex CreateForGeneratedRegex(string pattern, RegexOptions options, int matchTimeoutMilliseconds, string cultureName)
        {
            TimeSpan matchTimeout = matchTimeoutMilliseconds == int.MinValue ? s_defaultMatchTimeout : TimeSpan.FromMilliseconds(matchTimeoutMilliseconds);
            if ((options & RegexOptions.NonBacktracking) != 0)
            {
                return new Regex(pattern, options, matchTimeout, culture: null);
            }

            var regex = new Regex();
            CultureInfo? culture = CultureInfo.FromName(cultureName);
            RegexTree tree = regex.Init(pattern, options, matchTimeout, ref culture);
            regex.factory = new RegexInterpreterFactory(tree);
            return regex;
        }

        [UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode",
            Justification = "Compiled Regex is only used when RuntimeFeature.IsDynamicCodeCompiled is true. Workaround https://github.com/dotnet/linker/issues/2715.")]
        internal Regex(string pattern, RegexOptions options, TimeSpan matchTimeout, CultureInfo? culture)
        {
            // Validate arguments.
            ValidatePattern(pattern);
            ValidateOptions(options);
            ValidateMatchTimeout(matchTimeout);

            // Parse and store the argument information.
            RegexTree tree = Init(pattern, options, matchTimeout, ref culture);

            // Create the appropriate factory.
            if ((options & RegexOptions.NonBacktracking) != 0)
            {
                // csharpc: the NonBacktracking (symbolic automata) engine is not ported. Its capture semantics
                // differ from the backtracking engines', so falling back to the interpreter could silently
                // change results.
                throw new NotSupportedException("RegexOptions.NonBacktracking is not supported by csharpc yet.");
            }

            // csharpc: RegexOptions.Compiled has the interpreter's semantics; the generated code is
            // an optimization .NET applies with Reflection.Emit, which a native binary doesn't have.
            factory = new RegexInterpreterFactory(tree);
        }

        private RegexTree Init(string pattern, RegexOptions options, TimeSpan matchTimeout, [NotNull] ref CultureInfo? culture)
        {
            this.pattern = pattern;
            roptions = options;
            internalMatchTimeout = matchTimeout;
            culture ??= RegexParser.GetTargetCulture(options);

            // Parse the pattern.
            RegexTree tree = RegexParser.Parse(pattern, options, culture);

            // Store the relevant information, constructing the appropriate factory.
            capnames = tree.CaptureNameToNumberMapping;
            capslist = tree.CaptureNames;
            caps = tree.CaptureNumberSparseMapping;
            capsize = tree.CaptureCount;

            return tree;
        }

        internal static void ValidatePattern(string pattern)
        {
            if (pattern is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.pattern);
            }
        }

        internal static void ValidateOptions(RegexOptions options)
        {
            const int MaxOptionShift = 12;
            if (((((uint)options) >> MaxOptionShift) != 0) ||
                ((options & RegexOptions.ECMAScript) != 0 && (options & ~(RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.CultureInvariant)) != 0) ||
                ((options & RegexOptions.NonBacktracking) != 0 && (options & (RegexOptions.ECMAScript | RegexOptions.RightToLeft | RegexOptions.AnyNewLine)) != 0))
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.options);
            }
        }

        protected internal static void ValidateMatchTimeout(TimeSpan matchTimeout)
        {
            long matchTimeoutTicks = matchTimeout.Ticks;
            if (matchTimeoutTicks != InfiniteMatchTimeoutTicks && ((ulong)(matchTimeoutTicks - 1) >= MaximumMatchTimeoutTicks))
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.matchTimeout);
            }
        }

        public static string Escape(string str)
        {
            if (str is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.str);
            }

            return RegexParser.Escape(str);
        }

        public static string Unescape(string str)
        {
            if (str is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.str);
            }

            return RegexParser.Unescape(str);
        }

        public RegexOptions Options => roptions;

        public bool RightToLeft => (roptions & RegexOptions.RightToLeft) != 0;

        public override string ToString() => pattern!;

        public string[] GetGroupNames()
        {
            string[] result;

            if (capslist is null)
            {
                result = new string[capsize];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = ((uint)i).ToString();
                }
            }
            else
            {
                result = capslist.AsSpan().ToArray();
            }

            return result;
        }

        public int[] GetGroupNumbers()
        {
            int[] result;

            if (caps is null)
            {
                result = new int[capsize];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = i;
                }
            }
            else
            {
                // Manual use of IDictionaryEnumerator instead of foreach to avoid DictionaryEntry box allocations.
                result = new int[caps.Count];
                Hashtable.Enumerator de = caps.GetEnumerator(); // csharpc: see Hashtable
                while (de.MoveNext())
                {
                    result[(int)de.Value!] = (int)de.Key;
                }
                Array.Sort(result);
            }

            return result;
        }

        public string GroupNameFromNumber(int i)
        {
            return RegexParser.GroupNameFromNumber(caps, capslist, capsize, i);
        }

        public int GroupNumberFromName(string name)
        {
            if (name is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.name);
            }

            if (capnames != null)
            {
                // Look up name if we have a hashtable of names.
                return capnames.TryGetValue(name, out int result) ? result : -1;
            }
            else
            {
                // Otherwise, try to parse it as a number.
                return uint.TryParse(name, NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out uint result) && result < capsize ? (int)result : -1;
            }
        }

        private WeakReference<RegexReplacement?>? _replacementWeakReference;

        internal WeakReference<RegexReplacement?> RegexReplacementWeakReference =>
            _replacementWeakReference ??
            Interlocked.CompareExchange(ref _replacementWeakReference, new WeakReference<RegexReplacement?>(null), null) ??
            _replacementWeakReference;

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected void InitializeReferences()
        {
            // This method no longer has anything to initialize. It continues to exist
            // purely for API compat, as it was originally shipped as protected, with
            // assemblies generated by Regex.CompileToAssembly calling it.
        }

        internal Match? RunSingleMatch(RegexRunnerMode mode, int prevlen, string input, int beginning, int length, int startat)
        {
            if ((uint)startat > (uint)input.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.startat, ExceptionResource.BeginIndexNotNegative);
            }
            if ((uint)length > (uint)input.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length, ExceptionResource.LengthNotNegative);
            }

            RegexRunner runner = Interlocked.Exchange(ref _runner, null) ?? CreateRunner();
            try
            {
                runner.InitializeTimeout(internalMatchTimeout);
                runner.runtext = input;
                ReadOnlySpan<char> span = input.AsSpan(beginning, length);
                runner.InitializeForScan(this, span, startat - beginning, mode);

                // If previous match was empty or failed, advance by one before matching.
                if (prevlen == 0)
                {
                    int stoppos = span.Length;
                    int bump = 1;
                    if (RightToLeft)
                    {
                        stoppos = 0;
                        bump = -1;
                    }

                    if (runner.runtextstart == stoppos)
                    {
                        return RegularExpressions.Match.Empty;
                    }

                    runner.runtextpos += bump;
                }

                return ScanInternal(mode, reuseMatchObject: mode == RegexRunnerMode.ExistenceRequired, input, beginning, runner, span, returnNullIfReuseMatchObject: true);
            }
            finally
            {
                runner.runtext = null; // drop reference to text to avoid keeping it alive in a cache.
                _runner = runner;
            }
        }

        internal (bool Success, int Index, int Length, int TextPosition) RunSingleMatch(RegexRunnerMode mode, int prevlen, ReadOnlySpan<char> input, int startat)
        {
            Debug.Assert(mode <= RegexRunnerMode.BoundsRequired);

            // startat parameter is always either 0 or input.Length since public API for IsMatch doesn't have an overload
            // that takes in startat.
            Debug.Assert(startat <= input.Length);

            RegexRunner runner = Interlocked.Exchange(ref _runner, null) ?? CreateRunner();
            try
            {
                runner.InitializeTimeout(internalMatchTimeout);
                runner.InitializeForScan(this, input, startat, mode);

                // If previous match was empty or failed, advance by one before matching.
                if (prevlen == 0)
                {
                    if (RightToLeft)
                    {
                        if (runner.runtextstart == 0)
                        {
                            return (false, -1, -1, -1);
                        }
                        runner.runtextpos--;
                    }
                    else
                    {
                        if (runner.runtextstart == input.Length)
                        {
                            return (false, -1, -1, -1);
                        }
                        runner.runtextpos++;
                    }
                }

                runner.Scan(input);

                // If runmatch is null it means that an override of Scan didn't implement it correctly, so we will
                // let this null ref since there are lots of ways where you can end up in a erroneous state.
                Match match = runner.runmatch!;
                if (match.FoundMatch)
                {
                    if (mode == RegexRunnerMode.ExistenceRequired)
                    {
                        return (true, -1, -1, -1);
                    }

                    match.Tidy(runner.runtextpos, 0, mode);
                    return (true, match.Index, match.Length, match._textpos);
                }

                return (false, -1, -1, -1);
            }
            finally
            {
                _runner = runner;
            }
        }

        internal void RunAllMatchesWithCallback<TState>(string? input, int startat, ref TState state, MatchCallback<TState> callback, RegexRunnerMode mode, bool reuseMatchObject) =>
            RunAllMatchesWithCallback(input, (ReadOnlySpan<char>)input, startat, ref state, callback, mode, reuseMatchObject);

        internal void RunAllMatchesWithCallback<TState>(ReadOnlySpan<char> input, int startat, ref TState state, MatchCallback<TState> callback, RegexRunnerMode mode, bool reuseMatchObject) =>
            RunAllMatchesWithCallback(inputString: null, input, startat, ref state, callback, mode, reuseMatchObject);

        private void RunAllMatchesWithCallback<TState>(string? inputString, ReadOnlySpan<char> inputSpan, int startat, ref TState state, MatchCallback<TState> callback, RegexRunnerMode mode, bool reuseMatchObject)
        {
            Debug.Assert(inputString is null || inputSpan.SequenceEqual(inputString));
            Debug.Assert((uint)startat <= (uint)inputSpan.Length);

            RegexRunner runner = Interlocked.Exchange(ref _runner, null) ?? CreateRunner();
            try
            {
                runner.runtext = inputString;
                runner.InitializeTimeout(internalMatchTimeout);
                int runtextpos = startat;

                while (true)
                {
                    runner.InitializeForScan(this, inputSpan, startat, mode);
                    runner.runtextpos = runtextpos;

                    // We get the Match by calling Scan. 'input' parameter is used to set the Match text which is only relevant if we are using the Run<TState> string
                    // overload, as APIs that call the span overload (like Count) don't require match.Text to be set, so we pass null in that case.
                    Match? match = ScanInternal(mode, reuseMatchObject, inputString, 0, runner, inputSpan, returnNullIfReuseMatchObject: false);
                    Debug.Assert(match is not null);

                    // If we failed to match again, we're done.
                    if (!match.Success)
                    {
                        break;
                    }

                    // We got a match.  Call the callback function with the match and prepare for next iteration.

                    if (!reuseMatchObject)
                    {
                        // We're not reusing match objects, so null out our field reference to the instance.
                        // It'll be recreated the next time one is needed.  reuseMatchObject will be false
                        // when the callback may expose the Match object to user code.
                        runner.runmatch = null;
                    }

                    if (!callback(ref state, match))
                    {
                        // If the callback returns false, we're done.
                        return;
                    }

                    // Now that we've matched successfully, update the starting position to reflect
                    // the current position, just as Match.NextMatch() would pass in _textpos as textstart.
                    runtextpos = startat = runner.runtextpos;

                    if (match.Length == 0)
                    {
                        int stoppos = inputSpan.Length;
                        int bump = 1;
                        if (RightToLeft)
                        {
                            stoppos = 0;
                            bump = -1;
                        }

                        if (runtextpos == stoppos)
                        {
                            return;
                        }

                        runtextpos += bump;
                    }

                    // Reset state for another iteration.
                    runner.runtrackpos = runner.runtrack!.Length;
                    runner.runstackpos = runner.runstack!.Length;
                    runner.runcrawlpos = runner.runcrawl!.Length;
                }
            }
            finally
            {
                runner.runtext = null; // drop reference to string to avoid keeping it alive in a cache.
                _runner = runner;
            }
        }

        private static Match? ScanInternal(RegexRunnerMode mode, bool reuseMatchObject, string? input, int beginning, RegexRunner runner, ReadOnlySpan<char> span, bool returnNullIfReuseMatchObject)
        {
            runner.Scan(span);

            Match? match = runner.runmatch;
            Debug.Assert(match is not null);

            // If we got a match, do some cleanup and return it, or return null if reuseMatchObject and returnNullIfReuseMatchObject are true.
            if (match.FoundMatch)
            {
                if (!reuseMatchObject)
                {
                    // The match object is only reusable in very specific circumstances where the internal caller
                    // extracts only the matching information (e.g. bounds) it needs from the Match object, so
                    // in such situations we don't need to fill in the input value, and because it's being reused,
                    // we don't want to null it out in the runner.  If, however, the match object isn't going to
                    // be reused, then we do need to finish populating it with the input text, and we do want to
                    // remove it from the runner so that no one else touches the object once we give it back.
                    match.Text = input;
                    runner.runmatch = null;
                }
                else if (returnNullIfReuseMatchObject)
                {
                    match.Text = null;
                    return null;
                }

                match.Tidy(runner.runtextpos, beginning, mode);

                return match;
            }

            // We failed to match, so we will return Match.Empty which means we can reuse runmatch object.
            // We do however need to clear its Text in case it was set, so as to not keep it alive in some cache.
            match.Text = null;

            return RegularExpressions.Match.Empty;
        }

        private RegexRunner CreateRunner() =>
            // The factory needs to be set by the ctor.  `factory` is a protected field, so it's possible a derived
            // type nulls out the factory after we've set it, but that's the nature of the design.
            factory!.CreateInstance();

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool UseOptionC() => (roptions & RegexOptions.Compiled) != 0;

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool UseOptionR() => RightToLeft;
    }
}
