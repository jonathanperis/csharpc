// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173),
// src/libraries/Common/src/Interop/Interop.{ICU,Locale,Calendar,Casing,Collation,Normalization,Idna}.cs;
// see THIRD-PARTY-NOTICES.md. csharpc: the shim (src/runtime/globalization, a port of
// System.Globalization.Native) is linked into the program, so the imports bind to "__Internal" directly.
// The [LibraryImport] marshalling is spelled out: strings go to the shim as NUL-terminated UTF-16
// (CharSet.Unicode) or UTF-8 (sort handles), and BOOL results are read as int. WASI has no ICU (nothing can
// be loaded dynamically): LoadICU fails there and the other imports are never reached (invariant mode).
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

            // csharpc: every import but the loader's own goes through a wrapper that loads ICU first
            // (GlobalizationMode.EnsureIcuLoaded; on WASI the stubs fail the same way first): ICU is loaded
            // by the first call that needs it, not when globalization code first checks for invariant mode.

            // ---- ICU loading (Interop.ICU.cs) ----

#if CSHARPC_TARGET_WASI
            internal static int LoadICU() => 0;
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_LoadICU")]
            internal static extern int LoadICU();
#endif

#if CSHARPC_TARGET_WASI
            internal static int GetICUVersion() { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetICUVersion")]
            private static extern int GetICUVersionIcu();
            internal static int GetICUVersion() { GlobalizationMode.EnsureIcuLoaded(); return GetICUVersionIcu(); }
#endif

#if CSHARPC_TARGET_WASI
            internal static void InitICUFunctions(IntPtr icuuc, IntPtr icuin, string version, string? suffix) => throw new PlatformNotSupportedException();
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_InitICUFunctions")]
            internal static extern void InitICUFunctions(IntPtr icuuc, IntPtr icuin, string version, string? suffix);
#endif

            // ---- locales (Interop.Locale.cs) ----

#if CSHARPC_TARGET_WASI
            private static int GetLocaleNameNative(string localeName, char* value, int valueLength) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleName", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleNameNativeIcu(string localeName, char* value, int valueLength);
            private static int GetLocaleNameNative(string localeName, char* value, int valueLength) { GlobalizationMode.EnsureIcuLoaded(); return GetLocaleNameNativeIcu(localeName, value, valueLength); }
#endif
            internal static bool GetLocaleName(string localeName, char* value, int valueLength) => GetLocaleNameNative(localeName, value, valueLength) != 0;

#if CSHARPC_TARGET_WASI
            private static int GetLocaleInfoStringNative(string localeName, uint localeStringData, char* value, int valueLength, string? uiLocaleName) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleInfoString", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleInfoStringNativeIcu(string localeName, uint localeStringData, char* value, int valueLength, string? uiLocaleName);
            private static int GetLocaleInfoStringNative(string localeName, uint localeStringData, char* value, int valueLength, string? uiLocaleName) { GlobalizationMode.EnsureIcuLoaded(); return GetLocaleInfoStringNativeIcu(localeName, localeStringData, value, valueLength, uiLocaleName); }
#endif
            internal static bool GetLocaleInfoString(string localeName, uint localeStringData, char* value, int valueLength, string? uiLocaleName = null) =>
                GetLocaleInfoStringNative(localeName, localeStringData, value, valueLength, uiLocaleName) != 0;

#if CSHARPC_TARGET_WASI
            private static int GetDefaultLocaleNameNative(char* value, int valueLength) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetDefaultLocaleName")]
            private static extern int GetDefaultLocaleNameNativeIcu(char* value, int valueLength);
            private static int GetDefaultLocaleNameNative(char* value, int valueLength) { GlobalizationMode.EnsureIcuLoaded(); return GetDefaultLocaleNameNativeIcu(value, valueLength); }
#endif
            internal static bool GetDefaultLocaleName(char* value, int valueLength) => GetDefaultLocaleNameNative(value, valueLength) != 0;

#if CSHARPC_TARGET_WASI
            private static int IsPredefinedLocaleNative(string localeName) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_IsPredefinedLocale", CharSet = CharSet.Unicode)]
            private static extern int IsPredefinedLocaleNativeIcu(string localeName);
            private static int IsPredefinedLocaleNative(string localeName) { GlobalizationMode.EnsureIcuLoaded(); return IsPredefinedLocaleNativeIcu(localeName); }
#endif
            internal static bool IsPredefinedLocale(string localeName) => IsPredefinedLocaleNative(localeName) != 0;

#if CSHARPC_TARGET_WASI
            private static int GetLocaleTimeFormatNative(string localeName, int shortFormat, char* value, int valueLength) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleTimeFormat", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleTimeFormatNativeIcu(string localeName, int shortFormat, char* value, int valueLength);
            private static int GetLocaleTimeFormatNative(string localeName, int shortFormat, char* value, int valueLength) { GlobalizationMode.EnsureIcuLoaded(); return GetLocaleTimeFormatNativeIcu(localeName, shortFormat, value, valueLength); }
#endif
            internal static bool GetLocaleTimeFormat(string localeName, bool shortFormat, char* value, int valueLength) =>
                GetLocaleTimeFormatNative(localeName, shortFormat ? 1 : 0, value, valueLength) != 0;

#if CSHARPC_TARGET_WASI
            private static int GetLocaleInfoIntNative(string localeName, uint localeNumberData, int* value) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleInfoInt", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleInfoIntNativeIcu(string localeName, uint localeNumberData, int* value);
            private static int GetLocaleInfoIntNative(string localeName, uint localeNumberData, int* value) { GlobalizationMode.EnsureIcuLoaded(); return GetLocaleInfoIntNativeIcu(localeName, localeNumberData, value); }
#endif
            internal static bool GetLocaleInfoInt(string localeName, uint localeNumberData, ref int value)
            {
                int v = value;
                bool ok = GetLocaleInfoIntNative(localeName, localeNumberData, &v) != 0;
                value = v;
                return ok;
            }

#if CSHARPC_TARGET_WASI
            private static int GetLocaleInfoGroupingSizesNative(string localeName, uint localeGroupingData, int* primaryGroupSize, int* secondaryGroupSize) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocaleInfoGroupingSizes", CharSet = CharSet.Unicode)]
            private static extern int GetLocaleInfoGroupingSizesNativeIcu(string localeName, uint localeGroupingData, int* primaryGroupSize, int* secondaryGroupSize);
            private static int GetLocaleInfoGroupingSizesNative(string localeName, uint localeGroupingData, int* primaryGroupSize, int* secondaryGroupSize) { GlobalizationMode.EnsureIcuLoaded(); return GetLocaleInfoGroupingSizesNativeIcu(localeName, localeGroupingData, primaryGroupSize, secondaryGroupSize); }
#endif
            internal static bool GetLocaleInfoGroupingSizes(string localeName, uint localeGroupingData, ref int primaryGroupSize, ref int secondaryGroupSize)
            {
                int p = primaryGroupSize, s = secondaryGroupSize;
                bool ok = GetLocaleInfoGroupingSizesNative(localeName, localeGroupingData, &p, &s) != 0;
                primaryGroupSize = p;
                secondaryGroupSize = s;
                return ok;
            }

#if CSHARPC_TARGET_WASI
            private static int GetLocalesNative(char* value, int valueLength) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLocales")]
            private static extern int GetLocalesNativeIcu(char* value, int valueLength);
            private static int GetLocalesNative(char* value, int valueLength) { GlobalizationMode.EnsureIcuLoaded(); return GetLocalesNativeIcu(value, valueLength); }
#endif
            internal static int GetLocales(char[]? value, int valueLength)
            {
                if (value == null) return GetLocalesNative(null, valueLength);
                fixed (char* p = value) return GetLocalesNative(p, valueLength);
            }

            // ---- calendars (Interop.Calendar.cs) ----

#if CSHARPC_TARGET_WASI
            private static int GetCalendarsNative(string localeName, ushort* calendars, int calendarsCapacity) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetCalendars", CharSet = CharSet.Unicode)]
            private static extern int GetCalendarsNativeIcu(string localeName, ushort* calendars, int calendarsCapacity);
            private static int GetCalendarsNative(string localeName, ushort* calendars, int calendarsCapacity) { GlobalizationMode.EnsureIcuLoaded(); return GetCalendarsNativeIcu(localeName, calendars, calendarsCapacity); }
#endif
            internal static int GetCalendars(string localeName, CalendarId[] calendars, int calendarsCapacity)
            {
                ushort* buffer = stackalloc ushort[calendarsCapacity];
                int count = GetCalendarsNative(localeName, buffer, calendarsCapacity);
                for (int i = 0; i < count && i < calendars.Length; i++) calendars[i] = (CalendarId)buffer[i];
                return count;
            }

#if CSHARPC_TARGET_WASI
            private static int GetCalendarInfoNative(string localeName, ushort calendarId, int calendarDataType, char* result, int resultCapacity) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetCalendarInfo", CharSet = CharSet.Unicode)]
            private static extern int GetCalendarInfoNativeIcu(string localeName, ushort calendarId, int calendarDataType, char* result, int resultCapacity);
            private static int GetCalendarInfoNative(string localeName, ushort calendarId, int calendarDataType, char* result, int resultCapacity) { GlobalizationMode.EnsureIcuLoaded(); return GetCalendarInfoNativeIcu(localeName, calendarId, calendarDataType, result, resultCapacity); }
#endif
            internal static ResultCode GetCalendarInfo(string localeName, CalendarId calendarId, CalendarDataType calendarDataType, char* result, int resultCapacity) =>
                (ResultCode)GetCalendarInfoNative(localeName, (ushort)calendarId, (int)calendarDataType, result, resultCapacity);

#if CSHARPC_TARGET_WASI
            private static int EnumCalendarInfoNative(IntPtr callback, string localeName, ushort calendarId, int calendarDataType, IntPtr context) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_EnumCalendarInfo", CharSet = CharSet.Unicode)]
            private static extern int EnumCalendarInfoNativeIcu(IntPtr callback, string localeName, ushort calendarId, int calendarDataType, IntPtr context);
            private static int EnumCalendarInfoNative(IntPtr callback, string localeName, ushort calendarId, int calendarDataType, IntPtr context) { GlobalizationMode.EnsureIcuLoaded(); return EnumCalendarInfoNativeIcu(callback, localeName, calendarId, calendarDataType, context); }
#endif
            internal static bool EnumCalendarInfo(delegate* unmanaged<char*, IntPtr, void> callback, string localeName, CalendarId calendarId, CalendarDataType calendarDataType, IntPtr context) =>
                EnumCalendarInfoNative((IntPtr)callback, localeName, (ushort)calendarId, (int)calendarDataType, context) != 0;

#if CSHARPC_TARGET_WASI
            internal static int GetLatestJapaneseEra() { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetLatestJapaneseEra")]
            private static extern int GetLatestJapaneseEraIcu();
            internal static int GetLatestJapaneseEra() { GlobalizationMode.EnsureIcuLoaded(); return GetLatestJapaneseEraIcu(); }
#endif

#if CSHARPC_TARGET_WASI
            private static int GetJapaneseEraStartDateNative(int era, int* startYear, int* startMonth, int* startDay) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetJapaneseEraStartDate")]
            private static extern int GetJapaneseEraStartDateNativeIcu(int era, int* startYear, int* startMonth, int* startDay);
            private static int GetJapaneseEraStartDateNative(int era, int* startYear, int* startMonth, int* startDay) { GlobalizationMode.EnsureIcuLoaded(); return GetJapaneseEraStartDateNativeIcu(era, startYear, startMonth, startDay); }
#endif
            internal static bool GetJapaneseEraStartDate(int era, out int startYear, out int startMonth, out int startDay)
            {
                int y = 0, m = 0, d = 0;
                bool ok = GetJapaneseEraStartDateNative(era, &y, &m, &d) != 0;
                startYear = y; startMonth = m; startDay = d;
                return ok;
            }

            // ---- casing (Interop.Casing.cs) ----

#if CSHARPC_TARGET_WASI
            internal static void ChangeCaseNative(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_ChangeCase")]
            private static extern void ChangeCaseNativeIcu(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper);
            internal static void ChangeCaseNative(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper) { GlobalizationMode.EnsureIcuLoaded(); ChangeCaseNativeIcu(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper); }
#endif
            internal static void ChangeCase(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
                ChangeCaseNative(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper ? 1 : 0);

#if CSHARPC_TARGET_WASI
            internal static void ChangeCaseInvariantNative(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_ChangeCaseInvariant")]
            private static extern void ChangeCaseInvariantNativeIcu(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper);
            internal static void ChangeCaseInvariantNative(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper) { GlobalizationMode.EnsureIcuLoaded(); ChangeCaseInvariantNativeIcu(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper); }
#endif
            internal static void ChangeCaseInvariant(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
                ChangeCaseInvariantNative(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper ? 1 : 0);

#if CSHARPC_TARGET_WASI
            internal static void ChangeCaseTurkishNative(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_ChangeCaseTurkish")]
            private static extern void ChangeCaseTurkishNativeIcu(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper);
            internal static void ChangeCaseTurkishNative(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, int bToUpper) { GlobalizationMode.EnsureIcuLoaded(); ChangeCaseTurkishNativeIcu(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper); }
#endif
            internal static void ChangeCaseTurkish(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
                ChangeCaseTurkishNative(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper ? 1 : 0);

#if CSHARPC_TARGET_WASI
            internal static void InitOrdinalCasingPage(int pageNumber, char* pTarget) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_InitOrdinalCasingPage")]
            private static extern void InitOrdinalCasingPageIcu(int pageNumber, char* pTarget);
            internal static void InitOrdinalCasingPage(int pageNumber, char* pTarget) { GlobalizationMode.EnsureIcuLoaded(); InitOrdinalCasingPageIcu(pageNumber, pTarget); }
#endif

#if CSHARPC_TARGET_WASI
            internal static void InitOrdinalLowerCasingPage(int pageNumber, char* pTarget) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_InitOrdinalLowerCasingPage")]
            private static extern void InitOrdinalLowerCasingPageIcu(int pageNumber, char* pTarget);
            internal static void InitOrdinalLowerCasingPage(int pageNumber, char* pTarget) { GlobalizationMode.EnsureIcuLoaded(); InitOrdinalLowerCasingPageIcu(pageNumber, pTarget); }
#endif

            // ---- collation (Interop.Collation.cs) ----

#if CSHARPC_TARGET_WASI
            private static int GetSortHandleNative(string localeName, IntPtr* sortHandle) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetSortHandle")]
            private static extern int GetSortHandleNativeIcu(string localeName, IntPtr* sortHandle);
            private static int GetSortHandleNative(string localeName, IntPtr* sortHandle) { GlobalizationMode.EnsureIcuLoaded(); return GetSortHandleNativeIcu(localeName, sortHandle); }
#endif
            internal static ResultCode GetSortHandle(string localeName, out IntPtr sortHandle)
            {
                IntPtr handle = IntPtr.Zero;
                int result = GetSortHandleNative(localeName, &handle);
                sortHandle = handle;
                return (ResultCode)result;
            }

#if CSHARPC_TARGET_WASI
            internal static void CloseSortHandle(IntPtr handle) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_CloseSortHandle")]
            private static extern void CloseSortHandleIcu(IntPtr handle);
            internal static void CloseSortHandle(IntPtr handle) { GlobalizationMode.EnsureIcuLoaded(); CloseSortHandleIcu(handle); }
#endif

#if CSHARPC_TARGET_WASI
            private static int CompareStringNative(IntPtr sortHandle, char* lpStr1, int cwStr1Len, char* lpStr2, int cwStr2Len, int options) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_CompareString")]
            private static extern int CompareStringNativeIcu(IntPtr sortHandle, char* lpStr1, int cwStr1Len, char* lpStr2, int cwStr2Len, int options);
            private static int CompareStringNative(IntPtr sortHandle, char* lpStr1, int cwStr1Len, char* lpStr2, int cwStr2Len, int options) { GlobalizationMode.EnsureIcuLoaded(); return CompareStringNativeIcu(sortHandle, lpStr1, cwStr1Len, lpStr2, cwStr2Len, options); }
#endif
            internal static int CompareString(IntPtr sortHandle, char* lpStr1, int cwStr1Len, char* lpStr2, int cwStr2Len, System.Globalization.CompareOptions options) =>
                CompareStringNative(sortHandle, lpStr1, cwStr1Len, lpStr2, cwStr2Len, (int)options);

#if CSHARPC_TARGET_WASI
            private static int IndexOfNative(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, int options, int* matchLengthPtr) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_IndexOf")]
            private static extern int IndexOfNativeIcu(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, int options, int* matchLengthPtr);
            private static int IndexOfNative(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, int options, int* matchLengthPtr) { GlobalizationMode.EnsureIcuLoaded(); return IndexOfNativeIcu(sortHandle, target, cwTargetLength, pSource, cwSourceLength, options, matchLengthPtr); }
#endif
            internal static int IndexOf(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, System.Globalization.CompareOptions options, int* matchLengthPtr) =>
                IndexOfNative(sortHandle, target, cwTargetLength, pSource, cwSourceLength, (int)options, matchLengthPtr);

#if CSHARPC_TARGET_WASI
            private static int LastIndexOfNative(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, int options, int* matchLengthPtr) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_LastIndexOf")]
            private static extern int LastIndexOfNativeIcu(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, int options, int* matchLengthPtr);
            private static int LastIndexOfNative(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, int options, int* matchLengthPtr) { GlobalizationMode.EnsureIcuLoaded(); return LastIndexOfNativeIcu(sortHandle, target, cwTargetLength, pSource, cwSourceLength, options, matchLengthPtr); }
#endif
            internal static int LastIndexOf(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, System.Globalization.CompareOptions options, int* matchLengthPtr) =>
                LastIndexOfNative(sortHandle, target, cwTargetLength, pSource, cwSourceLength, (int)options, matchLengthPtr);

#if CSHARPC_TARGET_WASI
            private static int StartsWithNative(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, int options, int* matchedLength) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_StartsWith")]
            private static extern int StartsWithNativeIcu(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, int options, int* matchedLength);
            private static int StartsWithNative(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, int options, int* matchedLength) { GlobalizationMode.EnsureIcuLoaded(); return StartsWithNativeIcu(sortHandle, target, cwTargetLength, source, cwSourceLength, options, matchedLength); }
#endif
            internal static bool StartsWith(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, System.Globalization.CompareOptions options, int* matchedLength) =>
                StartsWithNative(sortHandle, target, cwTargetLength, source, cwSourceLength, (int)options, matchedLength) != 0;

#if CSHARPC_TARGET_WASI
            private static int EndsWithNative(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, int options, int* matchedLength) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_EndsWith")]
            private static extern int EndsWithNativeIcu(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, int options, int* matchedLength);
            private static int EndsWithNative(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, int options, int* matchedLength) { GlobalizationMode.EnsureIcuLoaded(); return EndsWithNativeIcu(sortHandle, target, cwTargetLength, source, cwSourceLength, options, matchedLength); }
#endif
            internal static bool EndsWith(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, System.Globalization.CompareOptions options, int* matchedLength) =>
                EndsWithNative(sortHandle, target, cwTargetLength, source, cwSourceLength, (int)options, matchedLength) != 0;

#if CSHARPC_TARGET_WASI
            private static int GetSortKeyNative(IntPtr sortHandle, char* str, int strLength, byte* sortKey, int sortKeyLength, int options) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetSortKey")]
            private static extern int GetSortKeyNativeIcu(IntPtr sortHandle, char* str, int strLength, byte* sortKey, int sortKeyLength, int options);
            private static int GetSortKeyNative(IntPtr sortHandle, char* str, int strLength, byte* sortKey, int sortKeyLength, int options) { GlobalizationMode.EnsureIcuLoaded(); return GetSortKeyNativeIcu(sortHandle, str, strLength, sortKey, sortKeyLength, options); }
#endif
            internal static int GetSortKey(IntPtr sortHandle, char* str, int strLength, byte* sortKey, int sortKeyLength, System.Globalization.CompareOptions options) =>
                GetSortKeyNative(sortHandle, str, strLength, sortKey, sortKeyLength, (int)options);

#if CSHARPC_TARGET_WASI
            internal static int GetSortVersion(IntPtr sortHandle) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_GetSortVersion")]
            private static extern int GetSortVersionIcu(IntPtr sortHandle);
            internal static int GetSortVersion(IntPtr sortHandle) { GlobalizationMode.EnsureIcuLoaded(); return GetSortVersionIcu(sortHandle); }
#endif

            // ---- normalization (Interop.Normalization.cs) ----

#if CSHARPC_TARGET_WASI
            internal static int IsNormalized(int normalizationForm, char* src, int srcLen) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_IsNormalized")]
            private static extern int IsNormalizedIcu(int normalizationForm, char* src, int srcLen);
            internal static int IsNormalized(int normalizationForm, char* src, int srcLen) { GlobalizationMode.EnsureIcuLoaded(); return IsNormalizedIcu(normalizationForm, src, srcLen); }
#endif

#if CSHARPC_TARGET_WASI
            internal static int NormalizeString(int normalizationForm, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_NormalizeString")]
            private static extern int NormalizeStringIcu(int normalizationForm, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity);
            internal static int NormalizeString(int normalizationForm, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity) { GlobalizationMode.EnsureIcuLoaded(); return NormalizeStringIcu(normalizationForm, src, srcLen, dstBuffer, dstBufferCapacity); }
#endif

            // ---- IDNA (Interop.Idna.cs) ----

            internal const int AllowUnassigned = 0x1;
            internal const int UseStd3AsciiRules = 0x2;

#if CSHARPC_TARGET_WASI
            internal static int ToAscii(uint flags, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_ToAscii")]
            private static extern int ToAsciiIcu(uint flags, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity);
            internal static int ToAscii(uint flags, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity) { GlobalizationMode.EnsureIcuLoaded(); return ToAsciiIcu(flags, src, srcLen, dstBuffer, dstBufferCapacity); }
#endif

#if CSHARPC_TARGET_WASI
            internal static int ToUnicode(uint flags, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity) { GlobalizationMode.EnsureIcuLoaded(); throw new PlatformNotSupportedException(); }
#else
            [DllImport(Lib, EntryPoint = "GlobalizationNative_ToUnicode")]
            private static extern int ToUnicodeIcu(uint flags, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity);
            internal static int ToUnicode(uint flags, char* src, int srcLen, char* dstBuffer, int dstBufferCapacity) { GlobalizationMode.EnsureIcuLoaded(); return ToUnicodeIcu(flags, src, srcLen, dstBuffer, dstBufferCapacity); }
#endif
        }
    }
}
