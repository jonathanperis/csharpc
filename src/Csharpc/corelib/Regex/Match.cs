// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Collections;
using System.Diagnostics;

namespace CsharpcCoreLib.RegularExpressions
{
    [Shadows("System.Text.RegularExpressions.Match")]
    public class Match : Group
    {
        /// <summary>csharpc: this match as the public type (what MatchEvaluator and IEnumerable&lt;Match&gt; carry).</summary>
        internal System.Text.RegularExpressions.Match AsPublic() => (System.Text.RegularExpressions.Match)(object)this;

        internal GroupCollection? _groupcoll;

        // input to the match
        internal readonly Regex? _regex;
        internal int _textbeg; // 0 while the match is being generated, then bumped in Tidy to the actual beginning
        internal int _textpos; // position offset from beginning
        internal int _textend; // input length while the match is being generated, them bumped in Tidy to be the end

        // output from the match
        internal readonly int[][] _matches;
        internal readonly int[] _matchcount;
        internal bool _balancing;        // whether we've done any balancing with this match.  If we
                                         // have done balancing, we'll need to do extra work in Tidy().

        internal Match(Regex? regex, int capcount, string? text, int textLength) :
            base(text, new int[2], 0, "0")
        {
            _regex = regex;
            _matchcount = new int[capcount];
            _matches = new int[capcount][];
            _matches[0] = _caps;
            _textend = textLength;
            _balancing = false;
        }

        private static Match? s_empty;
        public static Match Empty => s_empty ??= new Match(null, 1, string.Empty, 0);

        internal void Reset(string? text, int textLength)
        {
            Text = text;
            _textbeg = 0;
            _textend = textLength;

            int[] matchcount = _matchcount;
            for (int i = 0; i < matchcount.Length; i++)
            {
                matchcount[i] = 0;
            }

            _balancing = false;
            _groupcoll?.Reset();
        }

        internal bool FoundMatch => _matchcount[0] > 0;

        public virtual GroupCollection Groups => _groupcoll ??= new GroupCollection(this, null);

        public Match NextMatch()
        {
            Regex? r = _regex;
            Debug.Assert(Text != null);
            return r != null ?
                r.RunSingleMatch(RegexRunnerMode.FullMatchRequired, Length, Text, _textbeg, _textend - _textbeg, _textpos)! :
                this;
        }

        public virtual string Result(string replacement)
        {
            if (replacement is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.replacement);
            }

            Regex? regex = _regex ?? throw new NotSupportedException(SR.NoResultOnFailed);

            // Gets the weakly cached replacement helper or creates one if there isn't one already.
            RegexReplacement repl = RegexReplacement.GetOrCreate(regex.RegexReplacementWeakReference, replacement, regex.caps!, regex.capsize, regex.capnames!, regex.roptions);
            var segments = new StructListBuilder<ReadOnlyMemory<char>>();
            repl.ReplacementImpl(ref segments, this);
            return Regex.SegmentsToStringAndDispose(ref segments);
        }

        internal ReadOnlyMemory<char> GroupToStringImpl(int groupnum)
        {
            int c = _matchcount[groupnum];
            if (c == 0)
            {
                return default;
            }

            int[] matches = _matches[groupnum];
            return Text.AsMemory(matches[(c - 1) * 2], matches[(c * 2) - 1]);
        }

        internal ReadOnlyMemory<char> LastGroupToStringImpl() =>
            GroupToStringImpl(_matchcount.Length - 1);

        public static Match Synchronized(Match inner)
        {
            if (inner is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.inner);
            }

            int numgroups = inner._matchcount.Length;

            // Populate all groups by looking at each one
            for (int i = 0; i < numgroups; i++)
            {
                // Depends on the fact that Group.Synchronized just
                // operates on and returns the same instance
                Synchronized(inner.Groups[i]);
            }

            return inner;
        }

        internal void AddMatch(int cap, int start, int len)
        {
            _matches[cap] ??= new int[2];
            int[][] matches = _matches;

            int[] matchcount = _matchcount;
            int capcount = matchcount[cap];

            if (capcount * 2 + 2 > matches[cap].Length)
            {
                int[] oldmatches = matches[cap];
                int[] newmatches = new int[capcount * 8];
                Array.Copy(oldmatches, newmatches, capcount * 2);

                matches[cap] = newmatches;
            }

            matches[cap][capcount * 2] = start;
            matches[cap][capcount * 2 + 1] = len;
            matchcount[cap] = capcount + 1;
        }

        internal void BalanceMatch(int cap)
        {
            _balancing = true;

            // we'll look at the last capture first
            int capcount = _matchcount[cap];
            int target = capcount * 2 - 2;

            // first see if it is negative, and therefore is a reference to the next available
            // capture group for balancing.  If it is, we'll reset target to point to that capture.
            int[][] matches = _matches;
            if (matches[cap][target] < 0)
            {
                target = -3 - matches[cap][target];
            }

            // move back to the previous capture
            target -= 2;

            // if the previous capture is a reference, just copy that reference to the end.  Otherwise, point to it.
            if (target >= 0 && matches[cap][target] < 0)
            {
                AddMatch(cap, matches[cap][target], matches[cap][target + 1]);
            }
            else
            {
                AddMatch(cap, -3 - target, -4 - target /* == -3 - (target + 1) */ );
            }
        }

        internal void RemoveMatch(int cap) => _matchcount[cap]--;

        internal bool IsMatched(int cap)
        {
            int[] matchcount = _matchcount;
            return
                (uint)cap < (uint)matchcount.Length &&
                matchcount[cap] > 0 &&
                _matches[cap][matchcount[cap] * 2 - 1] != (-3 + 1);
        }

        internal int MatchIndex(int cap)
        {
            int[][] matches = _matches;

            int i = matches[cap][_matchcount[cap] * 2 - 2];
            return i >= 0 ? i : matches[cap][-3 - i];
        }

        internal int MatchLength(int cap)
        {
            int[][] matches = _matches;

            int i = matches[cap][_matchcount[cap] * 2 - 1];
            return i >= 0 ? i : matches[cap][-3 - i];
        }

        internal void Tidy(int textpos, int beginningOfSpanSlice, RegexRunnerMode mode)
        {
            Debug.Assert(mode != RegexRunnerMode.ExistenceRequired);

            int[] matchcount = _matchcount;
            _capcount = matchcount[0]; // used to indicate Success

            // Used for NextMatch. During the matching process these stored the span-based offsets.
            // If there was actually a beginning supplied, all of these need to be shifted by that
            // beginning value.
            Debug.Assert(_textbeg == 0);
            _textbeg = beginningOfSpanSlice;
            _textpos = beginningOfSpanSlice + textpos;
            _textend += beginningOfSpanSlice;

            int[][] matches = _matches;
            int[] interval = matches[0];
            Length = interval[1]; // the length of the match
            Index = interval[0] + beginningOfSpanSlice; // the index of the match, adjusted for input slicing

            // At this point the Match is consistent for handing back to a caller, with regards to the span that was processed.
            // However, the caller may have actually provided a string, and may have done so with a non-zero beginning.
            // In such a case, all offsets need to be shifted by beginning, e.g. if beginning is 5 and a capture occurred at
            // offset 17, that 17 offset needs to be increased to 22 to account for the fact that it was actually 17 from the
            // beginning, which the implementation saw as 0 but which from the caller's perspective was 5.
            if (mode == RegexRunnerMode.FullMatchRequired)
            {
                if (_balancing)
                {
                    TidyBalancing();
                }
                Debug.Assert(!_balancing);

                if (beginningOfSpanSlice != 0)
                {
                    for (int groupNumber = 0; groupNumber < matches.Length; groupNumber++)
                    {
                        int[] captures = matches[groupNumber];
                        if (captures is not null)
                        {
                            int capturesLength = matchcount[groupNumber] * 2; // each capture has an offset and a length
                            for (int c = 0; c < capturesLength; c += 2)
                            {
                                captures[c] += beginningOfSpanSlice;
                            }
                        }
                    }
                }
            }
        }

        private void TidyBalancing()
        {
            int[] matchcount = _matchcount;
            int[][] matches = _matches;

            // The idea here is that we want to compact all of our unbalanced captures.  To do that we
            // use j basically as a count of how many unbalanced captures we have at any given time
            // (really j is an index, but j/2 is the count).  First we skip past all of the real captures
            // until we find a balance captures.  Then we check each subsequent entry.  If it's a balance
            // capture (it's negative), we decrement j.  If it's a real capture, we increment j and copy
            // it down to the last free position.
            for (int cap = 0; cap < matchcount.Length; cap++)
            {
                int limit;
                int[] matcharray;

                limit = matchcount[cap] * 2;
                matcharray = matches[cap];

                int i;
                int j;

                for (i = 0; i < limit; i++)
                {
                    if (matcharray[i] < 0)
                    {
                        break;
                    }
                }

                for (j = i; i < limit; i++)
                {
                    if (matcharray[i] < 0)
                    {
                        // skip negative values
                        j--;
                    }
                    else
                    {
                        // but if we find something positive (an actual capture), copy it back to the last
                        // unbalanced position.
                        if (i != j)
                        {
                            matcharray[j] = matcharray[i];
                        }

                        j++;
                    }
                }

                matchcount[cap] = j / 2;
            }

            _balancing = false;
        }
    }

    internal sealed class MatchSparse : Match
    {
        private new readonly Hashtable _caps;

        internal MatchSparse(Regex regex, Hashtable caps, int capcount, string? text, int textLength) :
            base(regex, capcount, text, textLength)
        {
            _caps = caps;
        }

        public override GroupCollection Groups => _groupcoll ??= new GroupCollection(this, _caps);
    }
}
