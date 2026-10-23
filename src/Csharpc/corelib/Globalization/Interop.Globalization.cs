// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173),
// src/libraries/Common/src/Interop/Interop.{ICU,Locale,Calendar,Casing,Collation,Normalization,Idna}.cs;
// see THIRD-PARTY-NOTICES.md. csharpc: the shim (src/runtime/globalization, a port of
// System.Globalization.Native) is linked into the program, so the imports bind to "__Internal" directly.
// The [LibraryImport] marshalling is spelled out: strings go to the shim as NUL-terminated UTF-16
// (CharSet.Unicode) or UTF-8 (sort handles), and BOOL results are read as int.
#nullable enable annotations

using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Runtime.InteropServices;
    internal static unsafe class Interop
    {
        internal static class Globalization
        {
            private const string Lib = "__Internal";

            // Needed by the managed side to match the native ResultCode enum (pal_errors.h).
            internal enum ResultCode
            {
                Success = 0,
                UnknownError = 1,
                InsufficientBuffer = 2,
                OutOfMemory = 3
            }

            // ---- ICU loading (Interop.ICU.cs) ----

            [DllImport(Lib, EntryPoint = "GlobalizationNative_LoadICU")]
            internal static extern int LoadICU();

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetICUVersion")]
            internal static extern int GetICUVersion();

            [DllImport(Lib, EntryPoint = "GlobalizationNative_InitICUFunctions")]
            internal static extern void InitICUFunctions(IntPtr icuuc, IntPtr icuin, string version, string? suffix);

            // ---- locales (Interop.Locale.cs) ----

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleName", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleNameNative(string localeName, char* value, int valueLength);
            internal static bool GetLocaleName(string localeName, char* value, int valueLength) => GetLocaleNameNative(localeName, value, valueLength) != 0;

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleInfoString", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleInfoStringNative(string localeName, uint localeStringData, char* value, int valueLength, string? uiLocaleName);
            internal static bool GetLocaleInfoString(string localeName, uint localeStringData, char* value, int valueLength, string? uiLocaleName = null) =>
                GetLocaleInfoStringNative(localeName, localeStringData, value, valueLength, uiLocaleName) != 0;

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetDefaultLocaleName")]
            private static extern int GetDefaultLocaleNameNative(char* value, int valueLength);
            internal static bool GetDefaultLocaleName(char* value, int valueLength) => GetDefaultLocaleNameNative(value, valueLength) != 0;

            [DllImport(Lib, EntryPoint = "GlobalizationNative_IsPredefinedLocale", CharSet = CharSet.Unicode)]
            private static extern int IsPredefinedLocaleNative(string localeName);
            internal static bool IsPredefinedLocale(string localeName) => IsPredefinedLocaleNative(localeName) != 0;

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleTimeFormat", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleTimeFormatNative(string localeName, int shortFormat, char* value, int valueLength);
            internal static bool GetLocaleTimeFormat(string localeName, bool shortFormat, char* value, int valueLength) =>
                GetLocaleTimeFormatNative(localeName, shortFormat ? 1 : 0, value, valueLength) != 0;

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleInfoInt", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleInfoIntNative(string localeName, uint localeNumberData, int* value);
            internal static bool GetLocaleInfoInt(string localeName, uint localeNumberData, ref int value)
            {
                int v = value;
                bool ok = GetLocaleInfoIntNative(localeName, localeNumberData, &v) != 0;
                value = v;
                return ok;
            }

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleInfoGroupingSizes", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleInfoGroupingSizesNative(string localeName, uint localeGroupingData, int* primaryGroupSize, int* secondaryGroupSize);
            internal static bool GetLocaleInfoGroupingSizes(string localeName, uint localeGroupingData, ref int primaryGroupSize, ref int secondaryGroupSize)
            {
                int p = primaryGroupSize, s = secondaryGroupSize;
                bool ok = GetLocaleInfoGroupingSizesNative(localeName, localeGroupingData, &p, &s) != 0;
                primaryGroupSize = p;
                secondaryGroupSize = s;
                return ok;
            }

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocales")]
            private static extern int GetLocalesNative(char* value, int valueLength);
            internal static int GetLocales(char[]? value, int valueLength)
            {
                if (value == null) return GetLocalesNative(null, valueLength);
                fixed (char* p = value) return GetLocalesNative(p, valueLength);
            }

            // ---- calendars (Interop.Calendar.cs) ----

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetCalendars", CharSet = CharSet.Unicode)]
            private static extern int GetCalendarsNative(string localeName, ushort* calendars, int calendarsCapacity);
            internal static int GetCalendars(string localeName, CalendarId[] calendars, int calendarsCapacity)
            {
                ushort* buffer = stackalloc ushort[calendarsCapacity];
                int count = GetCalendarsNative(localeName, buffer, calendarsCapacity);
                for (int i = 0; i < count && i < calendars.Length; i++) calendars[i] = (CalendarId)buffer[i];
                return count;
            }

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetCalendarInfo", CharSet = CharSet.Unicode)]
            private static extern int GetCalendarInfoNative(string localeName, ushort calendarId, int calendarDataType, char* result, int resultCapacity);
            internal static ResultCode GetCalendarInfo(string localeName, CalendarId calendarId, CalendarDataType calendarDataType, char* result, int resultCapacity) =>
                (ResultCode)GetCalendarInfoNative(localeName, (ushort)calendarId, (int)calendarDataType, result, resultCapacity);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_EnumCalendarInfo", CharSet = CharSet.Unicode)]
            private static extern int EnumCalendarInfoNative(IntPtr callback, string localeName, ushort calendarId, int calendarDataType, IntPtr context);
            internal static bool EnumCalendarInfo(delegate* unmanaged<char*, IntPtr, void> callback, string localeName, CalendarId calendarId, CalendarDataType calendarDataType, IntPtr context) =>
                EnumCalendarInfoNative((IntPtr)callback, localeName, (ushort)calendarId, (int)calendarDataType, context) != 0;

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLatestJapaneseEra")]
            internal static extern int GetLatestJapaneseEra();

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetJapaneseEraStartDate")]
            private static extern int GetJapaneseEraStartDateNative(int era, int* startYear, int* startMonth, int* startDay);
            internal static bool GetJapaneseEraStartDate(int era, out int startYear, out int startMonth, out int startDay)
            {
                int y = 0, m = 0, d = 0;
                bool ok = GetJapaneseEraStartDateNative(era, &y, &m, &d) != 0;
                startYear = y; startMonth = m; startDay = d;
                return ok;
            }

            // ---- casing (Interop.Casing.cs) ----

            [DllImport(Lib, EntryPoint = "GlobalizationNative_ChangeCase")]
            internal static extern void ChangeCaseNative(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper);
            internal static void ChangeCase(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
                ChangeCaseNative(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper ? 1 : 0);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_ChangeCaseInvariant")]
            internal static extern void ChangeCaseInvariantNative(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper);
            internal static void ChangeCaseInvariant(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
                ChangeCaseInvariantNative(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper ? 1 : 0);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_ChangeCaseTurkish")]
            internal static extern void ChangeCaseTurkishNative(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper);
            internal static void ChangeCaseTurkish(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
                ChangeCaseTurkishNative(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper ? 1 : 0);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_InitOrdinalCasingPage")]
            internal static extern void InitOrdinalCasingPage(int pageNumber, char* pTarget);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_InitOrdinalLowerCasingPage")]
            internal static extern void InitOrdinalLowerCasingPage(int pageNumber, char* pTarget);

            // ---- collation (Interop.Collation.cs) ----

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetSortHandle")]
            private static extern int GetSortHandleNative(string localeName, IntPtr* sortHandle);
            internal static ResultCode GetSortHandle(string localeName, out IntPtr sortHandle)
            {
                IntPtr handle = IntPtr.Zero;
                int result = GetSortHandleNative(localeName, &handle);
                sortHandle = handle;
                return (ResultCode)result;
            }

            [DllImport(Lib, EntryPoint = "GlobalizationNative_CloseSortHandle")]
            internal static extern void CloseSortHandle(IntPtr handle);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_CompareString")]
            private static extern int CompareStringNative(IntPtr sortHandle, char* lpStr1, int cwStr1Len, char* lpStr2, int cwStr2Len, int options);
            internal static int CompareString(IntPtr sortHandle, char* lpStr1, int cwStr1Len, char* lpStr2, int cwStr2Len, System.Globalization.CompareOptions options) =>
                CompareStringNative(sortHandle, lpStr1, cwStr1Len, lpStr2, cwStr2Len, (int)options);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_IndexOf")]
            private static extern int IndexOfNative(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, int options, int* matchLengthPtr);
            internal static int IndexOf(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, System.Globalization.CompareOptions options, int* matchLengthPtr) =>
                IndexOfNative(sortHandle, target, cwTargetLength, pSource, cwSourceLength, (int)options, matchLengthPtr);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_LastIndexOf")]
            private static extern int LastIndexOfNative(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, int options, int* matchLengthPtr);
            internal static int LastIndexOf(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, System.Globalization.CompareOptions options, int* matchLengthPtr) =>
                LastIndexOfNative(sortHandle, target, cwTargetLength, pSource, cwSourceLength, (int)options, matchLengthPtr);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_StartsWith")]
            private static extern int StartsWithNative(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, int options, int* matchedLength);
            internal static bool StartsWith(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, System.Globalization.CompareOptions options, int* matchedLength) =>
                StartsWithNative(sortHandle, target, cwTargetLength, source, cwSourceLength, (int)options, matchedLength) != 0;

            [DllImport(Lib, EntryPoint = "GlobalizationNative_EndsWith")]
            private static extern int EndsWithNative(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, int options, int* matchedLength);
            internal static bool EndsWith(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, System.Globalization.CompareOptions options, int* matchedLength) =>
                EndsWithNative(sortHandle, target, cwTargetLength, source, cwSourceLength, (int)options, matchedLength) != 0;

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetSortKey")]
            private static extern int GetSortKeyNative(IntPtr sortHandle, char* str, int strLength, byte* sortKey, int sortKeyLength, int options);
            internal static int GetSortKey(IntPtr sortHandle, char* str, int strLength, byte* sortKey, int sortKeyLength, System.Globalization.CompareOptions options) =>
                GetSortKeyNative(sortHandle, str, strLength, sortKey, sortKeyLength, (int)options);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetSortVersion")]
            internal static extern int GetSortVersion(IntPtr sortHandle);

            // ---- normalization (Interop.Normalization.cs) ----

            [DllImport(Lib, EntryPoint = "GlobalizationNative_IsNormalized")]
            internal static extern int IsNormalized(int normalizationForm, char* src, int srcLen);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_NormalizeString")]
            internal static extern int NormalizeString(int normalizationForm, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity);

            // ---- IDNA (Interop.Idna.cs) ----

            internal const int AllowUnassigned = 0x1;
            internal const int UseStd3AsciiRules = 0x2;

            [DllImport(Lib, EntryPoint = "GlobalizationNative_ToAscii")]
            internal static extern int ToAscii(uint flags, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity);

            [DllImport(Lib, EntryPoint = "GlobalizationNative_ToUnicode")]
            internal static extern int ToUnicode(uint flags, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity);
        }
    }
}
