// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Private.CoreLib/src/System/Globalization;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md); changes of substance
// are marked `csharpc:`.
#nullable enable annotations

using System.Globalization;
using System;
using System.Diagnostics;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System.Globalization;
    using System;
    using System.Diagnostics;
    internal sealed partial class CalendarData
    {
        private bool LoadCalendarDataFromSystemCore(string localeName, CalendarId calendarId)
        {
            return IcuLoadCalendarDataFromSystem(localeName, calendarId);
        }

#pragma warning disable IDE0060
        internal static int GetCalendarsCore(string localeName, bool useUserOverride, CalendarId[] calendars) =>
            IcuGetCalendars(localeName, calendars);

        internal static int GetTwoDigitYearMax(CalendarId calendarId)
        {
            Debug.Assert(!GlobalizationMode.UseNls);

            // There is no user override for this value on Linux or in ICU.
            // So just return -1 to use the hard-coded defaults.
            return GlobalizationMode.Invariant ? Invariant.iTwoDigitYearMax : -1;
        }
#pragma warning restore IDE0060
    }
}
