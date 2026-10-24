// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173),
// src/libraries/System.Private.CoreLib/src/System/Globalization/CultureData.{Windows,Nls}.cs; see
// THIRD-PARTY-NOTICES.md. csharpc: the ICU configuration only (no NLS mode): with ICU on Windows, .NET
// still reads the *user default* culture's number, currency, time and calendar settings from NLS
// (GetLocaleInfoEx), which is where Windows keeps the user's regional settings, and only those.
#nullable enable annotations
#if CSHARPC_TARGET_WINDOWS

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Text;
    internal sealed partial class CultureData
    {
        private const uint LOCALE_NOUSEROVERRIDE = 0x80000000;
        private const uint LOCALE_RETURN_NUMBER = 0x20000000;
        private const uint LOCALE_ALLOW_NEUTRAL_NAMES = 0x08000000;
        private const uint LOCALE_SNAME = 0x0000005C;
        private const uint LOCALE_STIMEFORMAT = 0x00001003;
        private const uint LOCALE_SSHORTTIME = 0x00000079;
        private const uint LOCALE_IFIRSTDAYOFWEEK = 0x0000100C;
        private const int LOCALE_NAME_MAX_LENGTH = 85;
        private const int GEOCLASS_NATION = 16;
        private const int GEOID_NOT_AVAILABLE = -1;
        private const int GEO_ISO2 = 4;

        private unsafe bool InitCultureDataCore()
        {
            char* pBuffer = stackalloc char[LOCALE_NAME_MAX_LENGTH];
            return InitIcuCultureDataCore() &&
                   GetLocaleInfoEx(_sRealName, LOCALE_SNAME, pBuffer, LOCALE_NAME_MAX_LENGTH) != 0; // Ensure the culture name is supported by Windows.
        }

        partial void InitUserOverride(bool useUserOverride);

        partial void InitUserOverride(bool useUserOverride)
        {
            Debug.Assert(_sWindowsName != null, "[CultureData.InitUserOverride] Expected _sWindowsName to be populated by already");
            _bUseOverrides = useUserOverride && _sWindowsName == CultureInfo.UserDefaultLocaleName;
        }

        internal static bool IsWin32Installed => true;

        internal static unsafe CultureData GetCurrentRegionData()
        {
            char* geoIso2Letters = stackalloc char[10];

            int geoId = GetUserGeoID(GEOCLASS_NATION);
            if (geoId != GEOID_NOT_AVAILABLE)
            {
                int geoIsoIdLength = GetGeoInfo(geoId, GEO_ISO2, geoIso2Letters, 10, 0);
                if (geoIsoIdLength != 0)
                {
                    geoIsoIdLength -= geoIso2Letters[geoIsoIdLength - 1] == 0 ? 1 : 0; // handle null termination and exclude it.
                    CultureData? cd = GetCultureDataForRegion(new string(geoIso2Letters, 0, geoIsoIdLength), true);
                    if (cd != null)
                    {
                        return cd;
                    }
                }
            }

            // Fallback to current locale data.
            return CultureInfo.CurrentCulture._cultureData;
        }

        private static unsafe string? LCIDToLocaleName(int culture)
        {
            Debug.Assert(!GlobalizationMode.Invariant);

            // LOCALE_CUSTOM_UNSPECIFIED ia an unspecified custom locale, used to identify all supplemental locales.
            if (culture == CultureInfo.LOCALE_CUSTOM_UNSPECIFIED)
            {
                return null;
            }

            char* pBuffer = stackalloc char[LOCALE_NAME_MAX_LENGTH + 1]; // +1 for the null termination
            int length = LCIDToLocaleNameNative(culture, pBuffer, LOCALE_NAME_MAX_LENGTH + 1, LOCALE_ALLOW_NEUTRAL_NAMES);

            if (length > 0)
            {
                return new string(pBuffer, 0, length - 1);
            }

            return null;
        }

        private string[]? GetTimeFormatsCore(bool shortFormat)
        {
            Debug.Assert(_sWindowsName != null, "[CultureData.GetTimeFormatsCore] Expected _sWindowsName to be populated by already");

            string icuFormatString = IcuGetTimeFormatString(shortFormat);

            if (!_bUseOverrides)
            {
                return new string[] { icuFormatString };
            }

            // When using ICU and need to get user overrides, we put the user override at the beginning
            string userOverride = GetLocaleInfoFromLCType(_sWindowsName, shortFormat ? LOCALE_SSHORTTIME : LOCALE_STIMEFORMAT, useUserOverride: true);

            Debug.Assert(!string.IsNullOrEmpty(userOverride));

            return userOverride != icuFormatString ?
                 new string[] { userOverride, icuFormatString } : new string[] { userOverride };
        }

        private int GetAnsiCodePage(string _ /*cultureName*/) =>
            NlsGetLocaleInfo(LocaleNumberData.AnsiCodePage);

        private int GetOemCodePage(string _ /*cultureName*/) =>
            NlsGetLocaleInfo(LocaleNumberData.OemCodePage);

        private int GetMacCodePage(string _ /*cultureName*/) =>
            NlsGetLocaleInfo(LocaleNumberData.MacCodePage);

        private int GetEbcdicCodePage(string _ /*cultureName*/) =>
            NlsGetLocaleInfo(LocaleNumberData.EbcdicCodePage);

        // If we are using ICU and loading the calendar data for the user's default
        // local, and we're using user overrides, then we use NLS to load the data
        // in order to get the user overrides from the OS.
        private bool ShouldUseUserOverrideNlsData => _bUseOverrides;

        // ---- CultureData.Nls.cs -------------------------------------------------------------

        // Wrappers around the GetLocaleInfoEx APIs which handle marshalling the returned
        // data as either and Int or string.
        internal static unsafe string? GetLocaleInfoEx(string localeName, uint field)
        {
            const int BUFFER_SIZE = 530;

            char* pBuffer = stackalloc char[BUFFER_SIZE];
            int resultCode = GetLocaleInfoEx(localeName, field, pBuffer, BUFFER_SIZE);
            if (resultCode > 0)
            {
                return new string(pBuffer, 0, resultCode - 1);
            }

            return null;
        }

        internal static unsafe int GetLocaleInfoExInt(string localeName, uint field)
        {
            field |= LOCALE_RETURN_NUMBER;
            int value = 0;
            GetLocaleInfoEx(localeName, field, (char*)&value, sizeof(int));
            return value;
        }

        internal static unsafe int GetLocaleInfoEx(string lpLocaleName, uint lcType, char* lpLCData, int cchData)
        {
            Debug.Assert(!GlobalizationMode.Invariant);

            return GetLocaleInfoExNative(lpLocaleName, lcType, lpLCData, cchData);
        }

        private string NlsGetLocaleInfo(LocaleStringData type)
        {
            Debug.Assert(ShouldUseUserOverrideNlsData);
            Debug.Assert(_sRealName != null, "[CultureData.DoGetLocaleInfo] Expected _sRealName to be populated by already");
            return NlsGetLocaleInfo(_sRealName, type);
        }

        private string NlsGetLocaleInfo(string localeName, LocaleStringData type)
        {
            Debug.Assert(ShouldUseUserOverrideNlsData);
            uint lctype = (uint)type;

            return GetLocaleInfoFromLCType(localeName, lctype, _bUseOverrides);
        }

        private int NlsGetLocaleInfo(LocaleNumberData type)
        {
            uint lctype = (uint)type;

            // Fix lctype if we don't want overrides
            if (!_bUseOverrides)
            {
                lctype |= LOCALE_NOUSEROVERRIDE;
            }

            Debug.Assert(_sRealName != null, "[CultureData.DoGetLocaleInfoInt] Expected _sRealName to be populated by already");
            return GetLocaleInfoExInt(_sRealName, lctype);
        }

        private int[] NlsGetLocaleInfo(LocaleGroupingData type)
        {
            Debug.Assert(ShouldUseUserOverrideNlsData);
            Debug.Assert(_sRealName != null, "[CultureData.DoGetLocaleInfoInt] Expected _sRealName to be populated by already");
            return ConvertWin32GroupString(GetLocaleInfoFromLCType(_sRealName, (uint)type, _bUseOverrides));
        }

        private string? NlsGetTimeFormatString()
        {
            Debug.Assert(ShouldUseUserOverrideNlsData);
            Debug.Assert(_sRealName != null, "[CultureData.DoGetLocaleInfoInt] Expected _sRealName to be populated by already");
            return ReescapeWin32String(GetLocaleInfoFromLCType(_sRealName, LOCALE_STIMEFORMAT, _bUseOverrides));
        }

        private int NlsGetFirstDayOfWeek()
        {
            Debug.Assert(ShouldUseUserOverrideNlsData);
            Debug.Assert(_sRealName != null, "[CultureData.DoGetLocaleInfoInt] Expected _sRealName to be populated by already");

            int result = GetLocaleInfoExInt(_sRealName, LOCALE_IFIRSTDAYOFWEEK | (!_bUseOverrides ? LOCALE_NOUSEROVERRIDE : 0));

            // Win32 and .NET disagree on the numbering for days of the week, so we have to convert.
            return ConvertFirstDayOfWeekMonToSun(result);
        }

        private static string GetLocaleInfoFromLCType(string localeName, uint lctype, bool useUserOverride)
        {
            Debug.Assert(localeName != null, "[CultureData.GetLocaleInfoFromLCType] Expected localeName to be not be null");

            // Fix lctype if we don't want overrides
            if (!useUserOverride)
            {
                lctype |= LOCALE_NOUSEROVERRIDE;
            }

            // Ask OS for data
            // Failed? Just use empty string
            return GetLocaleInfoEx(localeName, lctype) ?? string.Empty;
        }

        /// <summary>
        /// Reescape a Win32 style quote string as a NLS+ style quoted string: \ becomes \\, and ''
        /// inside a quoted string becomes \'.
        /// </summary>
        internal static string? ReescapeWin32String(string? str)
        {
            // If we don't have data, then don't try anything
            if (str == null)
            {
                return null;
            }

            StringBuilder? result = null;

            bool inQuote = false;
            for (int i = 0; i < str.Length; i++)
            {
                // Look for quote
                if (str[i] == '\'')
                {
                    // Already in quote?
                    if (inQuote)
                    {
                        // See another single quote.  Is this '' of 'fred''s' or '''', or is it an ending quote?
                        if (i + 1 < str.Length && str[i + 1] == '\'')
                        {
                            // Found another ', so we have ''.  Need to add \' instead.
                            result ??= new StringBuilder(str, 0, i, str.Length * 2);

                            // Append a \' and keep going (so we don't turn off quote mode)
                            result.Append("\\'");
                            i++;
                            continue;
                        }

                        // Turning off quote mode, fall through to add it
                        inQuote = false;
                    }
                    else
                    {
                        // Found beginning quote, fall through to add it
                        inQuote = true;
                    }
                }
                // Is there a single \ character?
                else if (str[i] == '\\')
                {
                    // Found a \, need to change it to \\
                    result ??= new StringBuilder(str, 0, i, str.Length * 2);

                    // Append our \\ to the string & continue
                    result.Append("\\\\");
                    continue;
                }

                // If we have a builder we need to add our character
                result?.Append(str[i]);
            }

            // Unchanged string? , just return input string
            if (result == null)
                return str;

            // String changed, need to use the builder
            return result.ToString();
        }

        internal static string[]? ReescapeWin32Strings(string[]? array)
        {
            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = ReescapeWin32String(array[i])!;
                }
            }

            return array;
        }

        // If we get a group from windows, then its in 3;0 format with the 0 backwards
        // of how NLS+ uses it (ie: if the string has a 0, then the int[] shouldn't and vice versa)
        // EXCEPT in the case where the list only contains 0 in which NLS and NLS+ have the same meaning.
        private static int[] ConvertWin32GroupString(string win32Str)
        {
            // None of these cases make any sense
            if (string.IsNullOrEmpty(win32Str))
            {
                return new int[] { 3 };
            }

            if (win32Str[0] == '0')
            {
                return new int[] { 0 };
            }

            // Since its in n;n;n;n;n format, we can always get the length quickly
            int[] values;
            if (win32Str[win32Str.Length - 1] == '0')
            {
                // Trailing 0 gets dropped. 1;0 -> 1
                values = new int[win32Str.Length / 2];
            }
            else
            {
                // Need extra space for trailing zero 1 -> 1;0
                values = new int[(win32Str.Length / 2) + 2];
                values[values.Length - 1] = 0;
            }

            int i;
            int j;
            for (i = 0, j = 0; i < win32Str.Length && j < values.Length; i += 2, j++)
            {
                // Note that this # shouldn't ever be zero, 'cause 0 is only at end
                // But we'll test because its registry that could be anything
                if (win32Str[i] < '1' || win32Str[i] > '9')
                    return new int[] { 3 };

                values[j] = (int)(win32Str[i] - '0');
            }

            return values;
        }

        private static int ConvertFirstDayOfWeekMonToSun(int iTemp)
        {
            // Convert Mon-Sun to Sun-Sat format
            iTemp++;
            if (iTemp > 6)
            {
                // Wrap Sunday and convert invalid data to Sunday
                iTemp = 0;
            }
            return iTemp;
        }

        [DllImport("kernel32", EntryPoint = "GetLocaleInfoEx", CharSet = CharSet.Unicode)]
        private static extern unsafe int GetLocaleInfoExNative(string? lpLocaleName, uint LCType, char* lpLCData, int cchData);

        [DllImport("kernel32", EntryPoint = "LCIDToLocaleName")]
        private static extern unsafe int LCIDToLocaleNameNative(int locale, char* pLocaleName, int cchName, uint dwFlags);

        [DllImport("kernel32", EntryPoint = "GetUserGeoID")]
        private static extern int GetUserGeoID(int geoClass);

        [DllImport("kernel32", EntryPoint = "GetGeoInfoW")]
        private static extern unsafe int GetGeoInfo(int location, int geoType, char* lpGeoData, int cchData, int LangId);
    }
}
#endif
