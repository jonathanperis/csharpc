// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Private.CoreLib/src/System/Globalization;
// see THIRD-PARTY-NOTICES.md.
//
// The ICU boundary's enums (#426): the globalization code itself is served from the framework's IL, and the slicer's csharpc profile
// redirects CoreLib's CalendarId and CalendarDataType to these, so the slice and the corelib's ICU imports (Interop.Globalization.cs,
// UpstreamGlobalization.cs) pass the same type. The values are the native shim's (pal_calendarData.h).

namespace CsharpcCoreLib.Globalization
{
    public enum CalendarId : ushort
    {
        UNINITIALIZED_VALUE = 0,
        GREGORIAN = 1,     // Gregorian (localized) calendar
        GREGORIAN_US = 2,     // Gregorian (U.S.) calendar
        JAPAN = 3,     // Japanese Emperor Era calendar
                       /* SSS_WARNINGS_OFF */
        TAIWAN = 4,     // Taiwan Era calendar /* SSS_WARNINGS_ON */
        KOREA = 5,     // Korean Tangun Era calendar
        HIJRI = 6,     // Hijri (Arabic Lunar) calendar
        THAI = 7,     // Thai calendar
        HEBREW = 8,     // Hebrew (Lunar) calendar
        GREGORIAN_ME_FRENCH = 9,     // Gregorian Middle East French calendar
        GREGORIAN_ARABIC = 10,     // Gregorian Arabic calendar
        GREGORIAN_XLIT_ENGLISH = 11,     // Gregorian Transliterated English calendar
        GREGORIAN_XLIT_FRENCH = 12,
        // Note that all calendars after this point are MANAGED ONLY for now.
        JULIAN = 13,
        JAPANESELUNISOLAR = 14,
        CHINESELUNISOLAR = 15,
        SAKA = 16,     // reserved to match Office but not implemented in our code
        LUNAR_ETO_CHN = 17,     // reserved to match Office but not implemented in our code
        LUNAR_ETO_KOR = 18,     // reserved to match Office but not implemented in our code
        LUNAR_ETO_ROKUYOU = 19,     // reserved to match Office but not implemented in our code
        KOREANLUNISOLAR = 20,
        TAIWANLUNISOLAR = 21,
        PERSIAN = 22,
        UMALQURA = 23,
        LAST_CALENDAR = 23      // Last calendar ID
    }

    // needs to be kept in sync with CalendarDataType in System.Globalization.Native
    public enum CalendarDataType
    {
        Uninitialized = 0,
        NativeName = 1,
        MonthDay = 2,
        ShortDates = 3,
        LongDates = 4,
        YearMonths = 5,
        DayNames = 6,
        AbbrevDayNames = 7,
        MonthNames = 8,
        AbbrevMonthNames = 9,
        SuperShortDayNames = 10,
        MonthGenitiveNames = 11,
        AbbrevMonthGenitiveNames = 12,
        EraNames = 13,
        AbbrevEraNames = 14,
    }
}
