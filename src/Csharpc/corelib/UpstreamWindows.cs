// What the Windows CoreLib's globalization asks Windows itself in ICU mode (#424, #426): .NET on Windows uses ICU for culture data and
// collation, but still checks a locale name with GetLocaleInfoEx after ICU accepted it, reads the user's regional overrides and code pages,
// some calendar data (GetCalendarInfoEx), the user's region (GetUserGeoID/GetGeoInfo) and UI languages, LCID names, and the Hijri
// calendar's AddHijriDate registry setting. The slicer's csharpc profile points those Interop.Kernel32 calls of CoreLib's IL here
// (tools/il-first/slicer/Program.cs); the NLS back end itself (GlobalizationMode.UseNls) is folded out of the slice. Only the Windows
// CoreLib reaches this class, so nothing here is lowered for another target.
//
// Security: each import receives the caller's own buffer and the length CoreLib computed for it (its stackalloc or pinned array); strings
// go in NUL-terminated (fixed on a string). The registry read sizes its buffer from what RegQueryValueExW reports and never trusts a length
// beyond the buffer it allocated.
#nullable disable
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    /// <summary>Interop.BOOL (a Win32 BOOL), top-level so the slice's references to it can be redirected here.</summary>
    public enum UpstreamWin32Bool : int
    {
        FALSE = 0,
        TRUE = 1,
    }

    /// <summary>The members of CoreLib's Interop.Kernel32 the Windows slice calls, with CoreLib's signatures.</summary>
    public static unsafe class UpstreamWindowsInterop
    {
        [DllImport("kernel32", EntryPoint = "GetLocaleInfoEx")]
        private static extern int GetLocaleInfoExW(char* lpLocaleName, uint LCType, void* lpLCData, int cchData);

        [DllImport("kernel32", EntryPoint = "GetCalendarInfoEx")]
        private static extern int GetCalendarInfoExW(char* lpLocaleName, uint Calendar, IntPtr lpReserved, uint CalType, IntPtr lpCalData, int cchData, int* lpValue);

        [DllImport("kernel32", EntryPoint = "GetCalendarInfoEx")]
        private static extern int GetCalendarInfoExW(char* lpLocaleName, uint Calendar, IntPtr lpReserved, uint CalType, IntPtr lpCalData, int cchData, IntPtr lpValue);

        [DllImport("kernel32", EntryPoint = "GetGeoInfoW")]
        private static extern int GetGeoInfoW(int location, int geoType, char* lpGeoData, int cchData, int LangId);

        [DllImport("kernel32", EntryPoint = "GetUserGeoID")]
        private static extern int GetUserGeoIDW(int geoClass);

        [DllImport("kernel32", EntryPoint = "GetUserPreferredUILanguages")]
        private static extern int GetUserPreferredUILanguagesW(uint dwFlags, uint* pulNumLanguages, char* pwszLanguagesBuffer, uint* pcchLanguagesBuffer);

        [DllImport("kernel32", EntryPoint = "LCIDToLocaleName")]
        private static extern int LCIDToLocaleNameW(int locale, char* pName, int cchName, uint dwFlags);

        [DllImport("kernel32", EntryPoint = "MultiByteToWideChar")]
        private static extern int MultiByteToWideCharW(uint CodePage, uint dwFlags, byte* lpMultiByteStr, int cbMultiByte, char* lpWideCharStr, int cchWideChar);

        public static int GetLocaleInfoEx(string lpLocaleName, uint LCType, void* lpLCData, int cchData)
        {
            fixed (char* name = lpLocaleName) return GetLocaleInfoExW(name, LCType, lpLCData, cchData);
        }

        public static int GetCalendarInfoEx(string lpLocaleName, uint Calendar, IntPtr lpReserved, uint CalType, IntPtr lpCalData, int cchData, out int lpValue)
        {
            int value = 0;
            int result;
            fixed (char* name = lpLocaleName) result = GetCalendarInfoExW(name, Calendar, lpReserved, CalType, lpCalData, cchData, &value);
            lpValue = value;
            return result;
        }

        public static int GetCalendarInfoEx(string lpLocaleName, uint Calendar, IntPtr lpReserved, uint CalType, IntPtr lpCalData, int cchData, IntPtr lpValue)
        {
            fixed (char* name = lpLocaleName) return GetCalendarInfoExW(name, Calendar, lpReserved, CalType, lpCalData, cchData, lpValue);
        }

        public static int GetGeoInfo(int location, int geoType, char* lpGeoData, int cchData, int LangId) => GetGeoInfoW(location, geoType, lpGeoData, cchData, LangId);

        public static int GetUserGeoID(int geoClass) => GetUserGeoIDW(geoClass);

        public static UpstreamWin32Bool GetUserPreferredUILanguages(uint dwFlags, uint* pulNumLanguages, char* pwszLanguagesBuffer, uint* pcchLanguagesBuffer) =>
            (UpstreamWin32Bool)GetUserPreferredUILanguagesW(dwFlags, pulNumLanguages, pwszLanguagesBuffer, pcchLanguagesBuffer);

        public static int LCIDToLocaleName(int locale, char* pName, int cchName, uint dwFlags) => LCIDToLocaleNameW(locale, pName, cchName, dwFlags);

        public static int MultiByteToWideChar(uint CodePage, uint dwFlags, byte* lpMultiByteStr, int cbMultiByte, char* lpWideCharStr, int cchWideChar) =>
            MultiByteToWideCharW(CodePage, dwFlags, lpMultiByteStr, cbMultiByte, lpWideCharStr, cchWideChar);

        // ---- HijriCalendar's AddHijriDate (CoreLib's HijriCalendar.Win32.cs, over the registry API instead of Internal.Win32.RegistryKey) ----

        private const string InternationalRegKey = "Control Panel\\International";
        private const string HijriAdvanceRegKeyEntry = "AddHijriDate";
        private const int MinAdvancedHijri = -2, MaxAdvancedHijri = 2;
        private static readonly IntPtr HKEY_CURRENT_USER = unchecked((IntPtr)(int)0x80000001);
        private const int KEY_READ = 0x20019, ERROR_SUCCESS = 0, ERROR_MORE_DATA = 234;
        private const int REG_SZ = 1, REG_EXPAND_SZ = 2;

        [DllImport("advapi32", EntryPoint = "RegOpenKeyExW")]
        private static extern int RegOpenKeyExW(IntPtr hKey, char* lpSubKey, int ulOptions, int samDesired, IntPtr* phkResult);

        [DllImport("advapi32", EntryPoint = "RegQueryValueExW")]
        private static extern int RegQueryValueExW(IntPtr hKey, char* lpValueName, int* lpReserved, int* lpType, byte* lpData, uint* lpcbData);

        [DllImport("advapi32", EntryPoint = "RegCloseKey")]
        private static extern int RegCloseKey(IntPtr hKey);

        /// <summary>
        /// HKCU\Control Panel\International\AddHijriDate: "AddHijriDate" alone is -1, "AddHijriDate" followed by an integer in [-2, 2] is that
        /// integer, anything else 0. Only a string value can match (RegistryKey.GetValue(...).ToString() of a number or an array never starts
        /// with the entry's name), so other value types read as 0, as in .NET.
        /// </summary>
        public static int GetAdvanceHijriDate()
        {
            IntPtr key;
            fixed (char* subKey = InternationalRegKey)
                if (RegOpenKeyExW(HKEY_CURRENT_USER, subKey, 0, KEY_READ, &key) != ERROR_SUCCESS) return 0;
            string text;
            try
            {
                text = ReadString(key, HijriAdvanceRegKeyEntry);
            }
            finally
            {
                RegCloseKey(key);
            }
            if (text is null || !text.StartsWith(HijriAdvanceRegKeyEntry, StringComparison.OrdinalIgnoreCase)) return 0;
            if (text.Length == HijriAdvanceRegKeyEntry.Length) return -1;
            try
            {
                int advance = int.Parse(text.AsSpan(HijriAdvanceRegKeyEntry.Length), provider: CultureInfo.InvariantCulture);
                return advance >= MinAdvancedHijri && advance <= MaxAdvancedHijri ? advance : 0;
            }
            catch (ArgumentException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            return 0;
        }

        /// <summary>A REG_SZ / REG_EXPAND_SZ value as RegistryKey.GetValue gives it (its terminating NUL dropped, REG_EXPAND_SZ expanded); null otherwise.</summary>
        private static string ReadString(IntPtr key, string name)
        {
            fixed (char* valueName = name)
            {
                int type = 0;
                uint size = 0;
                if (RegQueryValueExW(key, valueName, null, &type, null, &size) != ERROR_SUCCESS || (type != REG_SZ && type != REG_EXPAND_SZ)) return null;
                while (true)
                {
                    // Whole chars, at least one; the value can grow between the two calls (ERROR_MORE_DATA: ask again with the new size).
                    var buffer = new char[Math.Max(1, ((int)Math.Min(size, int.MaxValue - 1) + 1) / 2)];
                    uint bytes = (uint)buffer.Length * 2;
                    int status;
                    fixed (char* data = buffer) status = RegQueryValueExW(key, valueName, null, &type, (byte*)data, &bytes);
                    if (status == ERROR_MORE_DATA) { size = bytes; continue; }
                    if (status != ERROR_SUCCESS || (type != REG_SZ && type != REG_EXPAND_SZ)) return null;
                    int length = (int)Math.Min(bytes / 2, (uint)buffer.Length);
                    if (length > 0 && buffer[length - 1] == '\0') length--;
                    string text = new string(buffer, 0, length);
                    return type == REG_EXPAND_SZ ? Environment.ExpandEnvironmentVariables(text) : text;
                }
            }
        }
    }
}
