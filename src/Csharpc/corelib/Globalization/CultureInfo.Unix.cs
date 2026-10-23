// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173),
// src/libraries/System.Private.CoreLib/src/System/Globalization/CultureInfo.{Unix,Windows}.cs; see
// THIRD-PARTY-NOTICES.md. The user default culture: on Unix ICU's default locale (LC_ALL / LC_MESSAGES / LANG;
// on macOS the system locale when that is POSIX), on Windows the user's locale (GetLocaleInfoEx) and preferred
// UI language, as in .NET.
#nullable enable annotations

using System.Globalization;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System.Globalization;
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    public partial class CultureInfo : IFormatProvider
    {
#if CSHARPC_TARGET_WINDOWS
        internal static CultureInfo GetUserDefaultCulture()
        {
            if (GlobalizationMode.Invariant)
                return InvariantCulture;

            string? strDefault = GetUserDefaultLocaleName();

            return strDefault != null ?
                GetCultureByName(strDefault) :
                InvariantCulture;
        }

        private static unsafe CultureInfo GetUserDefaultUICulture()
        {
            if (GlobalizationMode.Invariant)
                return InvariantCulture;

            const uint MUI_LANGUAGE_NAME = 0x8;    // Use ISO language (culture) name convention
            uint langCount = 0;
            uint bufLen = 0;

            if (GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &langCount, null, &bufLen) != 0)
            {
                char[] languages = new char[bufLen];
                fixed (char* pLanguages = languages)
                {
                    if (GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &langCount, pLanguages, &bufLen) != 0)
                    {
                        // csharpc: the first name of the double-NUL-terminated list (.NET passes the whole
                        // buffer, whose name parsing stops at the first NUL).
                        int length = Array.IndexOf(languages, '\0');
                        return GetCultureByName(new string(languages, 0, length < 0 ? languages.Length : length));
                    }
                }
            }

            return InitializeUserDefaultCulture();
        }

        // .NET: CultureInfo.Windows.cs's UserDefaultLocaleName (a static initializer there, lazy here).
        private static string? s_userDefaultLocaleName;
        private static bool s_userDefaultLocaleNameRead;

        internal static string? UserDefaultLocaleName
        {
            get
            {
                if (!s_userDefaultLocaleNameRead)
                {
                    s_userDefaultLocaleName = GetUserDefaultLocaleName();
                    s_userDefaultLocaleNameRead = true;
                }
                return s_userDefaultLocaleName;
            }
        }

        private static string? GetUserDefaultLocaleName() =>
            GetLocaleInfoEx(null, LOCALE_SNAME) ?? GetLocaleInfoEx("!x-sys-default-locale", LOCALE_SNAME);

        private const uint LOCALE_SNAME = 0x0000005c;

        private static unsafe string? GetLocaleInfoEx(string? localeName, uint field)
        {
            char* buffer = stackalloc char[85]; // LOCALE_NAME_MAX_LENGTH
            int length = GetLocaleInfoExNative(localeName, field, buffer, 85);
            return length > 0 ? new string(buffer, 0, length - 1) : null;
        }

        [DllImport("kernel32", EntryPoint = "GetLocaleInfoEx", CharSet = CharSet.Unicode)]
        private static extern unsafe int GetLocaleInfoExNative(string? lpLocaleName, uint LCType, char* lpLCData, int cchData);

        [DllImport("kernel32", EntryPoint = "GetUserPreferredUILanguages")]
        private static extern unsafe int GetUserPreferredUILanguages(uint dwFlags, uint* pulNumLanguages, char* pwszLanguagesBuffer, uint* pcchLanguagesBuffer);
#else
        internal static CultureInfo GetUserDefaultCulture()
        {
            if (GlobalizationMode.Invariant)
                return CultureInfo.InvariantCulture;

            CultureInfo cultureInfo;
            if (CultureData.GetDefaultLocaleName(out string? localeName))
            {
                Debug.Assert(localeName != null);
                cultureInfo = GetCultureByName(localeName);
            }
            else
            {
                cultureInfo = CultureInfo.InvariantCulture;
            }

            return cultureInfo;
        }

        private static CultureInfo GetUserDefaultUICulture()
        {
            return InitializeUserDefaultCulture();
        }
#endif
    }
}
