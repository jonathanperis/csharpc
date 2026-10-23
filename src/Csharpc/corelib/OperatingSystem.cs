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

        /// <summary>.NET compares the name case-insensitively; macOS answers to "OSX" too.</summary>
        public static bool IsOSPlatform(string platform)
        {
            if (platform == null) throw new ArgumentNullException("platform");
            string p = platform.ToUpperInvariant();
            return p == Platform || (Platform == "MACOS" && p == "OSX");
        }
    }
}
