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

    /// <summary>Interop.Globalization.TimeZoneDisplayNameType (pal_timeZoneInfo.h's values): what TimeZoneInfo asks ICU to name (#98).</summary>
    public enum UpstreamTimeZoneDisplayNameType
    {
        Generic = 0,
        Standard = 1,
        DaylightSavings = 2,
        GenericLocation = 3,
        ExemplarCity = 4,
        TimeZoneName = 5,
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

        // ---- time zones (TimeZoneInfo, #98): the result buffers are the callers' stackalloc'd ones with their own lengths ----

        public static UpstreamGlobalizationResultCode GetTimeZoneDisplayName(string localeName, string timeZoneId, UpstreamTimeZoneDisplayNameType type, char* result, int resultLength) =>
            (UpstreamGlobalizationResultCode)IcuInterop.GetTimeZoneDisplayName(localeName, timeZoneId, (int)type, result, resultLength);

        public static int IanaIdToWindowsId(string ianaId, char* windowsId, int windowsIdLength) => IcuInterop.IanaIdToWindowsId(ianaId, windowsId, windowsIdLength);

        public static int WindowsIdToIanaId(string windowsId, IntPtr region, char* ianaId, int ianaIdLength) => IcuInterop.WindowsIdToIanaId(windowsId, region, ianaId, ianaIdLength);

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
    /// The group-size arrays of NumberFormatInfo (without the public getters' copy) that the corelib's remaining standard formatter reads
    /// for BigInteger (Number.Formatting.cs FormatIntegerDigits), now that NumberFormatInfo is the framework's (#426). Bound at compile time
    /// (FunctionCodeGen.UnsafeAccessor.cs).
    /// </summary>
    internal static class NumberFormatInfoInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_numberGroupSizes")]
        internal static extern ref int[] NumberGroupSizes(NumberFormatInfo nfi);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_currencyGroupSizes")]
        internal static extern ref int[] CurrencyGroupSizes(NumberFormatInfo nfi);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_percentGroupSizes")]
        internal static extern ref int[] PercentGroupSizes(NumberFormatInfo nfi);
    }
}

namespace CsharpcCoreLib
{
    using System.Runtime.CompilerServices;
    using System.Threading;
    using CultureInfo = System.Globalization.CultureInfo;

    /// <summary>
    /// CultureInfo.CurrentCulture and CurrentUICulture (the slicer's csharpc profile points the framework's getters here, #426). CoreLib keeps
    /// the current thread's culture in a [ThreadStatic] field that an AsyncLocal&lt;CultureInfo&gt; change handler sets whenever the execution
    /// context switches (a thread starts, a work item or a continuation runs). csharpc's runtime switches its flows without running managed
    /// handlers, so the getters read the AsyncLocal itself: the same culture, flowing the same way (a thread or a task sees the culture its
    /// creator had set), then DefaultThreadCurrentCulture and the user's default, as CoreLib's getters fall back.
    /// </summary>
    public static class UpstreamCultureInfo
    {
        [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "s_asyncLocalCurrentCulture")]
        private static extern ref AsyncLocal<CultureInfo> AsyncLocalCurrentCulture(CultureInfo type);

        [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "s_asyncLocalCurrentUICulture")]
        private static extern ref AsyncLocal<CultureInfo> AsyncLocalCurrentUICulture(CultureInfo type);

        [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "s_userDefaultCulture")]
        private static extern ref CultureInfo UserDefaultCulture(CultureInfo type);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "InitializeUserDefaultCulture")]
        private static extern CultureInfo InitializeUserDefaultCulture(CultureInfo type);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "get_UserDefaultUICulture")]
        private static extern CultureInfo UserDefaultUICulture(CultureInfo type);

        public static CultureInfo GetCurrentCulture() =>
            AsyncLocalCurrentCulture(null)?.Value ?? CultureInfo.DefaultThreadCurrentCulture ?? UserDefaultCulture(null) ?? InitializeUserDefaultCulture(null);

        public static CultureInfo GetCurrentUICulture() =>
            AsyncLocalCurrentUICulture(null)?.Value ?? CultureInfo.DefaultThreadCurrentUICulture ?? UserDefaultUICulture(null);

        /// <summary>
        /// CultureInfo.GetFormat, as CoreLib's: the culture's NumberFormat or DateTimeFormat (both virtual) for those types, null otherwise.
        /// The DateTimeFormatInfo case goes through the runtime (csrt_culture_format_dtfi), whose real implementation is exported only by
        /// programs that use DateTimeFormatInfo (its method table is the trigger): formatting a number through the current culture would
        /// otherwise link DateTimeFormatInfo, every calendar and their data (half a megabyte); no other program can have its Type.
        /// </summary>
        public static object GetFormat(object self, Type formatType)
        {
            var culture = (CultureInfo)self;
            if (formatType == typeof(System.Globalization.NumberFormatInfo)) return culture.NumberFormat;
            IntPtr result = FormatDateTimeFormat(CoreIntrinsics.ObjectToPointer(culture), CoreIntrinsics.ObjectToPointer(formatType));
            return result == IntPtr.Zero ? null : CoreIntrinsics.PointerToObject(result);
        }

        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "csrt_culture_format_dtfi")]
        private static extern IntPtr FormatDateTimeFormat(IntPtr culture, IntPtr formatType);

        [RuntimeExport("csrt_culture_get_format_dtfi", "_CN14CsharpcCoreLib8Upstream6System13Globalization18DateTimeFormatInfo2MTE")]
        internal static object GetFormatDateTimeFormat(CultureInfo culture, Type formatType) =>
            formatType == typeof(System.Globalization.DateTimeFormatInfo) ? culture.DateTimeFormat : null;
    }
}
