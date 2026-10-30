// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/Globalization/
// TimeSpanParse.cs and the FormatLiterals part of TimeSpanFormat.cs; see THIRD-PARTY-NOTICES.md. Adapted to the
// csharpc corelib (docs/12-corelib.md); changes of substance are marked `csharpc:`.
#nullable enable annotations

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using System.Text;

    internal static class TimeSpanFormat
    {
        // csharpc: lazily built instead of static readonly fields with initializers.
        private static FormatLiterals[]? s_csharpc_invariant;
        private static FormatLiterals[] Invariant => s_csharpc_invariant ??= new[] { FormatLiterals.InitInvariant(isNegative: false), FormatLiterals.InitInvariant(isNegative: true) };
        internal static FormatLiterals PositiveInvariantFormatLiterals => Invariant[0];
        internal static FormatLiterals NegativeInvariantFormatLiterals => Invariant[1];

        internal struct FormatLiterals
        {
            internal string AppCompatLiteral;
            internal int dd;
            internal int hh;
            internal int mm;
            internal int ss;
            internal int ff;

            private string[] _literals;

            internal string Start => _literals[0];
            internal string DayHourSep => _literals[1];
            internal string HourMinuteSep => _literals[2];
            internal string MinuteSecondSep => _literals[3];
            internal string SecondFractionSep => _literals[4];
            internal string End => _literals[5];

            /* factory method for static invariant FormatLiterals */
            internal static FormatLiterals InitInvariant(bool isNegative)
            {
                FormatLiterals x = default;
                x._literals = new string[6];
                x._literals[0] = isNegative ? "-" : string.Empty;
                x._literals[1] = ".";
                x._literals[2] = ":";
                x._literals[3] = ":";
                x._literals[4] = ".";
                x._literals[5] = string.Empty;
                x.AppCompatLiteral = ":."; // MinuteSecondSep+SecondFractionSep;
                x.dd = 2;
                x.hh = 2;
                x.mm = 2;
                x.ss = 2;
                x.ff = DateTimeFormat.MaxSecondsFractionDigits;
                return x;
            }

            // For the "v1" TimeSpan localized patterns, the data is simply literal field separators with
            // the constants guaranteed to include DHMSF ordered greatest to least significant.
            // Once the data becomes more complex than this we will need to write a proper tokenizer for
            // parsing and formatting
            internal unsafe void Init(ReadOnlySpan<char> format, bool useInvariantFieldLengths)
            {
                dd = hh = mm = ss = ff = 0;
                _literals = new string[6];
                for (int i = 0; i < _literals.Length; i++)
                {
                    _literals[i] = string.Empty;
                }

                var sb = new ValueStringBuilder(stackalloc char[256]);
                bool inQuote = false;
                char quote = '\'';
                int field = 0;

                for (int i = 0; i < format.Length; i++)
                {
                    switch (format[i])
                    {
                        case '\'':
                        case '\"':
                            if (inQuote && (quote == format[i]))
                            {
                                /* we were in a quote and found a matching exit quote, so we are outside a quote now */
                                Debug.Assert(field >= 0 && field <= 5);
                                _literals[field] = sb.AsSpan().ToString();
                                sb.Length = 0;
                                inQuote = false;
                            }
                            else if (!inQuote)
                            {
                                /* we are at the start of a new quote block */
                                quote = format[i];
                                inQuote = true;
                            }
                            else
                            {
                                /* we were in a quote and saw the other type of quote character, so we are still in a quote */
                            }
                            break;
                        case '%':
                            Debug.Fail("Unexpected special token '%', Bug in DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                            goto default;
                        case '\\':
                            if (!inQuote)
                            {
                                i++; /* skip next character that is escaped by this backslash or percent sign */
                                break;
                            }
                            goto default;
                        case 'd':
                            if (!inQuote)
                            {
                                Debug.Assert((field == 0 && sb.Length == 0) || field == 1, "field == 0 || field == 1, Bug in DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                                field = 1; // DayHourSep
                                dd++;
                            }
                            break;
                        case 'h':
                            if (!inQuote)
                            {
                                Debug.Assert((field == 1 && sb.Length == 0) || field == 2, "field == 1 || field == 2, Bug in DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                                field = 2; // HourMinuteSep
                                hh++;
                            }
                            break;
                        case 'm':
                            if (!inQuote)
                            {
                                Debug.Assert((field == 2 && sb.Length == 0) || field == 3, "field == 2 || field == 3, Bug in DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                                field = 3; // MinuteSecondSep
                                mm++;
                            }
                            break;
                        case 's':
                            if (!inQuote)
                            {
                                Debug.Assert((field == 3 && sb.Length == 0) || field == 4, "field == 3 || field == 4, Bug in DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                                field = 4; // SecondFractionSep
                                ss++;
                            }
                            break;
                        case 'f':
                        case 'F':
                            if (!inQuote)
                            {
                                Debug.Assert((field == 4 && sb.Length == 0) || field == 5, "field == 4 || field == 5, Bug in DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                                field = 5; // End
                                ff++;
                            }
                            break;
                        default:
                            sb.Append(format[i]);
                            break;
                    }
                }

                sb.Dispose();

                Debug.Assert(field == 5);
                AppCompatLiteral = MinuteSecondSep + SecondFractionSep;

                Debug.Assert(0 < dd && dd < 3, "0 < dd && dd < 3, Bug in System.Globalization.DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                Debug.Assert(0 < hh && hh < 3, "0 < hh && hh < 3, Bug in System.Globalization.DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                Debug.Assert(0 < mm && mm < 3, "0 < mm && mm < 3, Bug in System.Globalization.DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                Debug.Assert(0 < ss && ss < 3, "0 < ss && ss < 3, Bug in System.Globalization.DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");
                Debug.Assert(0 < ff && ff < 8, "0 < ff && ff < 8, Bug in System.Globalization.DateTimeFormatInfo.FullTimeSpan[Positive|Negative]Pattern");

                if (useInvariantFieldLengths)
                {
                    dd = 2;
                    hh = 2;
                    mm = 2;
                    ss = 2;
                    ff = DateTimeFormat.MaxSecondsFractionDigits;
                }
                else
                {
                    if (dd < 1 || dd > 2) dd = 2;   // The DTFI property has a problem. let's try to make the best of the situation.
                    if (hh < 1 || hh > 2) hh = 2;
                    if (mm < 1 || mm > 2) mm = 2;
                    if (ss < 1 || ss > 2) ss = 2;
                    if (ff < 1 || ff > 7) ff = 7;
                }
            }
        }
    }

    internal static class TimeSpanParse
    {
        // csharpc: FormattingHelpers.CountDigits(uint).
        private static int CsharpcCountDigits(uint value)
        {
            int digits = 1;
            while (value >= 10) { value /= 10; digits++; }
            return digits;
        }

        private const int MaxFractionDigits = 7;
        private const int MaxDays = 10675199;
        private const int MaxHours = 23;
        private const int MaxMinutes = 59;
        private const int MaxSeconds = 59;
        private const int MaxFraction = 9999999;

        [Flags]
        private enum TimeSpanStandardStyles : byte
        {
            // Standard Format Styles
            None = 0x00000000,
            Invariant = 0x00000001, // Allow Invariant Culture
            Localized = 0x00000002, // Allow Localized Culture
            RequireFull = 0x00000004, // Require the input to be in DHMSF format
            Any = Invariant | Localized,
        }

        // TimeSpan Token Types
        private enum TTT : byte
        {
            None = 0,         // None of the TimeSpanToken fields are set
            End = 1,          // '\0'
            Num = 2,          // Number
            Sep = 3,          // literal
            NumOverflow = 4,  // Number that overflowed
        }

        private ref struct TimeSpanToken
        {
            internal TTT _ttt;
            internal int _num;                // Store the number that we are parsing (if any)
            internal int _zeroes;             // Store the number of leading zeroes (if any)
            internal ReadOnlySpan<char> _sep; // Store the literal that we are parsing (if any)

            public TimeSpanToken(TTT type) : this(type, 0, 0, default) { }

            public TimeSpanToken(int number) : this(TTT.Num, number, 0, default) { }

            public TimeSpanToken(int number, int leadingZeroes) : this(TTT.Num, number, leadingZeroes, default) { }

            public TimeSpanToken(TTT type, int number, int leadingZeroes, ReadOnlySpan<char> separator)
            {
                _ttt = type;
                _num = number;
                _zeroes = leadingZeroes;
                _sep = separator;
            }

            public bool NormalizeAndValidateFraction()
            {
                Debug.Assert(_ttt == TTT.Num);
                Debug.Assert(_num > -1);

                if (_num == 0)
                    return true;

                if (_zeroes == 0 && _num > MaxFraction)
                    return false;

                int totalDigitsCount = CsharpcCountDigits((uint)_num) + _zeroes;

                if (totalDigitsCount == MaxFractionDigits)
                {
                    // Already normalized. no more action needed
                    // .9999999  normalize to 9,999,999 ticks
                    // .0000001  normalize to 1 ticks
                    return true;
                }

                if (totalDigitsCount < MaxFractionDigits)
                {
                    // normalize the fraction to the 7-digits
                    // .999999  normalize to 9,999,990 ticks
                    // .99999   normalize to 9,999,900 ticks
                    // .000001  normalize to 10 ticks
                    // .1       normalize to 1,000,000 ticks

                    _num *= Pow10UpToMaxFractionDigits(MaxFractionDigits - totalDigitsCount);
                    return true;
                }

                // totalDigitsCount is greater then MaxFractionDigits, we'll need to do the rounding to 7-digits length
                // .00000001    normalized to 0 ticks
                // .00000005    normalized to 1 ticks
                // .09999999    normalize to 1,000,000 ticks
                // .099999999   normalize to 1,000,000 ticks

                Debug.Assert(_zeroes > 0); // Already validated that in the condition _zeroes == 0 && _num > MaxFraction

                if (_zeroes > MaxFractionDigits)
                {
                    // If there are 8 leading zeroes, it rounds to zero
                    _num = 0;
                    return true;
                }

                Debug.Assert(totalDigitsCount - MaxFractionDigits <= MaxFractionDigits);
                uint power = (uint)Pow10UpToMaxFractionDigits(totalDigitsCount - MaxFractionDigits);
                // Unsigned integer division, rounding away from zero
                _num = (int)(((uint)_num + power / 2) / power);
                Debug.Assert(_num < MaxFraction);

                return true;
            }
        }

        private ref struct TimeSpanTokenizer
        {
            private readonly ReadOnlySpan<char> _value;
            private int _pos;

            internal TimeSpanTokenizer(ReadOnlySpan<char> input) : this(input, 0) { }

            internal TimeSpanTokenizer(ReadOnlySpan<char> input, int startPosition)
            {
                _value = input;
                _pos = startPosition;
            }

            /// <summary>Returns the next token in the input string</summary>
            /// <remarks>Used by the parsing routines that operate on standard-formats.</remarks>
            internal TimeSpanToken GetNextToken()
            {
                // Get the position of the next character to be processed.  If there is no
                // next character, we're at the end.
                int startPos = _pos;
                int pos = startPos;
                ReadOnlySpan<char> value = _value;
                Debug.Assert(pos > -1);
                if ((uint)pos >= (uint)value.Length)
                {
                    return new TimeSpanToken(TTT.End);
                }

                // Now retrieve that character. If it's a digit, we're processing a number.
                int num = value[pos] - '0';
                if ((uint)num <= 9)
                {
                    int zeroes = 0;
                    if (num == 0)
                    {
                        // Read all leading zeroes.
                        zeroes = 1;
                        while (true)
                        {
                            int digit;
                            if ((uint)++pos >= (uint)value.Length || (uint)(digit = value[pos] - '0') > 9)
                            {
                                _pos = pos;
                                return new TimeSpanToken(TTT.Num, 0, zeroes, default);
                            }

                            if (digit == 0)
                            {
                                zeroes++;
                                continue;
                            }

                            num = digit;
                            break;
                        }

                        _pos = pos;
                    }

                    // Continue to read as long as we're reading digits.
                    while ((uint)++pos < (uint)value.Length)
                    {
                        int digit = value[pos] - '0';
                        if ((uint)digit > 9)
                        {
                            break;
                        }

                        num = num * 10 + digit;
                        if ((num & 0xF0000000) != 0) // Max limit we can support 268435455 which is FFFFFFF
                        {
                            _pos = pos;
                            return new TimeSpanToken(TTT.NumOverflow);
                        }
                    }

                    _pos = pos;
                    return new TimeSpanToken(TTT.Num, num, zeroes, default);
                }

                // Otherwise, we're processing a separator, and we've already processed the first
                // character of it.  Continue processing characters as long as they're not digits.
                int length = 1;
                while ((uint)++pos < (uint)value.Length && !char.IsAsciiDigit(value[pos]))
                {
                    length++;
                }
                _pos = pos;

                // Return the separator.
                return new TimeSpanToken(TTT.Sep, 0, 0, _value.Slice(startPos, length));
            }

            internal bool EOL => _pos >= (_value.Length - 1);

            internal void BackOne()
            {
                if (_pos > 0) --_pos;
            }

            internal char NextChar()
            {
                int pos = ++_pos;
                return (uint)pos < (uint)_value.Length ?
                    _value[pos] :
                    (char)0;
            }
        }

        /// <summary>Stores intermediary parsing state for the standard formats.</summary>
        private ref struct TimeSpanRawInfo
        {
            internal TimeSpanFormat.FormatLiterals PositiveLocalized
            {
                get
                {
                    if (!_posLocInit)
                    {
                        _posLoc = default;
                        _posLoc.Init(_fullPosPattern, false);
                        _posLocInit = true;
                    }
                    return _posLoc;
                }
            }

            internal TimeSpanFormat.FormatLiterals NegativeLocalized
            {
                get
                {
                    if (!_negLocInit)
                    {
                        _negLoc = default;
                        _negLoc.Init(_fullNegPattern, false);
                        _negLocInit = true;
                    }
                    return _negLoc;
                }
            }

            internal bool FullAppCompatMatch(TimeSpanFormat.FormatLiterals pattern) =>
                _sepCount == 5
                && _numCount == 4
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals0, pattern.Start)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals1, pattern.DayHourSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals2, pattern.HourMinuteSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals3, pattern.AppCompatLiteral)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals4, pattern.End);

            internal bool PartialAppCompatMatch(TimeSpanFormat.FormatLiterals pattern) =>
                _sepCount == 4
                && _numCount == 3
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals0, pattern.Start)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals1, pattern.HourMinuteSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals2, pattern.AppCompatLiteral)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals3, pattern.End);

            /// <summary>DHMSF (all values matched)</summary>
            internal bool FullMatch(TimeSpanFormat.FormatLiterals pattern) =>
                _sepCount == MaxLiteralTokens
                && _numCount == MaxNumericTokens
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals0, pattern.Start)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals1, pattern.DayHourSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals2, pattern.HourMinuteSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals3, pattern.MinuteSecondSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals4, pattern.SecondFractionSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals5, pattern.End);

            /// <summary>D (no hours, minutes, seconds, or fractions)</summary>
            internal bool FullDMatch(TimeSpanFormat.FormatLiterals pattern) =>
                _sepCount == 2
                && _numCount == 1
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals0, pattern.Start)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals1, pattern.End);

            /// <summary>HM (no days, seconds, or fractions)</summary>
            internal bool FullHMMatch(TimeSpanFormat.FormatLiterals pattern) =>
                _sepCount == 3
                && _numCount == 2
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals0, pattern.Start)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals1, pattern.HourMinuteSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals2, pattern.End);

            /// <summary>DHM (no seconds or fraction)</summary>
            internal bool FullDHMMatch(TimeSpanFormat.FormatLiterals pattern) =>
                _sepCount == 4
                && _numCount == 3
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals0, pattern.Start)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals1, pattern.DayHourSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals2, pattern.HourMinuteSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals3, pattern.End);

            /// <summary>HMS (no days or fraction)</summary>
            internal bool FullHMSMatch(TimeSpanFormat.FormatLiterals pattern) =>
                _sepCount == 4
                && _numCount == 3
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals0, pattern.Start)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals1, pattern.HourMinuteSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals2, pattern.MinuteSecondSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals3, pattern.End);

            /// <summary>DHMS (no fraction)</summary>
            internal bool FullDHMSMatch(TimeSpanFormat.FormatLiterals pattern) =>
                _sepCount == 5
                && _numCount == 4
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals0, pattern.Start)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals1, pattern.DayHourSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals2, pattern.HourMinuteSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals3, pattern.MinuteSecondSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals4, pattern.End);

            /// <summary>HMSF (no days)</summary>
            internal bool FullHMSFMatch(TimeSpanFormat.FormatLiterals pattern) =>
                _sepCount == 5
                && _numCount == 4
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals0, pattern.Start)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals1, pattern.HourMinuteSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals2, pattern.MinuteSecondSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals3, pattern.SecondFractionSep)
                && CsharpcCoreLib.StringOps.EqualsOrdinal(_literals4, pattern.End);

            internal TTT _lastSeenTTT;
            internal int _tokenCount;
            internal int _sepCount;
            internal int _numCount;

            private TimeSpanFormat.FormatLiterals _posLoc;
            private TimeSpanFormat.FormatLiterals _negLoc;
            private bool _posLocInit;
            private bool _negLocInit;
            private string _fullPosPattern;
            private string _fullNegPattern;

            private const int MaxTokens = 11;
            private const int MaxLiteralTokens = 6;
            private const int MaxNumericTokens = 5;

            internal TimeSpanToken _numbers0, _numbers1, _numbers2, _numbers3, _numbers4; // MaxNumericTokens = 5
            internal ReadOnlySpan<char> _literals0, _literals1, _literals2, _literals3, _literals4, _literals5; // MaxLiteralTokens=6

            internal void Init(DateTimeFormatInfo dtfi)
            {
                Debug.Assert(dtfi != null);

                _lastSeenTTT = TTT.None;
                _tokenCount = 0;
                _sepCount = 0;
                _numCount = 0;

                _fullPosPattern = dtfi.FullTimeSpanPositivePattern;
                _fullNegPattern = dtfi.FullTimeSpanNegativePattern;
                _posLocInit = false;
                _negLocInit = false;
            }

            internal bool ProcessToken(ref TimeSpanToken tok, ref TimeSpanResult result)
            {
                switch (tok._ttt)
                {
                    case TTT.Num:
                        if ((_tokenCount == 0 && !AddSep(default, ref result)) || !AddNum(tok, ref result))
                        {
                            return false;
                        }
                        break;

                    case TTT.Sep:
                        if (!AddSep(tok._sep, ref result))
                        {
                            return false;
                        }
                        break;

                    case TTT.NumOverflow:
                        return result.SetOverflowFailure();

                    default:
                        // Some unknown token or a repeat token type in the input
                        return result.SetBadTimeSpanFailure();
                }

                _lastSeenTTT = tok._ttt;
                Debug.Assert(_tokenCount == (_sepCount + _numCount), "tokenCount == (SepCount + NumCount)");
                return true;
            }

            private bool AddSep(ReadOnlySpan<char> sep, ref TimeSpanResult result)
            {
                if (_sepCount >= MaxLiteralTokens || _tokenCount >= MaxTokens)
                {
                    return result.SetBadTimeSpanFailure();
                }

                switch (_sepCount++)
                {
                    case 0: _literals0 = sep; break;
                    case 1: _literals1 = sep; break;
                    case 2: _literals2 = sep; break;
                    case 3: _literals3 = sep; break;
                    case 4: _literals4 = sep; break;
                    default: _literals5 = sep; break;
                }

                _tokenCount++;
                return true;
            }
            private bool AddNum(TimeSpanToken num, ref TimeSpanResult result)
            {
                if (_numCount >= MaxNumericTokens || _tokenCount >= MaxTokens)
                {
                    return result.SetBadTimeSpanFailure();
                }

                switch (_numCount++)
                {
                    case 0: _numbers0 = num; break;
                    case 1: _numbers1 = num; break;
                    case 2: _numbers2 = num; break;
                    case 3: _numbers3 = num; break;
                    default: _numbers4 = num; break;
                }

                _tokenCount++;
                return true;
            }
        }

        /// <summary>Store the result of the parsing.</summary>
        private ref struct TimeSpanResult
        {
            internal TimeSpan parsedTimeSpan;
            private readonly bool _throwOnFailure;
            private readonly ReadOnlySpan<char> _originalTimeSpanString;

            internal TimeSpanResult(bool throwOnFailure, ReadOnlySpan<char> originalTimeSpanString)
            {
                parsedTimeSpan = default;
                _throwOnFailure = throwOnFailure;
                _originalTimeSpanString = originalTimeSpanString;
            }

            internal bool SetNoFormatSpecifierFailure()
            {
                if (!_throwOnFailure)
                {
                    return false;
                }

                throw new FormatException(SR.Format_NoFormatSpecifier);
            }

            internal bool SetBadQuoteFailure(char failingCharacter)
            {
                if (!_throwOnFailure)
                {
                    return false;
                }

                throw new FormatException(SR.Format(SR.Format_BadQuote, failingCharacter));
            }

            internal bool SetInvalidStringFailure()
            {
                if (!_throwOnFailure)
                {
                    return false;
                }

                throw new FormatException(SR.Format_InvalidString);
            }

            internal bool SetArgumentNullFailure(string argumentName)
            {
                if (_throwOnFailure)
                {
                    Debug.Assert(argumentName != null);
                    throw new ArgumentNullException(argumentName); // csharpc: no ArgumentNullException.Throw
                }

                return false;
            }

            internal bool SetOverflowFailure()
            {
                if (!_throwOnFailure)
                {
                    return false;
                }

                throw new OverflowException(SR.Format(SR.Overflow_TimeSpanElementTooLarge, new string(_originalTimeSpanString)));
            }

            internal bool SetBadTimeSpanFailure()
            {
                if (!_throwOnFailure)
                {
                    return false;
                }

                throw new FormatException(SR.Format(SR.Format_BadTimeSpan, new string(_originalTimeSpanString)));
            }

            internal bool SetBadFormatSpecifierFailure(char? formatSpecifierCharacter = null)
            {
                if (!_throwOnFailure)
                {
                    return false;
                }

                throw new FormatException(SR.Format(SR.Format_BadFormatSpecifier, formatSpecifierCharacter));
            }
        }

        internal static int Pow10UpToMaxFractionDigits(int pow)
        {
            ReadOnlySpan<int> powersOfTen =
            [
                1,
                10,
                100,
                1000,
                10000,
                100000,
                1000000,
                10000000,
            ];
            Debug.Assert(powersOfTen.Length == MaxFractionDigits + 1);
            return powersOfTen[pow];
        }

        private static bool TryTimeToTicks(bool positive, TimeSpanToken days, TimeSpanToken hours, TimeSpanToken minutes, TimeSpanToken seconds, TimeSpanToken fraction, out long result)
        {
            if (days._num > MaxDays ||
                hours._num > MaxHours ||
                minutes._num > MaxMinutes ||
                seconds._num > MaxSeconds ||
                !fraction.NormalizeAndValidateFraction())
            {
                result = 0;
                return false;
            }

            const long MaxMilliSeconds = long.MaxValue / TimeSpan.TicksPerMillisecond;
            const long MinMilliSeconds = long.MinValue / TimeSpan.TicksPerMillisecond;

            long ticks = ((long)days._num * 3600 * 24 + (long)hours._num * 3600 + (long)minutes._num * 60 + seconds._num) * 1000;
            if (ticks > MaxMilliSeconds || ticks < MinMilliSeconds)
            {
                result = 0;
                return false;
            }

            result = ticks * TimeSpan.TicksPerMillisecond + fraction._num;
            if (positive && result < 0)
            {
                result = 0;
                return false;
            }

            return true;
        }

        internal static TimeSpan Parse(ReadOnlySpan<char> input, IFormatProvider? formatProvider)
        {
            var parseResult = new TimeSpanResult(throwOnFailure: true, originalTimeSpanString: input);
            bool success = TryParseTimeSpan(input, TimeSpanStandardStyles.Any, formatProvider, ref parseResult);
            Debug.Assert(success, "Should have thrown on failure");
            return parseResult.parsedTimeSpan;
        }

        internal static bool TryParse(ReadOnlySpan<char> input, IFormatProvider? formatProvider, out TimeSpan result)
        {
            var parseResult = new TimeSpanResult(throwOnFailure: false, originalTimeSpanString: input);

            if (TryParseTimeSpan(input, TimeSpanStandardStyles.Any, formatProvider, ref parseResult))
            {
                result = parseResult.parsedTimeSpan;
                return true;
            }

            result = default;
            return false;
        }

        internal static TimeSpan ParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format, IFormatProvider? formatProvider, TimeSpanStyles styles)
        {
            var parseResult = new TimeSpanResult(throwOnFailure: true, originalTimeSpanString: input);
            bool success = TryParseExactTimeSpan(input, format, formatProvider, styles, ref parseResult);
            Debug.Assert(success, "Should have thrown on failure");
            return parseResult.parsedTimeSpan;
        }

        internal static bool TryParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            var parseResult = new TimeSpanResult(throwOnFailure: false, originalTimeSpanString: input);

            if (TryParseExactTimeSpan(input, format, formatProvider, styles, ref parseResult))
            {
                result = parseResult.parsedTimeSpan;
                return true;
            }

            result = default;
            return false;
        }

        internal static TimeSpan ParseExactMultiple(ReadOnlySpan<char> input, string?[]? formats, IFormatProvider? formatProvider, TimeSpanStyles styles)
        {
            var parseResult = new TimeSpanResult(throwOnFailure: true, originalTimeSpanString: input);
            bool success = TryParseExactMultipleTimeSpan(input, formats, formatProvider, styles, ref parseResult);
            Debug.Assert(success, "Should have thrown on failure");
            return parseResult.parsedTimeSpan;
        }

        internal static bool TryParseExactMultiple(ReadOnlySpan<char> input, string?[]? formats, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            var parseResult = new TimeSpanResult(throwOnFailure: false, originalTimeSpanString: input);

            if (TryParseExactMultipleTimeSpan(input, formats, formatProvider, styles, ref parseResult))
            {
                result = parseResult.parsedTimeSpan;
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>Common private Parse method called by both Parse and TryParse.</summary>
        private static bool TryParseTimeSpan(ReadOnlySpan<char> input, TimeSpanStandardStyles style, IFormatProvider? formatProvider, ref TimeSpanResult result)
        {
            input = input.Trim();
            if (input.IsEmpty)
            {
                return result.SetBadTimeSpanFailure();
            }

            var tokenizer = new TimeSpanTokenizer(input);

            TimeSpanRawInfo raw = default;
            raw.Init(DateTimeFormatInfo.GetInstance(formatProvider));

            TimeSpanToken tok = tokenizer.GetNextToken();

            // The following loop will break out when we reach the end of the str or
            // when we can determine that the input is invalid.
            while (tok._ttt != TTT.End)
            {
                if (!raw.ProcessToken(ref tok, ref result))
                {
                    return result.SetBadTimeSpanFailure();
                }
                tok = tokenizer.GetNextToken();
            }
            Debug.Assert(tokenizer.EOL);

            if (!ProcessTerminalState(ref raw, style, ref result))
            {
                return result.SetBadTimeSpanFailure();
            }

            return true;
        }

        /// <summary>
        /// Validate the terminal state of a standard format parse.
        /// Sets result.parsedTimeSpan on success.
        /// Calculates the resultant TimeSpan from the TimeSpanRawInfo.
        /// </summary>
        /// <remarks>
        /// try => +InvariantPattern, -InvariantPattern, +LocalizedPattern, -LocalizedPattern
        /// 1) Verify Start matches
        /// 2) Verify End matches
        /// 3) 1 number  => d
        ///    2 numbers => h:m
        ///    3 numbers => h:m:s | d.h:m | h:m:.f
        ///    4 numbers => h:m:s.f | d.h:m:s | d.h:m:.f
        ///    5 numbers => d.h:m:s.f
        /// </remarks>
        private static bool ProcessTerminalState(ref TimeSpanRawInfo raw, TimeSpanStandardStyles style, ref TimeSpanResult result)
        {
            if (raw._lastSeenTTT == TTT.Num)
            {
                TimeSpanToken tok = default;
                tok._ttt = TTT.Sep;
                if (!raw.ProcessToken(ref tok, ref result))
                {
                    return result.SetBadTimeSpanFailure();
                }
            }

            return raw._numCount switch
            {
                1 => ProcessTerminal_D(ref raw, style, ref result),
                2 => ProcessTerminal_HM(ref raw, style, ref result),
                3 => ProcessTerminal_HM_S_D(ref raw, style, ref result),
                4 => ProcessTerminal_HMS_F_D(ref raw, style, ref result),
                5 => ProcessTerminal_DHMSF(ref raw, style, ref result),
                _ => result.SetBadTimeSpanFailure(),
            };
        }

        /// <summary>Validate the 5-number "Days.Hours:Minutes:Seconds.Fraction" terminal case.</summary>
        private static bool ProcessTerminal_DHMSF(ref TimeSpanRawInfo raw, TimeSpanStandardStyles style, ref TimeSpanResult result)
        {
            if (raw._sepCount != 6)
            {
                return result.SetBadTimeSpanFailure();
            }
            Debug.Assert(raw._numCount == 5);

            bool inv = (style & TimeSpanStandardStyles.Invariant) != 0;
            bool loc = (style & TimeSpanStandardStyles.Localized) != 0;
            bool positive = false;
            bool match = false;

            if (inv)
            {
                if (raw.FullMatch(TimeSpanFormat.PositiveInvariantFormatLiterals))
                {
                    match = true;
                    positive = true;
                }
                if (!match && raw.FullMatch(TimeSpanFormat.NegativeInvariantFormatLiterals))
                {
                    match = true;
                    positive = false;
                }
            }

            if (loc)
            {
                if (!match && raw.FullMatch(raw.PositiveLocalized))
                {
                    match = true;
                    positive = true;
                }
                if (!match && raw.FullMatch(raw.NegativeLocalized))
                {
                    match = true;
                    positive = false;
                }
            }

            if (match)
            {

                if (!TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, raw._numbers3, raw._numbers4, out long ticks))
                {
                    return result.SetOverflowFailure();
                }

                if (!positive)
                {
                    ticks = -ticks;
                    if (ticks > 0)
                    {
                        return result.SetOverflowFailure();
                    }
                }

                result.parsedTimeSpan = new TimeSpan(ticks);
                return true;
            }

            return result.SetBadTimeSpanFailure();
        }


        /// <summary>
        /// Validate the ambiguous 4-number "Hours:Minutes:Seconds.Fraction", "Days.Hours:Minutes:Seconds",
        /// or "Days.Hours:Minutes:.Fraction" terminal case.
        /// </summary>
        private static bool ProcessTerminal_HMS_F_D(ref TimeSpanRawInfo raw, TimeSpanStandardStyles style, ref TimeSpanResult result)
        {
            if (raw._sepCount != 5 || (style & TimeSpanStandardStyles.RequireFull) != 0)
            {
                return result.SetBadTimeSpanFailure();
            }
            Debug.Assert(raw._numCount == 4);

            bool inv = ((style & TimeSpanStandardStyles.Invariant) != 0);
            bool loc = ((style & TimeSpanStandardStyles.Localized) != 0);

            long ticks = 0;
            bool positive = false, match = false, overflow = false;
            var zero = new TimeSpanToken(0);

            if (inv)
            {
                if (raw.FullHMSFMatch(TimeSpanFormat.PositiveInvariantFormatLiterals))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, raw._numbers2, raw._numbers3, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullDHMSMatch(TimeSpanFormat.PositiveInvariantFormatLiterals))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, raw._numbers3, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullAppCompatMatch(TimeSpanFormat.PositiveInvariantFormatLiterals))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, zero, raw._numbers3, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullHMSFMatch(TimeSpanFormat.NegativeInvariantFormatLiterals))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, raw._numbers2, raw._numbers3, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullDHMSMatch(TimeSpanFormat.NegativeInvariantFormatLiterals))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, raw._numbers3, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullAppCompatMatch(TimeSpanFormat.NegativeInvariantFormatLiterals))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, zero, raw._numbers3, out ticks);
                    overflow = overflow || !match;
                }
            }

            if (loc)
            {
                if (!match && raw.FullHMSFMatch(raw.PositiveLocalized))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, raw._numbers2, raw._numbers3, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullDHMSMatch(raw.PositiveLocalized))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, raw._numbers3, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullAppCompatMatch(raw.PositiveLocalized))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, zero, raw._numbers3, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullHMSFMatch(raw.NegativeLocalized))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, raw._numbers2, raw._numbers3, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullDHMSMatch(raw.NegativeLocalized))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, raw._numbers3, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullAppCompatMatch(raw.NegativeLocalized))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, zero, raw._numbers3, out ticks);
                    overflow = overflow || !match;
                }
            }

            if (match)
            {
                if (!positive)
                {
                    ticks = -ticks;
                    if (ticks > 0)
                    {
                        return result.SetOverflowFailure();
                    }
                }

                result.parsedTimeSpan = new TimeSpan(ticks);
                return true;
            }

            return overflow ?
                result.SetOverflowFailure() : // we found at least one literal pattern match but the numbers just didn't fit
                result.SetBadTimeSpanFailure(); // we couldn't find a thing
        }

        /// <summary>Validate the ambiguous 3-number "Hours:Minutes:Seconds", "Days.Hours:Minutes", or "Hours:Minutes:.Fraction" terminal case.</summary>
        private static bool ProcessTerminal_HM_S_D(ref TimeSpanRawInfo raw, TimeSpanStandardStyles style, ref TimeSpanResult result)
        {
            if (raw._sepCount != 4 || (style & TimeSpanStandardStyles.RequireFull) != 0)
            {
                return result.SetBadTimeSpanFailure();
            }
            Debug.Assert(raw._numCount == 3);

            bool inv = ((style & TimeSpanStandardStyles.Invariant) != 0);
            bool loc = ((style & TimeSpanStandardStyles.Localized) != 0);

            bool positive = false, match = false, overflow = false;
            var zero = new TimeSpanToken(0);
            long ticks = 0;

            if (inv)
            {
                if (raw.FullHMSMatch(TimeSpanFormat.PositiveInvariantFormatLiterals))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, raw._numbers2, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullDHMMatch(TimeSpanFormat.PositiveInvariantFormatLiterals))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, zero, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.PartialAppCompatMatch(TimeSpanFormat.PositiveInvariantFormatLiterals))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, zero, raw._numbers2, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullHMSMatch(TimeSpanFormat.NegativeInvariantFormatLiterals))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, raw._numbers2, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullDHMMatch(TimeSpanFormat.NegativeInvariantFormatLiterals))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, zero, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.PartialAppCompatMatch(TimeSpanFormat.NegativeInvariantFormatLiterals))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, zero, raw._numbers2, out ticks);
                    overflow = overflow || !match;
                }
            }

            if (loc)
            {
                if (!match && raw.FullHMSMatch(raw.PositiveLocalized))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, raw._numbers2, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullDHMMatch(raw.PositiveLocalized))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, zero, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.PartialAppCompatMatch(raw.PositiveLocalized))
                {
                    positive = true;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, zero, raw._numbers2, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullHMSMatch(raw.NegativeLocalized))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, raw._numbers2, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.FullDHMMatch(raw.NegativeLocalized))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, raw._numbers0, raw._numbers1, raw._numbers2, zero, zero, out ticks);
                    overflow = overflow || !match;
                }

                if (!match && raw.PartialAppCompatMatch(raw.NegativeLocalized))
                {
                    positive = false;
                    match = TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, zero, raw._numbers2, out ticks);
                    overflow = overflow || !match;
                }
            }

            if (match)
            {
                if (!positive)
                {
                    ticks = -ticks;
                    if (ticks > 0)
                    {
                        return result.SetOverflowFailure();
                    }
                }

                result.parsedTimeSpan = new TimeSpan(ticks);
                return true;
            }

            return overflow ?
                result.SetOverflowFailure() : // we found at least one literal pattern match but the numbers just didn't fit
                result.SetBadTimeSpanFailure(); // we couldn't find a thing
        }

        /// <summary>Validate the 2-number "Hours:Minutes" terminal case.</summary>
        private static bool ProcessTerminal_HM(ref TimeSpanRawInfo raw, TimeSpanStandardStyles style, ref TimeSpanResult result)
        {
            if (raw._sepCount != 3 || (style & TimeSpanStandardStyles.RequireFull) != 0)
            {
                return result.SetBadTimeSpanFailure();
            }
            Debug.Assert(raw._numCount == 2);

            bool inv = ((style & TimeSpanStandardStyles.Invariant) != 0);
            bool loc = ((style & TimeSpanStandardStyles.Localized) != 0);

            bool positive = false, match = false;

            if (inv)
            {
                if (raw.FullHMMatch(TimeSpanFormat.PositiveInvariantFormatLiterals))
                {
                    match = true;
                    positive = true;
                }

                if (!match && raw.FullHMMatch(TimeSpanFormat.NegativeInvariantFormatLiterals))
                {
                    match = true;
                    positive = false;
                }
            }

            if (loc)
            {
                if (!match && raw.FullHMMatch(raw.PositiveLocalized))
                {
                    match = true;
                    positive = true;
                }

                if (!match && raw.FullHMMatch(raw.NegativeLocalized))
                {
                    match = true;
                    positive = false;
                }
            }

            if (match)
            {
                var zero = new TimeSpanToken(0);

                if (!TryTimeToTicks(positive, zero, raw._numbers0, raw._numbers1, zero, zero, out long ticks))
                {
                    return result.SetOverflowFailure();
                }

                if (!positive)
                {
                    ticks = -ticks;
                    if (ticks > 0)
                    {
                        return result.SetOverflowFailure();
                    }
                }

                result.parsedTimeSpan = new TimeSpan(ticks);
                return true;
            }

            return result.SetBadTimeSpanFailure();
        }

        /// <summary>Validate the 1-number "Days" terminal case.</summary>
        private static bool ProcessTerminal_D(ref TimeSpanRawInfo raw, TimeSpanStandardStyles style, ref TimeSpanResult result)
        {
            if (raw._sepCount != 2 || (style & TimeSpanStandardStyles.RequireFull) != 0)
            {
                return result.SetBadTimeSpanFailure();
            }
            Debug.Assert(raw._numCount == 1);

            bool inv = ((style & TimeSpanStandardStyles.Invariant) != 0);
            bool loc = ((style & TimeSpanStandardStyles.Localized) != 0);

            bool positive = false, match = false;

            if (inv)
            {
                if (raw.FullDMatch(TimeSpanFormat.PositiveInvariantFormatLiterals))
                {
                    match = true;
                    positive = true;
                }

                if (!match && raw.FullDMatch(TimeSpanFormat.NegativeInvariantFormatLiterals))
                {
                    match = true;
                    positive = false;
                }
            }

            if (loc)
            {
                if (!match && raw.FullDMatch(raw.PositiveLocalized))
                {
                    match = true;
                    positive = true;
                }

                if (!match && raw.FullDMatch(raw.NegativeLocalized))
                {
                    match = true;
                    positive = false;
                }
            }

            if (match)
            {
                var zero = new TimeSpanToken(0);

                if (!TryTimeToTicks(positive, raw._numbers0, zero, zero, zero, zero, out long ticks))
                {
                    return result.SetOverflowFailure();
                }

                if (!positive)
                {
                    ticks = -ticks;
                    if (ticks > 0)
                    {
                        return result.SetOverflowFailure();
                    }
                }

                result.parsedTimeSpan = new TimeSpan(ticks);
                return true;
            }

            return result.SetBadTimeSpanFailure();
        }

        /// <summary>Common private ParseExact method called by both ParseExact and TryParseExact.</summary>
        private static bool TryParseExactTimeSpan(ReadOnlySpan<char> input, ReadOnlySpan<char> format, IFormatProvider? formatProvider, TimeSpanStyles styles, ref TimeSpanResult result)
        {
            if (format.Length == 0)
            {
                return result.SetBadFormatSpecifierFailure();
            }

            if (format.Length == 1)
            {
                return format[0] switch
                {
                    'c' or 't' or 'T' => TryParseTimeSpanConstant(input, ref result), // fast path for legacy style TimeSpan formats.
                    'g' => TryParseTimeSpan(input, TimeSpanStandardStyles.Localized, formatProvider, ref result),
                    'G' => TryParseTimeSpan(input, TimeSpanStandardStyles.Localized | TimeSpanStandardStyles.RequireFull, formatProvider, ref result),
                    _ => result.SetBadFormatSpecifierFailure(format[0]),
                };
            }

            return TryParseByFormat(input, format, styles, ref result);
        }

        /// <summary>Parse the TimeSpan instance using the specified format.  Used by TryParseExactTimeSpan.</summary>
        private static bool TryParseByFormat(ReadOnlySpan<char> input, ReadOnlySpan<char> format, TimeSpanStyles styles, ref TimeSpanResult result)
        {
            bool seenDD = false;      // already processed days?
            bool seenHH = false;      // already processed hours?
            bool seenMM = false;      // already processed minutes?
            bool seenSS = false;      // already processed seconds?
            bool seenFF = false;      // already processed fraction?

            int dd = 0;               // parsed days
            int hh = 0;               // parsed hours
            int mm = 0;               // parsed minutes
            int ss = 0;               // parsed seconds
            int leadingZeroes = 0;    // number of leading zeroes in the parsed fraction
            int ff = 0;               // parsed fraction
            int i = 0;                // format string position
            int tokenLen;             // length of current format token, used to update index 'i'

            var tokenizer = new TimeSpanTokenizer(input, -1);

            while ((uint)i < (uint)format.Length)
            {
                char ch = format[i];
                int nextFormatChar;
                switch (ch)
                {
                    case 'h':
                        tokenLen = DateTimeFormat.ParseRepeatPattern(format, i, ch);
                        if (tokenLen > 2 || seenHH || !ParseExactDigits(ref tokenizer, tokenLen, out hh))
                        {
                            return result.SetInvalidStringFailure();
                        }
                        seenHH = true;
                        break;

                    case 'm':
                        tokenLen = DateTimeFormat.ParseRepeatPattern(format, i, ch);
                        if (tokenLen > 2 || seenMM || !ParseExactDigits(ref tokenizer, tokenLen, out mm))
                        {
                            return result.SetInvalidStringFailure();
                        }
                        seenMM = true;
                        break;

                    case 's':
                        tokenLen = DateTimeFormat.ParseRepeatPattern(format, i, ch);
                        if (tokenLen > 2 || seenSS || !ParseExactDigits(ref tokenizer, tokenLen, out ss))
                        {
                            return result.SetInvalidStringFailure();
                        }
                        seenSS = true;
                        break;

                    case 'f':
                        tokenLen = DateTimeFormat.ParseRepeatPattern(format, i, ch);
                        if (tokenLen > DateTimeFormat.MaxSecondsFractionDigits || seenFF || !ParseExactDigits(ref tokenizer, tokenLen, tokenLen, out leadingZeroes, out ff))
                        {
                            return result.SetInvalidStringFailure();
                        }
                        seenFF = true;
                        break;

                    case 'F':
                        tokenLen = DateTimeFormat.ParseRepeatPattern(format, i, ch);
                        if (tokenLen > DateTimeFormat.MaxSecondsFractionDigits || seenFF)
                        {
                            return result.SetInvalidStringFailure();
                        }
                        ParseExactDigits(ref tokenizer, tokenLen, tokenLen, out leadingZeroes, out ff);
                        seenFF = true;
                        break;

                    case 'd':
                        tokenLen = DateTimeFormat.ParseRepeatPattern(format, i, ch);
                        if (tokenLen > 8 || seenDD || !ParseExactDigits(ref tokenizer, (tokenLen < 2) ? 1 : tokenLen, (tokenLen < 2) ? 8 : tokenLen, out _, out dd))
                        {
                            return result.SetInvalidStringFailure();
                        }
                        seenDD = true;
                        break;

                    case '\'':
                    case '\"':
                        var enquotedString = new ValueStringBuilder(64);
                        if (!DateTimeParse.TryParseQuoteString(format, i, ref enquotedString, out tokenLen))
                        {
                            enquotedString.Dispose();
                            return result.SetBadQuoteFailure(ch);
                        }
                        if (!ParseExactLiteral(ref tokenizer, ref enquotedString))
                        {
                            enquotedString.Dispose();
                            return result.SetInvalidStringFailure();
                        }
                        enquotedString.Dispose();
                        break;

                    case '%':
                        // Optional format character.
                        // For example, format string "%d" will print day
                        // Most of the cases, "%" can be ignored.
                        nextFormatChar = DateTimeFormat.ParseNextChar(format, i);

                        // nextFormatChar will be -1 if we already reach the end of the format string.
                        // Besides, we will not allow "%%" appear in the pattern.
                        if (nextFormatChar >= 0 && nextFormatChar != '%')
                        {
                            tokenLen = 1; // skip the '%' and process the format character
                            break;
                        }
                        else
                        {
                            // This means that '%' is at the end of the format string or
                            // "%%" appears in the format string.
                            return result.SetInvalidStringFailure();
                        }

                    case '\\':
                        // Escaped character.  Can be used to insert character into the format string.
                        // For example, "\d" will insert the character 'd' into the string.
                        //
                        nextFormatChar = DateTimeFormat.ParseNextChar(format, i);
                        if (nextFormatChar >= 0 && tokenizer.NextChar() == (char)nextFormatChar)
                        {
                            tokenLen = 2;
                        }
                        else
                        {
                            // This means that '\' is at the end of the format string or the literal match failed.
                            return result.SetInvalidStringFailure();
                        }
                        break;

                    default:
                        return result.SetInvalidStringFailure();
                }

                i += tokenLen;
            }


            if (!tokenizer.EOL)
            {
                // the custom format didn't consume the entire input
                return result.SetBadTimeSpanFailure();
            }

            bool positive = (styles & TimeSpanStyles.AssumeNegative) == 0;
            if (TryTimeToTicks(positive, new TimeSpanToken(dd),
                                         new TimeSpanToken(hh),
                                         new TimeSpanToken(mm),
                                         new TimeSpanToken(ss),
                                         new TimeSpanToken(ff, leadingZeroes),
                                         out long ticks))
            {
                if (!positive)
                {
                    ticks = -ticks;
                }

                result.parsedTimeSpan = new TimeSpan(ticks);
                return true;
            }
            else
            {
                return result.SetOverflowFailure();
            }
        }

        private static bool ParseExactDigits(ref TimeSpanTokenizer tokenizer, int minDigitLength, out int result)
        {
            int maxDigitLength = (minDigitLength == 1) ? 2 : minDigitLength;
            return ParseExactDigits(ref tokenizer, minDigitLength, maxDigitLength, out _, out result);
        }

        private static bool ParseExactDigits(ref TimeSpanTokenizer tokenizer, int minDigitLength, int maxDigitLength, out int zeroes, out int result)
        {
            int tmpResult = 0, tmpZeroes = 0;

            int tokenLength = 0;
            while (tokenLength < maxDigitLength)
            {
                char ch = tokenizer.NextChar();
                if (!char.IsAsciiDigit(ch))
                {
                    tokenizer.BackOne();
                    break;
                }

                tmpResult = tmpResult * 10 + (ch - '0');
                if (tmpResult == 0) tmpZeroes++;
                tokenLength++;
            }

            zeroes = tmpZeroes;
            result = tmpResult;
            return tokenLength >= minDigitLength;
        }

        private static bool ParseExactLiteral(ref TimeSpanTokenizer tokenizer, ref ValueStringBuilder enquotedString)
        {
            ReadOnlySpan<char> span = enquotedString.AsSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] != tokenizer.NextChar())
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Parses the "c" (constant) format.  This code is 100% identical to the non-globalized v1.0-v3.5 TimeSpan.Parse() routine
        /// and exists for performance/appcompat with legacy callers who cannot move onto the globalized Parse overloads.
        /// </summary>
        private static bool TryParseTimeSpanConstant(ReadOnlySpan<char> input, ref TimeSpanResult result) =>
            default(StringParser).TryParse(input, ref result);

        private ref struct StringParser
        {
            private ReadOnlySpan<char> _str;
            private char _ch;
            private int _pos;

            internal void NextChar()
            {
                ReadOnlySpan<char> str = _str;

                if (_pos < str.Length)
                {
                    _pos++;
                }

                int pos = _pos;
                _ch = (uint)pos < (uint)str.Length ?
                    str[pos] :
                    (char)0;
            }

            internal char NextNonDigit()
            {
                int i = _str.Slice(_pos).IndexOfAnyExceptInRange('0', '9');
                return i < 0 ? (char)0 : _str[_pos + i];
            }

            internal bool TryParse(ReadOnlySpan<char> input, ref TimeSpanResult result)
            {
                result.parsedTimeSpan = default;

                _str = input;
                _pos = -1;
                NextChar();
                SkipBlanks();

                bool negative = false;
                if (_ch == '-')
                {
                    negative = true;
                    NextChar();
                }

                long time;
                if (NextNonDigit() == ':')
                {
                    if (!ParseTime(out time, ref result))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!ParseInt((int)(0x7FFFFFFFFFFFFFFFL / TimeSpan.TicksPerDay), out int days, ref result))
                    {
                        return false;
                    }

                    time = days * TimeSpan.TicksPerDay;

                    if (_ch == '.')
                    {
                        NextChar();
                        if (!ParseTime(out long remainingTime, ref result))
                        {
                            return false;
                        }
                        time += remainingTime;
                    }
                }

                if (negative)
                {
                    time = -time;
                    // Allow -0 as well
                    if (time > 0)
                    {
                        return result.SetOverflowFailure();
                    }
                }
                else
                {
                    if (time < 0)
                    {
                        return result.SetOverflowFailure();
                    }
                }

                SkipBlanks();

                if (_pos < _str.Length)
                {
                    return result.SetBadTimeSpanFailure();
                }

                result.parsedTimeSpan = new TimeSpan(time);
                return true;
            }

            internal bool ParseInt(int max, out int i, ref TimeSpanResult result)
            {
                i = 0;
                int p = _pos;
                while (char.IsAsciiDigit(_ch))
                {
                    if ((i & 0xF0000000) != 0)
                    {
                        return result.SetOverflowFailure();
                    }

                    i = i * 10 + _ch - '0';
                    if (i < 0)
                    {
                        return result.SetOverflowFailure();
                    }

                    NextChar();
                }

                if (p == _pos)
                {
                    return result.SetBadTimeSpanFailure();
                }

                if (i > max)
                {
                    return result.SetOverflowFailure();
                }

                return true;
            }

            internal bool ParseTime(out long time, ref TimeSpanResult result)
            {
                time = 0;

                if (!ParseInt(23, out int unit, ref result))
                {
                    return false;
                }

                time = unit * TimeSpan.TicksPerHour;
                if (_ch != ':')
                {
                    return result.SetBadTimeSpanFailure();
                }

                NextChar();

                if (!ParseInt(59, out unit, ref result))
                {
                    return false;
                }

                time += unit * TimeSpan.TicksPerMinute;

                if (_ch == ':')
                {
                    NextChar();

                    // allow seconds with the leading zero
                    if (_ch != '.')
                    {
                        if (!ParseInt(59, out unit, ref result))
                        {
                            return false;
                        }
                        time += unit * TimeSpan.TicksPerSecond;
                    }

                    if (_ch == '.')
                    {
                        NextChar();
                        int f = (int)TimeSpan.TicksPerSecond;
                        while (f > 1 && char.IsAsciiDigit(_ch))
                        {
                            f /= 10;
                            time += (_ch - '0') * f;
                            NextChar();
                        }
                    }
                }

                return true;
            }

            internal void SkipBlanks()
            {
                while (_ch == ' ' || _ch == '\t') NextChar();
            }
        }

        /// <summary>Common private ParseExactMultiple method called by both ParseExactMultiple and TryParseExactMultiple.</summary>
        private static bool TryParseExactMultipleTimeSpan(ReadOnlySpan<char> input, string?[]? formats, IFormatProvider? formatProvider, TimeSpanStyles styles, ref TimeSpanResult result)
        {
            if (formats == null)
            {
                return result.SetArgumentNullFailure(nameof(formats));
            }

            if (input.Length == 0)
            {
                return result.SetBadTimeSpanFailure();
            }

            if (formats.Length == 0)
            {
                return result.SetNoFormatSpecifierFailure();
            }

            // Do a loop through the provided formats and see if we can parse successfully in
            // one of the formats.
            for (int i = 0; i < formats.Length; i++)
            {
                string? format = formats[i];

                if (string.IsNullOrEmpty(format))
                {
                    return result.SetBadFormatSpecifierFailure();
                }

                // Create a new non-throwing result each time to ensure the runs are independent.
                TimeSpanResult innerResult = new TimeSpanResult(throwOnFailure: false, originalTimeSpanString: input);

                if (TryParseExactTimeSpan(input, format, formatProvider, styles, ref innerResult))
                {
                    result.parsedTimeSpan = innerResult.parsedTimeSpan;
                    return true;
                }
            }

            return result.SetBadTimeSpanFailure();
        }
    }

    /// <summary>System.TimeSpan's Parse/TryParse/ParseExact/TryParseExact (dotnet/runtime's TimeSpan.cs).</summary>
    [ShadowMembers("System.TimeSpan")]
    internal static class TimeSpanParseMembers
    {
        private static void ValidateStyles(TimeSpanStyles style)
        {
            if (style is not TimeSpanStyles.None and not TimeSpanStyles.AssumeNegative)
                throw new ArgumentException("An undefined TimeSpanStyles value is being used.", "styles");
        }

        public static TimeSpan Parse(string s)
        {
            /* Constructs a TimeSpan from a string.  Leading and trailing white space characters are allowed. */
            ArgumentNullException.ThrowIfNull(s, "input");
            return TimeSpanParse.Parse(s, null);
        }
        public static TimeSpan Parse(string input, IFormatProvider? formatProvider)
        {
            if (input is null)
            {
                throw new ArgumentNullException("input");
            }
            return TimeSpanParse.Parse(input, formatProvider);
        }
        public static TimeSpan Parse(ReadOnlySpan<char> input, IFormatProvider? formatProvider = null) => TimeSpanParse.Parse(input, formatProvider);
        public static TimeSpan ParseExact(string input, string format, IFormatProvider? formatProvider)
        {
            ArgumentNullException.ThrowIfNull(input, "input");
            ArgumentNullException.ThrowIfNull(format, "format");
            return TimeSpanParse.ParseExact(input, format, formatProvider, TimeSpanStyles.None);
        }
        public static TimeSpan ParseExact(string input, string[] formats, IFormatProvider? formatProvider)
        {
            if (input is null)
            {
                throw new ArgumentNullException("input");
            }
            return TimeSpanParse.ParseExactMultiple(input, formats, formatProvider, TimeSpanStyles.None);
        }
        public static TimeSpan ParseExact(string input, string format, IFormatProvider? formatProvider, TimeSpanStyles styles)
        {
            ValidateStyles(styles);
            ArgumentNullException.ThrowIfNull(input, "input");
            ArgumentNullException.ThrowIfNull(format, "format");
            return TimeSpanParse.ParseExact(input, format, formatProvider, styles);
        }

        public static TimeSpan ParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format, IFormatProvider? formatProvider, TimeSpanStyles styles = TimeSpanStyles.None)
        {
            ValidateStyles(styles);
            return TimeSpanParse.ParseExact(input, format, formatProvider, styles);
        }
        public static TimeSpan ParseExact(string input, string[] formats, IFormatProvider? formatProvider, TimeSpanStyles styles)
        {
            ValidateStyles(styles);
            ArgumentNullException.ThrowIfNull(input, "input");
            return TimeSpanParse.ParseExactMultiple(input, formats, formatProvider, styles);
        }
        public static TimeSpan ParseExact(ReadOnlySpan<char> input, string[] formats, IFormatProvider? formatProvider, TimeSpanStyles styles = TimeSpanStyles.None)
        {
            ValidateStyles(styles);
            return TimeSpanParse.ParseExactMultiple(input, formats, formatProvider, styles);
        }
        public static bool TryParse(string? s, out TimeSpan result)
        {
            if (s is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParse(s, null, out result);
        }
        public static bool TryParse(ReadOnlySpan<char> s, out TimeSpan result) => TimeSpanParse.TryParse(s, null, out result);

        public static bool TryParse(string? input, IFormatProvider? formatProvider, out TimeSpan result)
        {
            if (input is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParse(input, formatProvider, out result);
        }
        public static bool TryParse(ReadOnlySpan<char> input, IFormatProvider? formatProvider, out TimeSpan result) => TimeSpanParse.TryParse(input, formatProvider, out result);
        public static bool TryParseExact(string? input, string? format, IFormatProvider? formatProvider, out TimeSpan result)
        {
            if (input is null || format is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParseExact(input, format, formatProvider, TimeSpanStyles.None, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format, IFormatProvider? formatProvider, out TimeSpan result)
            => TimeSpanParse.TryParseExact(input, format, formatProvider, TimeSpanStyles.None, out result);

        public static bool TryParseExact(string? input, string?[]? formats, IFormatProvider? formatProvider, out TimeSpan result)
        {
            if (input is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParseExactMultiple(input, formats, formatProvider, TimeSpanStyles.None, out result);
        }
        public static bool TryParseExact(ReadOnlySpan<char> input, string?[]? formats, IFormatProvider? formatProvider, out TimeSpan result)
            => TimeSpanParse.TryParseExactMultiple(input, formats, formatProvider, TimeSpanStyles.None, out result);

        public static bool TryParseExact(string? input, string? format, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);

            if (input is null || format is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParseExact(input, format, formatProvider, styles, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);
            return TimeSpanParse.TryParseExact(input, format, formatProvider, styles, out result);
        }
        public static bool TryParseExact(string? input, string?[]? formats, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);

            if (input is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParseExactMultiple(input, formats, formatProvider, styles, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> input, string?[]? formats, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);
            return TimeSpanParse.TryParseExactMultiple(input, formats, formatProvider, styles, out result);
        }
    }
}
