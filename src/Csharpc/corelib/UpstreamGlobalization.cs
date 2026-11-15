// The ICU boundary of the globalization code served from the framework's IL (ADR 0023, #426): the slicer's csharpc profile points every
// Interop.Globalization call of CoreLib's IL at UpstreamGlobalizationInterop (tools/il-first/slicer/Program.cs), which forwards it to the
// corelib's Interop.Globalization, the one set of imports of the runtime's ICU shim (src/runtime/globalization/pal_*.c). Those wrappers load
// ICU on first use (GlobalizationMode.EnsureIcuLoaded) and fail the same way on WASI, which has no ICU.
//
// Security: every buffer handed to native code is bounded by the managed buffer it comes from. A pointer argument is CoreLib's own
// stackalloc or pinned buffer with its own length; where the length travels next to an array or a span (GetLocales, GetCalendars, ToAscii,
// ToUnicode), the length passed on is clamped to the array's or span's length, so a wrong length can never let the shim write past it.
// The shim writes at most the given length, terminator included, and strings go in NUL-terminated (CharSet.Unicode marshalling).
#nullable disable
using System;
using CsharpcCoreLib.Globalization;
using IcuInterop = CsharpcCoreLib.Globalization.Interop.Globalization;

namespace CsharpcCoreLib
{
    /// <summary>Interop.Globalization.ResultCode (pal_errors.h), top-level so the slice's references to it can be redirected here.</summary>
    public enum UpstreamGlobalizationResultCode
    {
        Success = 0,
        UnknownError = 1,
        InsufficientBuffer = 2,
        OutOfMemory = 3,
    }

    /// <summary>The members of CoreLib's Interop.Globalization the slice calls, with CoreLib's signatures.</summary>
    public static unsafe class UpstreamGlobalizationInterop
    {
        // ---- locales ----

        public static bool GetLocaleName(string localeName, char* value, int valueLength) => IcuInterop.GetLocaleName(localeName, value, valueLength);

        public static bool GetLocaleInfoString(string localeName, uint localeStringData, char* value, int valueLength, string uiLocaleName) =>
            IcuInterop.GetLocaleInfoString(localeName, localeStringData, value, valueLength, uiLocaleName);

        public static bool GetDefaultLocaleName(char* value, int valueLength) => IcuInterop.GetDefaultLocaleName(value, valueLength);

        public static bool IsPredefinedLocale(string localeName) => IcuInterop.IsPredefinedLocale(localeName);

        public static bool GetLocaleTimeFormat(string localeName, bool shortFormat, char* value, int valueLength) =>
            IcuInterop.GetLocaleTimeFormat(localeName, shortFormat, value, valueLength);

        public static bool GetLocaleInfoInt(string localeName, uint localeNumberData, ref int value) =>
            IcuInterop.GetLocaleInfoInt(localeName, localeNumberData, ref value);

        public static bool GetLocaleInfoGroupingSizes(string localeName, uint localeGroupingData, ref int primaryGroupSize, ref int secondaryGroupSize) =>
            IcuInterop.GetLocaleInfoGroupingSizes(localeName, localeGroupingData, ref primaryGroupSize, ref secondaryGroupSize);

        /// <summary>The locale list; called with (null, 0) for the size first. The shim writes at most min(valueLength, value.Length) chars.</summary>
        public static int GetLocales(char[] value, int valueLength) =>
            IcuInterop.GetLocales(value, value is null ? 0 : Math.Clamp(valueLength, 0, value.Length));

        // ---- calendars ----

        public static int GetCalendars(string localeName, CalendarId[] calendars, int calendarsCapacity) =>
            IcuInterop.GetCalendars(localeName, calendars, Math.Clamp(calendarsCapacity, 0, calendars.Length));

        public static UpstreamGlobalizationResultCode GetCalendarInfo(string localeName, CalendarId calendarId, CalendarDataType calendarDataType, char* result, int resultCapacity) =>
            (UpstreamGlobalizationResultCode)IcuInterop.GetCalendarInfo(localeName, calendarId, calendarDataType, result, resultCapacity);

        public static bool EnumCalendarInfo(delegate* unmanaged<char*, IntPtr, void> callback, string localeName, CalendarId calendarId, CalendarDataType calendarDataType, IntPtr context) =>
            IcuInterop.EnumCalendarInfo(callback, localeName, calendarId, calendarDataType, context);

        public static int GetLatestJapaneseEra() => IcuInterop.GetLatestJapaneseEra();

        public static bool GetJapaneseEraStartDate(int era, out int startYear, out int startMonth, out int startDay) =>
            IcuInterop.GetJapaneseEraStartDate(era, out startYear, out startMonth, out startDay);

        // ---- casing ----

        public static void ChangeCase(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
            IcuInterop.ChangeCase(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper);

        public static void ChangeCaseInvariant(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
            IcuInterop.ChangeCaseInvariant(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper);

        public static void ChangeCaseTurkish(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
            IcuInterop.ChangeCaseTurkish(src, srcLen, dstBuffer, dstBufferCapacity, bToUpper);

        public static void InitOrdinalCasingPage(int pageNumber, char* pTarget) => IcuInterop.InitOrdinalCasingPage(pageNumber, pTarget);

        public static void InitOrdinalLowerCasingPage(int pageNumber, char* pTarget) => IcuInterop.InitOrdinalLowerCasingPage(pageNumber, pTarget);

        // ---- collation ----

        public static UpstreamGlobalizationResultCode GetSortHandle(string localeName, out IntPtr sortHandle) =>
            (UpstreamGlobalizationResultCode)IcuInterop.GetSortHandle(localeName, out sortHandle);

        public static void CloseSortHandle(IntPtr handle) => IcuInterop.CloseSortHandle(handle);

        public static int CompareString(IntPtr sortHandle, char* lpStr1, int cwStr1Len, char* lpStr2, int cwStr2Len, System.Globalization.CompareOptions options) =>
            IcuInterop.CompareString(sortHandle, lpStr1, cwStr1Len, lpStr2, cwStr2Len, options);

        public static int IndexOf(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, System.Globalization.CompareOptions options, int* matchLengthPtr) =>
            IcuInterop.IndexOf(sortHandle, target, cwTargetLength, pSource, cwSourceLength, options, matchLengthPtr);

        public static int LastIndexOf(IntPtr sortHandle, char* target, int cwTargetLength, char* pSource, int cwSourceLength, System.Globalization.CompareOptions options, int* matchLengthPtr) =>
            IcuInterop.LastIndexOf(sortHandle, target, cwTargetLength, pSource, cwSourceLength, options, matchLengthPtr);

        public static bool StartsWith(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, System.Globalization.CompareOptions options, int* matchedLength) =>
            IcuInterop.StartsWith(sortHandle, target, cwTargetLength, source, cwSourceLength, options, matchedLength);

        public static bool EndsWith(IntPtr sortHandle, char* target, int cwTargetLength, char* source, int cwSourceLength, System.Globalization.CompareOptions options, int* matchedLength) =>
            IcuInterop.EndsWith(sortHandle, target, cwTargetLength, source, cwSourceLength, options, matchedLength);

        public static int GetSortKey(IntPtr sortHandle, char* str, int strLength, byte* sortKey, int sortKeyLength, System.Globalization.CompareOptions options) =>
            IcuInterop.GetSortKey(sortHandle, str, strLength, sortKey, sortKeyLength, options);

        public static int GetSortVersion(IntPtr sortHandle) => IcuInterop.GetSortVersion(sortHandle);

        // ---- IDNA ----

        public static int ToAscii(uint flags, ReadOnlySpan<char> src, int srcLen, Span<char> dstBuffer, int dstBufferCapacity) =>
            IcuInterop.ToAscii(flags, src, Math.Clamp(srcLen, 0, src.Length), dstBuffer, Math.Clamp(dstBufferCapacity, 0, dstBuffer.Length));

        public static int ToUnicode(uint flags, ReadOnlySpan<char> src, int srcLen, Span<char> dstBuffer, int dstBufferCapacity) =>
            IcuInterop.ToUnicode(flags, src, Math.Clamp(srcLen, 0, src.Length), dstBuffer, Math.Clamp(dstBufferCapacity, 0, dstBuffer.Length));
    }
}

namespace CsharpcCoreLib
{
    using System.Runtime.CompilerServices;
    using NumberFormatInfo = System.Globalization.NumberFormatInfo;

    /// <summary>
    /// The CoreLib-internal members of NumberFormatInfo the corelib's own number formatting and parsing reads (Number*.cs), now that
    /// NumberFormatInfo is the framework's (#426): its group-size arrays without the public getters' copy, and the style checks. Bound at
    /// compile time (FunctionCodeGen.UnsafeAccessor.cs).
    /// </summary>
    internal static class NumberFormatInfoInternals
    {
        private const string Type = "CsharpcCoreLib.Upstream.System.Globalization.NumberFormatInfo, CsharpcUpstream";

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_numberGroupSizes")]
        internal static extern ref int[] NumberGroupSizes(NumberFormatInfo nfi);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_currencyGroupSizes")]
        internal static extern ref int[] CurrencyGroupSizes(NumberFormatInfo nfi);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_percentGroupSizes")]
        internal static extern ref int[] PercentGroupSizes(NumberFormatInfo nfi);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_HasInvariantNumberSigns")]
        internal static extern bool HasInvariantNumberSigns(NumberFormatInfo nfi);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AllowHyphenDuringParsing")]
        internal static extern bool AllowHyphenDuringParsing(NumberFormatInfo nfi);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ValidateParseStyleInteger")]
        internal static extern void ValidateParseStyleInteger([UnsafeAccessorType(Type)] object nfi, System.Globalization.NumberStyles style);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ValidateParseStyleFloatingPoint")]
        internal static extern void ValidateParseStyleFloatingPoint([UnsafeAccessorType(Type)] object nfi, System.Globalization.NumberStyles style);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ValidateParseStyleDecimal")]
        internal static extern void ValidateParseStyleDecimal([UnsafeAccessorType(Type)] object nfi, System.Globalization.NumberStyles style);
    }
}
