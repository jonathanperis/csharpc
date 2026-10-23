// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib.RegularExpressions
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract class RegexRunner
    {
        protected internal int runtextbeg;

        protected internal int runtextend;

        protected internal int runtextstart;

        protected internal string? runtext;

        protected internal int runtextpos;

        protected internal int[]? runtrack;
        protected internal int runtrackpos;

        protected internal int[]? runstack;
        protected internal int runstackpos;

        protected internal int[]? runcrawl;
        protected internal int runcrawlpos;

        protected internal int runtrackcount;

        protected internal Match? runmatch;
        protected internal Regex? runregex;

        private protected RegexRunnerMode _mode;

        private int _timeout;
        private bool _checkTimeout;
        private long _timeoutOccursAt;

        protected RegexRunner() { }

        protected internal virtual void Scan(ReadOnlySpan<char> text)
        {
            // csharpc: every runner is the interpreter, which overrides Scan; .NET's fallback here serves
            // Regex-derived types compiled by .NET Framework's Regex.CompileToAssembly.
            throw new NotSupportedException();
        }

        protected Match? Scan(Regex regex, string text, int textbeg, int textend, int textstart, int prevlen, bool quick) =>
            Scan(regex, text, textbeg, textend, textstart, prevlen, quick, regex.MatchTimeout);

        protected internal Match? Scan(Regex regex, string text, int textbeg, int textend, int textstart, int prevlen, bool quick, TimeSpan timeout)
        {
            InitializeTimeout(timeout);

            RegexRunnerMode mode = quick ? RegexRunnerMode.ExistenceRequired : RegexRunnerMode.FullMatchRequired;

            // We set runtext before calling InitializeForScan so that runmatch object is initialized with the text
            runtext = text;

            InitializeForScan(regex, text, textstart, mode);

            // InitializeForScan will default runtextstart and runtextend to 0 and length of string
            // since it is configured to work over a sliced portion of text so we adjust those values.
            runtextstart = textstart;
            runtextend = textend;

            // Configure the additional value to "bump" the position along each time we loop around
            // to call FindFirstChar again, as well as the stopping position for the loop.  We generally
            // bump by 1 and stop at textend, but if we're examining right-to-left, we instead bump
            // by -1 and stop at textbeg.
            int bump = 1, stoppos = textend;
            if (regex.RightToLeft)
            {
                bump = -1;
                stoppos = textbeg;
            }

            // If previous match was empty or failed, advance by one before matching.
            if (prevlen == 0)
            {
                if (textstart == stoppos)
                {
                    return Match.Empty;
                }

                runtextpos += bump;
            }

            Match match = InternalScan(regex, textbeg, textend);
            runtext = null; //drop reference

            if (match.FoundMatch)
            {
                if (quick)
                {
                    return null;
                }

                runmatch = null;
                match.Tidy(runtextpos, 0, mode);
            }
            else
            {
                runmatch!.Text = null;
            }

            return match;

        }

        private Match InternalScan(Regex regex, int textbeg, int textend)
        {
            // Configure the additional value to "bump" the position along each time we loop around
            // to call FindFirstChar again, as well as the stopping position for the loop.  We generally
            // bump by 1 and stop at textend, but if we're examining right-to-left, we instead bump
            // by -1 and stop at textbeg.
            int bump = 1, stoppos = textend;
            if (regex.RightToLeft)
            {
                bump = -1;
                stoppos = textbeg;
            }

            while (true)
            {
                // Find the next potential location for a match in the input.
                if (FindFirstChar())
                {
                    CheckTimeout();

                    // See if there's a match at this position.
                    Go();

                    if (runmatch!.FoundMatch)
                    {
                        return runmatch;
                    }

                    // Reset state for another iteration.
                    runtrackpos = runtrack!.Length;
                    runstackpos = runstack!.Length;
                    runcrawlpos = runcrawl!.Length;
                }

                // We failed to match at this position.  If we're at the stopping point, we're done.
                if (runtextpos == stoppos)
                {
                    return Match.Empty;
                }

                // Bump by one (in whichever direction is appropriate) and loop to go again.
                runtextpos += bump;
            }
        }

        internal void InitializeForScan(Regex regex, ReadOnlySpan<char> text, int textstart, RegexRunnerMode mode)
        {
            // Store remaining arguments into fields now that we're going to start the scan.
            // These are referenced by the derived runner.
            _mode = mode;
            runregex = regex;
            runtextstart = textstart;
            runtextbeg = 0;
            runtextend = text.Length;
            runtextpos = textstart;

            Match? m = runmatch;
            if (m is null)
            {
                // Use a hashtabled Match object if the capture numbers are sparse
                runmatch = runregex!.caps is null ?
                    new Match(runregex, runregex.capsize, runtext, text.Length) :
                    new MatchSparse(runregex, runregex.caps, runregex.capsize, runtext, text.Length);
            }
            else
            {
                m.Reset(runtext, text.Length);
            }

            // Note we test runcrawl, because it is the last one to be allocated
            // If there is an alloc failure in the middle of the three allocations,
            // we may still return to reuse this instance, and we want to behave
            // as if the allocations didn't occur.
            if (runcrawl != null)
            {
                runtrackpos = runtrack!.Length;
                runstackpos = runstack!.Length;
                runcrawlpos = runcrawl.Length;
                return;
            }

            // Everything above runs once per match.
            // Everything below runs once per runner.

            InitTrackCount();

            int stacksize;
            int tracksize = stacksize = runtrackcount * 8;

            if (tracksize < 32)
            {
                tracksize = 32;
            }
            if (stacksize < 16)
            {
                stacksize = 16;
            }

            runtrack = new int[tracksize];
            runtrackpos = tracksize;

            runstack = new int[stacksize];
            runstackpos = stacksize;

            runcrawl = new int[32];
            runcrawlpos = 32;
        }

        internal void InitializeTimeout(TimeSpan timeout)
        {
            // Handle timeout argument
            _checkTimeout = false;
            if (Regex.InfiniteMatchTimeout != timeout)
            {
                ConfigureTimeout(timeout);

                void ConfigureTimeout(TimeSpan timeout)
                {
                    _checkTimeout = true;
                    _timeout = (int)(timeout.TotalMilliseconds + 0.5); // Round;
                    _timeoutOccursAt = Environment.TickCount64 + _timeout;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected internal void CheckTimeout()
        {
            if (_checkTimeout && Environment.TickCount64 >= _timeoutOccursAt)
            {
                ThrowRegexTimeout();
            }

            void ThrowRegexTimeout() => throw new RegexMatchTimeoutException(runtext ?? string.Empty, runregex!.pattern!, TimeSpan.FromMilliseconds(_timeout));
        }

        protected virtual void Go() => throw new NotImplementedException();

        protected virtual bool FindFirstChar() => throw new NotImplementedException();

        protected virtual void InitTrackCount() { }

        protected void EnsureStorage()
        {
            int limit = runtrackcount * 4;

            if (runstackpos < limit)
                DoubleStack();

            if (runtrackpos < limit)
                DoubleTrack();
        }

        protected bool IsBoundary(int index, int startpos, int endpos)
        {
            return (index > startpos && RegexCharClass.IsBoundaryWordChar(runtext![index - 1])) !=
                   (index < endpos && RegexCharClass.IsBoundaryWordChar(runtext![index]));
        }

        internal static bool IsBoundary(ReadOnlySpan<char> inputSpan, int index)
        {
            int indexM1 = index - 1;
            return ((uint)indexM1 < (uint)inputSpan.Length && RegexCharClass.IsBoundaryWordChar(inputSpan[indexM1])) !=
                   ((uint)index < (uint)inputSpan.Length && RegexCharClass.IsBoundaryWordChar(inputSpan[index]));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsPreWordCharBoundary(ReadOnlySpan<char> inputSpan, int index)
        {
            int indexMinus1 = index - 1;
            return (uint)indexMinus1 >= (uint)inputSpan.Length || !RegexCharClass.IsBoundaryWordChar(inputSpan[indexMinus1]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsPostWordCharBoundary(ReadOnlySpan<char> inputSpan, int index) =>
            (uint)index >= (uint)inputSpan.Length || !RegexCharClass.IsBoundaryWordChar(inputSpan[index]);

        internal static bool IsWordChar(char ch) => RegexCharClass.IsWordChar(ch);

        protected bool IsECMABoundary(int index, int startpos, int endpos)
        {
            return (index > startpos && RegexCharClass.IsECMAWordChar(runtext![index - 1])) !=
                   (index < endpos && RegexCharClass.IsECMAWordChar(runtext![index]));
        }

        internal static bool IsECMABoundary(ReadOnlySpan<char> inputSpan, int index)
        {
            int indexM1 = index - 1;
            return ((uint)indexM1 < (uint)inputSpan.Length && RegexCharClass.IsECMAWordChar(inputSpan[indexM1])) !=
                   ((uint)index < (uint)inputSpan.Length && RegexCharClass.IsECMAWordChar(inputSpan[index]));
        }

        protected static bool CharInSet(char ch, string set, string category)
        {
            string charClass = RegexCharClass.ConvertOldStringsToClass(set, category);
            return RegexCharClass.CharInClass(ch, charClass);
        }

        public static bool CharInClass(char ch, string charClass)
        {
            return RegexCharClass.CharInClass(ch, charClass);
        }

        protected void DoubleTrack()
        {
            int[] newtrack = new int[runtrack!.Length * 2];

            Array.Copy(runtrack, 0, newtrack, runtrack.Length, runtrack.Length);
            runtrackpos += runtrack.Length;
            runtrack = newtrack;
        }

        protected void DoubleStack()
        {
            int[] newstack = new int[runstack!.Length * 2];

            Array.Copy(runstack, 0, newstack, runstack.Length, runstack.Length);
            runstackpos += runstack.Length;
            runstack = newstack;
        }

        protected void DoubleCrawl()
        {
            int[] newcrawl = new int[runcrawl!.Length * 2];

            Array.Copy(runcrawl, 0, newcrawl, runcrawl.Length, runcrawl.Length);
            runcrawlpos += runcrawl.Length;
            runcrawl = newcrawl;
        }

        protected void Crawl(int i)
        {
            if (runcrawlpos == 0)
                DoubleCrawl();

            runcrawl![--runcrawlpos] = i;
        }

        protected int Popcrawl()
        {
            return runcrawl![runcrawlpos++];
        }

        protected int Crawlpos()
        {
            return runcrawl!.Length - runcrawlpos;
        }

        protected void Capture(int capnum, int start, int end)
        {
            if (end < start)
            {
                int t = end;
                end = start;
                start = t;
            }

            Crawl(capnum);
            runmatch!.AddMatch(capnum, start, end - start);
        }

        protected void TransferCapture(int capnum, int uncapnum, int start, int end)
        {
            // these are the two intervals that are canceling each other

            if (end < start)
            {
                int t = end;
                end = start;
                start = t;
            }

            int start2 = MatchIndex(uncapnum);
            int end2 = start2 + MatchLength(uncapnum);

            // The new capture gets the innermost defined interval

            if (start >= end2)
            {
                end = start;
                start = end2;
            }
            else if (end <= start2)
            {
                start = start2;

                // Ensure we don't create a capture with negative length
                // When the balancing capture precedes the balanced group, end might be less than the new start
                if (end < start)
                {
                    end = start;
                }
            }
            else
            {
                if (end > end2)
                    end = end2;
                if (start2 > start)
                    start = start2;
            }

            Crawl(uncapnum);
            runmatch!.BalanceMatch(uncapnum);

            if (capnum != -1)
            {
                Crawl(capnum);
                runmatch.AddMatch(capnum, start, end - start);
            }
        }

        protected void Uncapture()
        {
            int capnum = Popcrawl();
            runmatch!.RemoveMatch(capnum);
        }

        protected bool IsMatched(int cap)
        {
            return runmatch!.IsMatched(cap);
        }

        protected int MatchIndex(int cap)
        {
            return runmatch!.MatchIndex(cap);
        }

        protected int MatchLength(int cap)
        {
            return runmatch!.MatchLength(cap);
        }
    }
}
