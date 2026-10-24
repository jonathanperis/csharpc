// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173),
// src/libraries/System.Private.CoreLib/src/System/Globalization/CalendarData.{Windows,Nls}.cs; see
// THIRD-PARTY-NOTICES.md. csharpc: the ICU configuration only (no NLS mode): calendar data comes from
// ICU, and for the user default culture the user's date patterns, two-digit-year limit and calendar
// come from NLS, as in .NET.
#nullable enable annotations
#if CSHARPC_TARGET_WINDOWS

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    internal sealed partial class CalendarData
    {
        private const uint CAL_RETURN_NUMBER = 0x20000000;
        private const uint CAL_SCALNAME = 0x00000002;
        private const uint CAL_ITWODIGITYEARMAX = 0x00000030;

        private const uint LOCALE_ICALENDARTYPE = 0x00001009;
        private const uint LOCALE_SSHORTDATE = 0x0000001F;
        private const uint LOCALE_SLONGDATE = 0x00000020;

        private bool LoadCalendarDataFromSystemCore(string localeName, CalendarId calendarId)
        {
            Debug.Assert(!GlobalizationMode.Invariant);

            // If running using ICU on Windows we should honor user overrides using NLS and the rest from ICU
            bool result = IcuLoadCalendarDataFromSystem(localeName, calendarId);

            if (result && bUseUserOverrides)
            {
                NormalizeCalendarId(ref calendarId, ref localeName);
                result &= CallGetCalendarInfoEx(localeName, calendarId, CAL_ITWODIGITYEARMAX, out this.iTwoDigitYearMax);

                // They want user overrides, see if the user calendar matches the input calendar
                CalendarId userCalendar = (CalendarId)CultureData.GetLocaleInfoExInt(localeName, LOCALE_ICALENDARTYPE);

                // If the calendars were the same, see if the locales were the same
                if (userCalendar == calendarId)
                {
                    string? shortDateOverride = CultureData.ReescapeWin32String(CultureData.GetLocaleInfoEx(localeName, LOCALE_SSHORTDATE));
                    string? longDateOverride = CultureData.ReescapeWin32String(CultureData.GetLocaleInfoEx(localeName, LOCALE_SLONGDATE));
                    InsertOrSwapOverride(shortDateOverride, ref this.saShortDates);
                    InsertOrSwapOverride(longDateOverride, ref this.saLongDates);
                }
            }

            return result;
        }

        private static void InsertOrSwapOverride(string? value, ref string[] destination)
        {
            if (value == null)
                return;

            int i = Array.IndexOf(destination, value);
            if (i >= 0)
            {
                if (i > 0)
                {
                    string tmp = destination[0];
                    destination[0] = value;
                    destination[i] = tmp;
                }

                return;
            }

            string[] newArray = new string[destination.Length + 1];
            newArray[0] = value;
            Array.Copy(destination, 0, newArray, 1, destination.Length);
            destination = newArray;
        }

        private static void NormalizeCalendarId(ref CalendarId calendarId, ref string localeName)
        {
            // Windows doesn't support some calendars right now, so remap those.
            switch (calendarId)
            {
                case CalendarId.JAPANESELUNISOLAR:    // Data looks like Japanese
                    calendarId = CalendarId.JAPAN;
                    break;
                case CalendarId.JULIAN:               // Data looks like gregorian US
                case CalendarId.CHINESELUNISOLAR:     // Algorithmic, so actual data is irrelevant
                case CalendarId.SAKA:                 // reserved to match Office but not implemented in our code, so data is irrelevant
                case CalendarId.LUNAR_ETO_CHN:        // reserved to match Office but not implemented in our code, so data is irrelevant
                case CalendarId.LUNAR_ETO_KOR:        // reserved to match Office but not implemented in our code, so data is irrelevant
                case CalendarId.LUNAR_ETO_ROKUYOU:    // reserved to match Office but not implemented in our code, so data is irrelevant
                case CalendarId.KOREANLUNISOLAR:      // Algorithmic, so actual data is irrelevant
                case CalendarId.TAIWANLUNISOLAR:      // Algorithmic, so actual data is irrelevant
                    calendarId = CalendarId.GREGORIAN_US;
                    break;
            }

            // Special handling for some special calendar due to OS limitation.
            CheckSpecialCalendar(ref calendarId, ref localeName);
        }

        private static void CheckSpecialCalendar(ref CalendarId calendar, ref string localeName)
        {
            // Gregorian-US isn't always available in the OS, however it is the same for all locales
            switch (calendar)
            {
                case CalendarId.GREGORIAN_US:
                    // See if this works
                    if (!CallGetCalendarInfoEx(localeName, calendar, CAL_SCALNAME, out string _))
                    {
                        // Failed, set it to a locale (fa-IR) that always has Gregorian US available in the OS
                        localeName = "fa-IR";

                        // See if that works
                        if (!CallGetCalendarInfoEx(localeName, calendar, CAL_SCALNAME, out string _))
                        {
                            // Failed again, just use en-US with the gregorian calendar
                            localeName = "en-US";
                            calendar = CalendarId.GREGORIAN;
                        }
                    }
                    break;
                case CalendarId.TAIWAN:
                    // Taiwan calendar data is only available in zh-TW localized versions of Windows.
                    if (!CallGetCalendarInfoEx("zh-TW", CalendarId.TAIWAN, CAL_SCALNAME, out string _))
                    {
                        calendar = CalendarId.GREGORIAN;
                    }
                    break;
            }
        }

        internal static int GetCalendarsCore(string localeName, bool useUserOverride, CalendarId[] calendars)
        {
            Debug.Assert(!GlobalizationMode.Invariant);

            int count = IcuGetCalendars(localeName, calendars);

            if (useUserOverride)
            {
                // They want user overrides, see if the user calendar matches the input calendar
                int userCalendar = CultureData.GetLocaleInfoExInt(localeName, LOCALE_ICALENDARTYPE);

                if (userCalendar != 0 && (CalendarId)userCalendar != calendars[0])
                {
                    CalendarId userOverride = (CalendarId)userCalendar;
                    for (int i = 1; i < calendars.Length; i++)
                    {
                        if (calendars[i] == userOverride)
                        {
                            CalendarId tmp = calendars[0];
                            calendars[0] = userOverride;
                            calendars[i] = tmp;
                            return count;
                        }
                    }

                    // We didn't find it, we insert it at the beginning of the array. If calendar's array is full, we drop the last element.
                    count = count < calendars.Length ? count + 1 : count;
                    CalendarId[] tmpArray = new CalendarId[count];
                    tmpArray[0] = userOverride;
                    Array.Copy(calendars, 0, tmpArray, 1, count - 1);
                    Array.Copy(tmpArray, 0, calendars, 0, count);
                }
            }

            return count;
        }

        // Get native two digit year max
        internal static int GetTwoDigitYearMax(CalendarId calendarId)
        {
            return GlobalizationMode.Invariant ? Invariant.iTwoDigitYearMax :
                    CallGetCalendarInfoEx(null, calendarId, CAL_ITWODIGITYEARMAX, out int twoDigitYearMax) ?
                        twoDigitYearMax :
                        -1;
        }

        // ---- CalendarData.Nls.cs ------------------------------------------------------------

        private static unsafe bool CallGetCalendarInfoEx(string? localeName, CalendarId calendar, uint calType, out int data)
        {
            int value = 0;
            int ret = GetCalendarInfoEx(localeName, (uint)calendar, IntPtr.Zero, calType | CAL_RETURN_NUMBER, IntPtr.Zero, 0, &value);
            data = value;
            return ret != 0;
        }

        private static unsafe bool CallGetCalendarInfoEx(string localeName, CalendarId calendar, uint calType, out string data)
        {
            const int BUFFER_LENGTH = 80;

            // The maximum size for values returned from GetCalendarInfoEx is 80 characters.
            char* buffer = stackalloc char[BUFFER_LENGTH];

            int ret = GetCalendarInfoEx(localeName, (uint)calendar, IntPtr.Zero, calType, (IntPtr)buffer, BUFFER_LENGTH, null);
            if (ret > 0)
            {
                if (buffer[ret - 1] == '\0')
                {
                    ret--; // don't include the null termination in the string
                }
                data = new string(buffer, 0, ret);
                return true;
            }
            data = "";
            return false;
        }

        [DllImport("kernel32", EntryPoint = "GetCalendarInfoEx", CharSet = CharSet.Unicode)]
        private static extern unsafe int GetCalendarInfoEx(string? lpLocaleName, uint Calendar, IntPtr lpReserved, uint CalType, IntPtr lpCalData, int cchData, int* lpValue);
    }
}
#endif
