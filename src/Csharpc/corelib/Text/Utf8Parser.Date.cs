// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/Buffers/
// Text/Utf8Parser/Utf8Parser.Date*.cs; see THIRD-PARTY-NOTICES.md. Utf8Parser.TryParse for DateTime and
// DateTimeOffset in the 'G', 'R', 'l' and 'O' standard formats (and the default).
#nullable disable
using System;
using System.Buffers;

namespace CsharpcCoreLib
{
    // (The integer overloads: corelib/Utf8Parser.cs.)
    [ShadowMembers("System.Buffers.Text.Utf8Parser")]
    internal static class Utf8ParserDateMembers
    {
        private static readonly int[] DaysToMonth365 = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };
        private static readonly int[] DaysToMonth366 = { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 };

        private static bool ThrowFormat<T>(out T value, out int bytesConsumed) => throw new FormatException("Format specifier was invalid.");

        // ---- Utf8Parser.Date.cs ----
        /// <summary>
        /// Parses a DateTime at the start of a Utf8 string.
        /// </summary>
        /// <param name="source">The Utf8 string to parse</param>
        /// <param name="value">Receives the parsed value</param>
        /// <param name="bytesConsumed">On a successful parse, receives the length in bytes of the substring that was parsed </param>
        /// <param name="standardFormat">Expected format of the Utf8 string</param>
        /// <returns>
        /// true for success. "bytesConsumed" contains the length in bytes of the substring that was parsed.
        /// false if the string was not syntactically valid or an overflow or underflow occurred. "bytesConsumed" is set to 0.
        /// </returns>
        /// <remarks>
        /// Formats supported:
        ///     default       05/25/2017 10:30:15 -08:00
        ///     G             05/25/2017 10:30:15
        ///     R             Tue, 03 Jan 2017 08:08:05 GMT       (RFC 1123)
        ///     l             tue, 03 jan 2017 08:08:05 gmt       (Lowercase RFC 1123)
        ///     O             2017-06-12T05:30:45.7680000-07:00   (Round-trippable)
        /// </remarks>
        /// <exceptions>
        /// <cref>System.FormatException</cref> if the format is not valid for this data type.
        /// </exceptions>
        public static bool TryParse(ReadOnlySpan<byte> source, out DateTime value, out int bytesConsumed, char standardFormat = default)
        {
            switch (standardFormat)
            {
                case 'R':
                    {
                        if (!TryParseDateTimeOffsetR(source, NoFlipCase, out DateTimeOffset dateTimeOffset, out bytesConsumed))
                        {
                            value = default;
                            return false;
                        }
                        value = dateTimeOffset.DateTime;  // (returns a DateTimeKind.Unspecified to match DateTime.ParseExact(). Maybe better to return UtcDateTime instead?)
                        return true;
                    }

                case 'l':
                    {
                        if (!TryParseDateTimeOffsetR(source, FlipCase, out DateTimeOffset dateTimeOffset, out bytesConsumed))
                        {
                            value = default;
                            return false;
                        }
                        value = dateTimeOffset.DateTime;  // (returns a DateTimeKind.Unspecified to match DateTime.ParseExact(). Maybe better to return UtcDateTime instead?)
                        return true;
                    }

                case 'O':
                    {
                        // Emulates DateTime.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                        // In particular, the formatted string "encodes" the DateTimeKind according to the following table:
                        //
                        //         2017-06-12T05:30:45.7680000       - Unspecified
                        //         2017-06-12T05:30:45.7680000+00:00 - Local
                        //         2017-06-12T05:30:45.7680000Z      - Utc

                        if (!TryParseDateTimeOffsetO(source, out DateTimeOffset dateTimeOffset, out bytesConsumed, out DateTimeKind kind))
                        {
                            value = default;
                            bytesConsumed = 0;
                            return false;
                        }

                        switch (kind)
                        {
                            case DateTimeKind.Local:
                                value = dateTimeOffset.LocalDateTime;
                                break;
                            case DateTimeKind.Utc:
                                value = dateTimeOffset.UtcDateTime;
                                break;
                            default:
                                value = dateTimeOffset.DateTime;
                                break;
                        }

                        return true;
                    }

                case default(char):
                case 'G':
                    return TryParseDateTimeG(source, out value, out _, out bytesConsumed);

                default:
                    return ThrowFormat(out value, out bytesConsumed);
            }
        }

        /// <summary>
        /// Parses a DateTimeOffset at the start of a Utf8 string.
        /// </summary>
        /// <param name="source">The Utf8 string to parse</param>
        /// <param name="value">Receives the parsed value</param>
        /// <param name="bytesConsumed">On a successful parse, receives the length in bytes of the substring that was parsed </param>
        /// <param name="standardFormat">Expected format of the Utf8 string</param>
        /// <returns>
        /// true for success. "bytesConsumed" contains the length in bytes of the substring that was parsed.
        /// false if the string was not syntactically valid or an overflow or underflow occurred. "bytesConsumed" is set to 0.
        /// </returns>
        /// <remarks>
        /// Formats supported:
        ///     G  (default)  05/25/2017 10:30:15
        ///     R             Tue, 03 Jan 2017 08:08:05 GMT       (RFC 1123)
        ///     l             tue, 03 jan 2017 08:08:05 gmt       (Lowercase RFC 1123)
        ///     O             2017-06-12T05:30:45.7680000-07:00   (Round-trippable)
        /// </remarks>
        /// <exceptions>
        /// <cref>System.FormatException</cref> if the format is not valid for this data type.
        /// </exceptions>
        public static bool TryParse(ReadOnlySpan<byte> source, out DateTimeOffset value, out int bytesConsumed, char standardFormat = default)
        {
            return standardFormat switch
            {
                'R' => TryParseDateTimeOffsetR(source, NoFlipCase, out value, out bytesConsumed),
                'l' => TryParseDateTimeOffsetR(source, FlipCase, out value, out bytesConsumed),
                'O' => TryParseDateTimeOffsetO(source, out value, out bytesConsumed, out _),
                default(char) => TryParseDateTimeOffsetDefault(source, out value, out bytesConsumed),
                'G' => TryParseDateTimeG(source, out DateTime _, out value, out bytesConsumed),
                _ => ThrowFormat(out value, out bytesConsumed),
            };
        }

        private const uint FlipCase = 0x00000020u;  // XOR mask to flip the case of a letter.
        private const uint NoFlipCase = 0x00000000u;
    

        // ---- Utf8Parser.Date.Default.cs ----
        //
        // ToString() format for DateTimeOffset. Does not have a corresponding format symbol but it
        // is the "G" format postpended with the UTC offset.
        //
        // 01234567890123456789012345
        // --------------------------
        // 05/25/2017 10:30:15 -08:00
        //
        private static bool TryParseDateTimeOffsetDefault(ReadOnlySpan<byte> source, out DateTimeOffset value, out int bytesConsumed)
        {
            if (source.Length < 26)
            {
                bytesConsumed = 0;
                value = default;
                return false;
            }

            if (!TryParseDateTimeG(source, out DateTime dateTime, out _, out _))
            {
                bytesConsumed = 0;
                value = default;
                return false;
            }

            if (source[19] != (byte)' ')
            {
                bytesConsumed = 0;
                value = default;
                return false;
            }

            byte sign = source[20];
            if (sign != (byte)'+' && sign != (byte)'-')
            {
                bytesConsumed = 0;
                value = default;
                return false;
            }

            int offsetHours;
            {
                uint digit1 = source[21] - 48u; // '0'
                uint digit2 = source[22] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    value = default;
                    return false;
                }

                offsetHours = (int)(digit1 * 10 + digit2);
            }

            if (source[23] != (byte)':')
            {
                bytesConsumed = 0;
                value = default;
                return false;
            }

            int offsetMinutes;
            {
                uint digit1 = source[24] - 48u; // '0'
                uint digit2 = source[25] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    value = default;
                    return false;
                }

                offsetMinutes = (int)(digit1 * 10 + digit2);
            }

            if (!TryCreateDateTimeOffset(dateTime: dateTime, offsetNegative: sign == (byte)'-', offsetHours: offsetHours, offsetMinutes: offsetMinutes, out value))
            {
                bytesConsumed = 0;
                value = default;
                return false;
            }

            bytesConsumed = 26;
            return true;
        }
    

        // ---- Utf8Parser.Date.G.cs ----
        //
        // 'G' format for DateTime.
        //
        //   0123456789012345678
        //   ---------------------------------
        //   05/25/2017 10:30:15
        //
        private static bool TryParseDateTimeG(ReadOnlySpan<byte> source, out DateTime value, out DateTimeOffset valueAsOffset, out int bytesConsumed)
        {
            if (source.Length < 19)
            {
                bytesConsumed = 0;
                value = default;
                valueAsOffset = default;
                return false;
            }

            int month;
            {
                uint digit1 = source[0] - 48u; // '0'
                uint digit2 = source[1] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    value = default;
                    valueAsOffset = default;
                    return false;
                }

                month = (int)(digit1 * 10 + digit2);
            }

            if (source[2] != (byte)'/')
            {
                bytesConsumed = 0;
                value = default;
                valueAsOffset = default;
                return false;
            }

            int day;
            {
                uint digit1 = source[3] - 48u; // '0'
                uint digit2 = source[4] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    value = default;
                    valueAsOffset = default;
                    return false;
                }

                day = (int)(digit1 * 10 + digit2);
            }

            if (source[5] != (byte)'/')
            {
                bytesConsumed = 0;
                value = default;
                valueAsOffset = default;
                return false;
            }

            int year;
            {
                uint digit1 = source[6] - 48u; // '0'
                uint digit2 = source[7] - 48u; // '0'
                uint digit3 = source[8] - 48u; // '0'
                uint digit4 = source[9] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9 || digit3 > 9 || digit4 > 9)
                {
                    bytesConsumed = 0;
                    value = default;
                    valueAsOffset = default;
                    return false;
                }

                year = (int)(digit1 * 1000 + digit2 * 100 + digit3 * 10 + digit4);
            }

            if (source[10] != (byte)' ')
            {
                bytesConsumed = 0;
                value = default;
                valueAsOffset = default;
                return false;
            }

            int hour;
            {
                uint digit1 = source[11] - 48u; // '0'
                uint digit2 = source[12] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    value = default;
                    valueAsOffset = default;
                    return false;
                }

                hour = (int)(digit1 * 10 + digit2);
            }

            if (source[13] != (byte)':')
            {
                bytesConsumed = 0;
                value = default;
                valueAsOffset = default;
                return false;
            }

            int minute;
            {
                uint digit1 = source[14] - 48u; // '0'
                uint digit2 = source[15] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    value = default;
                    valueAsOffset = default;
                    return false;
                }

                minute = (int)(digit1 * 10 + digit2);
            }

            if (source[16] != (byte)':')
            {
                bytesConsumed = 0;
                value = default;
                valueAsOffset = default;
                return false;
            }

            int second;
            {
                uint digit1 = source[17] - 48u; // '0'
                uint digit2 = source[18] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    value = default;
                    valueAsOffset = default;
                    return false;
                }

                second = (int)(digit1 * 10 + digit2);
            }

            if (!TryCreateDateTimeOffsetInterpretingDataAsLocalTime(year: year, month: month, day: day, hour: hour, minute: minute, second: second, fraction: 0, out valueAsOffset))
            {
                bytesConsumed = 0;
                value = default;
                valueAsOffset = default;
                return false;
            }

            bytesConsumed = 19;
            value = valueAsOffset.DateTime;
            return true;
        }
    

        // ---- Utf8Parser.Date.O.cs ----
        //
        // Roundtrippable format. One of
        //
        //   012345678901234567890123456789012
        //   ---------------------------------
        //   2017-06-12T05:30:45.7680000-07:00
        //   2017-06-12T05:30:45.7680000Z           (Z is short for "+00:00" but also distinguishes DateTimeKind.Utc from DateTimeKind.Local)
        //   2017-06-12T05:30:45.7680000            (interpreted as local time wrt to current time zone)
        //
        private static bool TryParseDateTimeOffsetO(ReadOnlySpan<byte> source, out DateTimeOffset value, out int bytesConsumed, out DateTimeKind kind)
        {
            if (source.Length < 27)
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            int year;
            {
                uint digit1 = source[0] - 48u; // '0'
                uint digit2 = source[1] - 48u; // '0'
                uint digit3 = source[2] - 48u; // '0'
                uint digit4 = source[3] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9 || digit3 > 9 || digit4 > 9)
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                year = (int)(digit1 * 1000 + digit2 * 100 + digit3 * 10 + digit4);
            }

            if (source[4] != (byte)'-')
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            int month;
            {
                uint digit1 = source[5] - 48u; // '0'
                uint digit2 = source[6] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                month = (int)(digit1 * 10 + digit2);
            }

            if (source[7] != (byte)'-')
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            int day;
            {
                uint digit1 = source[8] - 48u; // '0'
                uint digit2 = source[9] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                day = (int)(digit1 * 10 + digit2);
            }

            if (source[10] != 'T')
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            int hour;
            {
                uint digit1 = source[11] - 48u; // '0'
                uint digit2 = source[12] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                hour = (int)(digit1 * 10 + digit2);
            }

            if (source[13] != (byte)':')
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            int minute;
            {
                uint digit1 = source[14] - 48u; // '0'
                uint digit2 = source[15] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                minute = (int)(digit1 * 10 + digit2);
            }

            if (source[16] != (byte)':')
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            int second;
            {
                uint digit1 = source[17] - 48u; // '0'
                uint digit2 = source[18] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                second = (int)(digit1 * 10 + digit2);
            }

            if (source[19] != (byte)'.')
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            int fraction;
            {
                uint digit1 = source[20] - 48u; // '0'
                uint digit2 = source[21] - 48u; // '0'
                uint digit3 = source[22] - 48u; // '0'
                uint digit4 = source[23] - 48u; // '0'
                uint digit5 = source[24] - 48u; // '0'
                uint digit6 = source[25] - 48u; // '0'
                uint digit7 = source[26] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9 || digit3 > 9 || digit4 > 9 || digit5 > 9 || digit6 > 9 || digit7 > 9)
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                fraction = (int)(digit1 * 1000000 + digit2 * 100000 + digit3 * 10000 + digit4 * 1000 + digit5 * 100 + digit6 * 10 + digit7);
            }

            byte offsetChar = (source.Length <= 27) ? default : source[27];
            if (offsetChar != 'Z' && offsetChar != (byte)'+' && offsetChar != (byte)'-')
            {
                if (!TryCreateDateTimeOffsetInterpretingDataAsLocalTime(year: year, month: month, day: day, hour: hour, minute: minute, second: second, fraction: fraction, out value))
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }
                bytesConsumed = 27;
                kind = DateTimeKind.Unspecified;
                return true;
            }

            if (offsetChar == 'Z')
            {
                // Same as specifying an offset of "+00:00", except that DateTime's Kind gets set to UTC rather than Local
                if (!TryCreateDateTimeOffset(year: year, month: month, day: day, hour: hour, minute: minute, second: second, fraction: fraction, offsetNegative: false, offsetHours: 0, offsetMinutes: 0, out value))
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                bytesConsumed = 28;
                kind = DateTimeKind.Utc;
                return true;
            }

            if (source.Length < 33)
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            int offsetHours;
            {
                uint digit1 = source[28] - 48u; // '0'
                uint digit2 = source[29] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                offsetHours = (int)(digit1 * 10 + digit2);
            }

            if (source[30] != (byte)':')
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            int offsetMinutes;
            {
                uint digit1 = source[31] - 48u; // '0'
                uint digit2 = source[32] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    value = default;
                    bytesConsumed = 0;
                    kind = default;
                    return false;
                }

                offsetMinutes = (int)(digit1 * 10 + digit2);
            }

            if (!TryCreateDateTimeOffset(year: year, month: month, day: day, hour: hour, minute: minute, second: second, fraction: fraction, offsetNegative: offsetChar == (byte)'-', offsetHours: offsetHours, offsetMinutes: offsetMinutes, out value))
            {
                value = default;
                bytesConsumed = 0;
                kind = default;
                return false;
            }

            bytesConsumed = 33;
            kind = DateTimeKind.Local;
            return true;
        }
    

        // ---- Utf8Parser.Date.R.cs ----
        //
        // Parse an RFC1123 date string.
        //
        // 01234567890123456789012345678
        // -----------------------------
        // Tue, 03 Jan 2017 08:08:05 GMT
        //
        private static bool TryParseDateTimeOffsetR(ReadOnlySpan<byte> source, uint caseFlipXorMask, out DateTimeOffset dateTimeOffset, out int bytesConsumed)
        {
            if (source.Length < 29)
            {
                bytesConsumed = 0;
                dateTimeOffset = default;
                return false;
            }

            DayOfWeek dayOfWeek;
            {
                uint dow0 = source[0] ^ caseFlipXorMask;
                uint dow1 = source[1];
                uint dow2 = source[2];
                uint comma = source[3];
                uint dowString = (dow0 << 24) | (dow1 << 16) | (dow2 << 8) | comma;
                switch (dowString)
                {
                    case 0x53756E2c /* 'Sun,' */: dayOfWeek = DayOfWeek.Sunday; break;
                    case 0x4d6f6e2c /* 'Mon,' */: dayOfWeek = DayOfWeek.Monday; break;
                    case 0x5475652c /* 'Tue,' */: dayOfWeek = DayOfWeek.Tuesday; break;
                    case 0x5765642c /* 'Wed,' */: dayOfWeek = DayOfWeek.Wednesday; break;
                    case 0x5468752c /* 'Thu,' */: dayOfWeek = DayOfWeek.Thursday; break;
                    case 0x4672692c /* 'Fri,' */: dayOfWeek = DayOfWeek.Friday; break;
                    case 0x5361742c /* 'Sat,' */: dayOfWeek = DayOfWeek.Saturday; break;
                    default:
                        bytesConsumed = 0;
                        dateTimeOffset = default;
                        return false;
                }
            }

            if (source[4] != (byte)' ')
            {
                bytesConsumed = 0;
                dateTimeOffset = default;
                return false;
            }

            int day;
            {
                uint digit1 = source[5] - 48u; // '0'
                uint digit2 = source[6] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    dateTimeOffset = default;
                    return false;
                }

                day = (int)(digit1 * 10 + digit2);
            }

            if (source[7] != (byte)' ')
            {
                bytesConsumed = 0;
                dateTimeOffset = default;
                return false;
            }

            int month;
            {
                uint mon0 = source[8] ^ caseFlipXorMask;
                uint mon1 = source[9];
                uint mon2 = source[10];
                uint space = source[11];
                uint monthString = (mon0 << 24) | (mon1 << 16) | (mon2 << 8) | space;
                switch (monthString)
                {
                    case 0x4a616e20: /* 'Jan ' */ month = 1; break;
                    case 0x46656220: /* 'Feb ' */ month = 2; break;
                    case 0x4d617220: /* 'Mar ' */ month = 3; break;
                    case 0x41707220: /* 'Apr ' */ month = 4; break;
                    case 0x4d617920: /* 'May ' */ month = 5; break;
                    case 0x4a756e20: /* 'Jun ' */ month = 6; break;
                    case 0x4a756c20: /* 'Jul ' */ month = 7; break;
                    case 0x41756720: /* 'Aug ' */ month = 8; break;
                    case 0x53657020: /* 'Sep ' */ month = 9; break;
                    case 0x4f637420: /* 'Oct ' */ month = 10; break;
                    case 0x4e6f7620: /* 'Nov ' */ month = 11; break;
                    case 0x44656320: /* 'Dec ' */ month = 12; break;
                    default:
                        bytesConsumed = 0;
                        dateTimeOffset = default;
                        return false;
                }
            }

            int year;
            {
                uint digit1 = source[12] - 48u; // '0'
                uint digit2 = source[13] - 48u; // '0'
                uint digit3 = source[14] - 48u; // '0'
                uint digit4 = source[15] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9 || digit3 > 9 || digit4 > 9)
                {
                    bytesConsumed = 0;
                    dateTimeOffset = default;
                    return false;
                }

                year = (int)(digit1 * 1000 + digit2 * 100 + digit3 * 10 + digit4);
            }

            if (source[16] != (byte)' ')
            {
                bytesConsumed = 0;
                dateTimeOffset = default;
                return false;
            }

            int hour;
            {
                uint digit1 = source[17] - 48u; // '0'
                uint digit2 = source[18] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    dateTimeOffset = default;
                    return false;
                }

                hour = (int)(digit1 * 10 + digit2);
            }

            if (source[19] != (byte)':')
            {
                bytesConsumed = 0;
                dateTimeOffset = default;
                return false;
            }

            int minute;
            {
                uint digit1 = source[20] - 48u; // '0'
                uint digit2 = source[21] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    dateTimeOffset = default;
                    return false;
                }

                minute = (int)(digit1 * 10 + digit2);
            }

            if (source[22] != (byte)':')
            {
                bytesConsumed = 0;
                dateTimeOffset = default;
                return false;
            }

            int second;
            {
                uint digit1 = source[23] - 48u; // '0'
                uint digit2 = source[24] - 48u; // '0'

                if (digit1 > 9 || digit2 > 9)
                {
                    bytesConsumed = 0;
                    dateTimeOffset = default;
                    return false;
                }

                second = (int)(digit1 * 10 + digit2);
            }

            {
                uint space = source[25];
                uint g = source[26] ^ caseFlipXorMask;
                uint m = source[27] ^ caseFlipXorMask;
                uint t = source[28] ^ caseFlipXorMask;
                uint gmtString = (space << 24) | (g << 16) | (m << 8) | t;
                if (gmtString != 0x20474d54 /* ' GMT' */)
                {
                    bytesConsumed = 0;
                    dateTimeOffset = default;
                    return false;
                }
            }

            if (!TryCreateDateTimeOffset(year: year, month: month, day: day, hour: hour, minute: minute, second: second, fraction: 0, offsetNegative: false, offsetHours: 0, offsetMinutes: 0, out dateTimeOffset))
            {
                bytesConsumed = 0;
                dateTimeOffset = default;
                return false;
            }

            if (dayOfWeek != dateTimeOffset.DayOfWeek)
            {
                // If we got here, the day of week did not match the actual date.
                bytesConsumed = 0;
                dateTimeOffset = default;
                return false;
            }

            bytesConsumed = 29;
            return true;
        }
    

        // ---- Utf8Parser.Date.Helpers.cs ----
        /// <summary>
        /// Overflow-safe DateTimeOffset factory.
        /// </summary>
        private static bool TryCreateDateTimeOffset(DateTime dateTime, bool offsetNegative, int offsetHours, int offsetMinutes, out DateTimeOffset value)
        {
            if (((uint)offsetHours) > 14)
            {
                value = default;
                return false;
            }

            if (((uint)offsetMinutes) > 59)
            {
                value = default;
                return false;
            }

            if (offsetHours == 14 && offsetMinutes != 0)
            {
                value = default;
                return false;
            }

            long offsetTicks = (((long)offsetHours) * 3600 + ((long)offsetMinutes) * 60) * TimeSpan.TicksPerSecond;
            if (offsetNegative)
            {
                offsetTicks = -offsetTicks;
            }

            try
            {
                value = new DateTimeOffset(ticks: dateTime.Ticks, offset: new TimeSpan(ticks: offsetTicks));
            }
            catch (ArgumentOutOfRangeException)
            {
                // If we got here, the combination of the DateTime + UTC offset strayed outside the 1..9999 year range. This case seems rare enough
                // that it's better to catch the exception rather than replicate DateTime's range checking (which it's going to do anyway.)
                value = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Overflow-safe DateTimeOffset factory.
        /// </summary>
        private static bool TryCreateDateTimeOffset(int year, int month, int day, int hour, int minute, int second, int fraction, bool offsetNegative, int offsetHours, int offsetMinutes, out DateTimeOffset value)
        {
            if (!TryCreateDateTime(year: year, month: month, day: day, hour: hour, minute: minute, second: second, fraction: fraction, kind: DateTimeKind.Unspecified, out DateTime dateTime))
            {
                value = default;
                return false;
            }

            if (!TryCreateDateTimeOffset(dateTime: dateTime, offsetNegative: offsetNegative, offsetHours: offsetHours, offsetMinutes: offsetMinutes, out value))
            {
                value = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Overflow-safe DateTimeOffset/Local time conversion factory.
        /// </summary>
        private static bool TryCreateDateTimeOffsetInterpretingDataAsLocalTime(int year, int month, int day, int hour, int minute, int second, int fraction, out DateTimeOffset value)
        {
            if (!TryCreateDateTime(year: year, month: month, day: day, hour: hour, minute: minute, second: second, fraction: fraction, DateTimeKind.Local, out DateTime dateTime))
            {
                value = default;
                return false;
            }

            try
            {
                value = new DateTimeOffset(dateTime);
            }
            catch (ArgumentOutOfRangeException)
            {
                // If we got here, the combination of the DateTime + UTC offset strayed outside the 1..9999 year range. This case seems rare enough
                // that it's better to catch the exception rather than replicate DateTime's range checking (which it's going to do anyway.)

                value = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Overflow-safe DateTime factory.
        /// </summary>
        private static bool TryCreateDateTime(int year, int month, int day, int hour, int minute, int second, int fraction, DateTimeKind kind, out DateTime value)
        {
            if (year == 0)
            {
                value = default;
                return false;
            }
 // All of our callers to date parse the year from fixed 4-digit fields so this value is trusted.

            if ((((uint)month) - 1) >= 12)
            {
                value = default;
                return false;
            }

            uint dayMinusOne = ((uint)day) - 1;
            if (dayMinusOne >= 28 && dayMinusOne >= DateTime.DaysInMonth(year, month))
            {
                value = default;
                return false;
            }

            // Per ISO 8601 (Date and time — Representations for information interchange), 24:00:00 represents end of a calendar day
            // (same instant as next day's 00:00:00), but only when minute, second, and fraction are all zero.
            // We treat it as hour=0 and add one day at the end.
            bool isEndOfDay = false;
            if (hour == 24)
            {
                if (minute != 0 || second != 0 || fraction != 0)
                {
                    value = default;
                    return false;
                }

                hour = 0;
                isEndOfDay = true;
            }

            if (((uint)hour) > 23)
            {
                value = default;
                return false;
            }

            if (((uint)minute) > 59)
            {
                value = default;
                return false;
            }

            if (((uint)second) > 59)
            {
                value = default;
                return false;
            }
 // All of our callers to date parse the fraction from fixed 7-digit fields so this value is trusted.

            ReadOnlySpan<int> days = DateTime.IsLeapYear(year) ? DaysToMonth366 : DaysToMonth365;
            int yearMinusOne = year - 1;
            int totalDays = (yearMinusOne * 365) + (yearMinusOne / 4) - (yearMinusOne / 100) + (yearMinusOne / 400) + days[month - 1] + day - 1;
            long ticks = totalDays * TimeSpan.TicksPerDay;
            int totalSeconds = (hour * 3600) + (minute * 60) + second;
            ticks += totalSeconds * TimeSpan.TicksPerSecond;
            ticks += fraction;

            // If hour was originally 24 (end of day per ISO 8601), add one day to advance to next day's 00:00:00
            if (isEndOfDay)
            {
                ticks += TimeSpan.TicksPerDay;
                if ((ulong)ticks > (ulong)DateTime.MaxValue.Ticks)
                {
                    value = default;
                    return false;
                }
            }

            value = new DateTime(ticks: ticks, kind: kind);
            return true;
        }
    
    }
}
