// System.OperatingSystem's platform checks. The compiled program targets one OS (the corelib is parsed
// with CSHARPC_TARGET_*), so the answers are constants, as they are at run time under .NET. The
// version checks (IsWindowsVersionAtLeast, ...) need the running OS's version and aren't implemented
// yet: calling them is the usual "not supported" compile-time error.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.OperatingSystem")]
    internal static class OperatingSystemImpl
    {
#if CSHARPC_TARGET_WINDOWS
        private const string Platform = "WINDOWS";
#elif CSHARPC_TARGET_MACOS
        private const string Platform = "MACOS";
#elif CSHARPC_TARGET_WASI
        private const string Platform = "WASI";
#else
        private const string Platform = "LINUX";
#endif
        public static bool IsWindows() => Platform == "WINDOWS";
        public static bool IsLinux() => Platform == "LINUX";
        public static bool IsMacOS() => Platform == "MACOS";
        public static bool IsWasi() => Platform == "WASI";
        public static bool IsBrowser() => false;
        public static bool IsFreeBSD() => false;
        public static bool IsAndroid() => false;
        public static bool IsIOS() => false;
        public static bool IsMacCatalyst() => false;
        public static bool IsTvOS() => false;
        public static bool IsWatchOS() => false;

        /// <summary>
        /// OperatingSystem.IsWindowsVersionAtLeast: false off Windows, else the running version (RtlGetVersion, as .NET reads it)
        /// compared field by field. The framework's own Windows code (CNG, SSPI) gates features on it.
        /// </summary>
        public static bool IsWindowsVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0)
        {
#if CSHARPC_TARGET_WINDOWS
            var v = OSVersionMembers.WindowsVersion();
            if (v.Major != major) return v.Major > major;
            if (v.Minor != minor) return v.Minor > minor;
            if (v.Build != build) return v.Build > build;
            return true; // the revision (UBR) is not part of RtlGetVersion's answer; .NET reads it from the registry
#else
            return false;
#endif
        }

        /// <summary>
        /// OperatingSystem.IsMacOSVersionAtLeast (#99): false off macOS, else the running product version (kern.osproductversion,
        /// what NSProcessInfo reports and .NET compares) field by field.
        /// </summary>
        public static bool IsMacOSVersionAtLeast(int major, int minor = 0, int build = 0)
        {
#if CSHARPC_TARGET_MACOS
            var (m, n, p) = MacProductVersion();
            if (m != major) return m > major;
            if (n != minor) return n > minor;
            return p >= build;
#else
            return false;
#endif
        }

#if CSHARPC_TARGET_MACOS
        [System.Runtime.InteropServices.DllImport("libc")]
        private static extern unsafe int sysctlbyname(byte* name, byte* value, nuint* length, byte* newValue, nuint newLength);

        private static unsafe (int, int, int) MacProductVersion()
        {
            byte* value = stackalloc byte[64];
            nuint length = 64;
            fixed (byte* name = "kern.osproductversion\0"u8)
                if (sysctlbyname(name, value, &length, null, 0) != 0) return (0, 0, 0);
            value[63] = 0;
            string[] parts = Utf8.FromNative(value).Split('.');
            int Part(int i) => parts.Length > i && int.TryParse(parts[i], out int v) ? v : 0;
            return (Part(0), Part(1), Part(2));
        }
#endif

        /// <summary>.NET compares the name case-insensitively; macOS answers to "OSX" too.</summary>
        public static bool IsOSPlatform(string platform)
        {
            if (platform == null) throw new ArgumentNullException("platform");
            string p = platform.ToUpperInvariant();
            return p == Platform || (Platform == "MACOS" && p == "OSX");
        }
    }
}
