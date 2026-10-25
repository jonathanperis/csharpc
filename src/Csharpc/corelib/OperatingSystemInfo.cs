// System.OperatingSystem objects (Environment.OSVersion) and RuntimeInformation.OSDescription, after
// .NET's Environment.OSVersion.Unix.cs: uname's release parsed for its first four numbers, and
// "sysname release version" as System.Native's GetUnixVersion formats it.
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.OperatingSystem")]
    public sealed class OperatingSystemInstance : ICloneable
    {
        private readonly Version _version;
        private readonly PlatformID _platform;
        private readonly string _servicePack;
        private string _versionString;

        public OperatingSystemInstance(PlatformID platform, Version version) : this(platform, version, null) { }

        internal OperatingSystemInstance(PlatformID platform, Version version, string servicePack)
        {
            if (platform < PlatformID.Win32S || platform > PlatformID.Other)
                throw new ArgumentOutOfRangeException("platform", platform, "Illegal enum value: " + (int)platform + ".");
            ArgumentNullException.ThrowIfNull(version);
            _platform = platform;
            _version = version;
            _servicePack = servicePack;
        }

        public PlatformID Platform => _platform;
        public string ServicePack => _servicePack ?? string.Empty;
        public Version Version => _version;
        public object Clone() => new OperatingSystemInstance(_platform, _version, _servicePack);
        public override string ToString() => VersionString;

        public string VersionString
        {
            get
            {
                if (_versionString != null) return _versionString;
                string os = _platform switch
                {
                    PlatformID.Win32S => "Microsoft Win32S ",
                    PlatformID.Win32Windows => _version.Major > 4 || (_version.Major == 4 && _version.Minor > 0) ? "Microsoft Windows 98 " : "Microsoft Windows 95 ",
                    PlatformID.Win32NT => "Microsoft Windows NT ",
                    PlatformID.WinCE => "Microsoft Windows CE ",
                    PlatformID.Unix => "Unix ",
                    PlatformID.Xbox => "Xbox ",
                    PlatformID.MacOSX => "Mac OS X ",
                    _ => "<unknown> ",
                };
                return _versionString = string.IsNullOrEmpty(_servicePack) ? os + _version.ToString() : os + _version.ToString(3) + " " + _servicePack;
            }
        }
    }

    [ShadowMembers("System.Environment")]
    internal static unsafe class OSVersionMembers
    {
        private static OperatingSystem s_osVersion;

        public static OperatingSystem OSVersion => s_osVersion ??= Create();

        private static OperatingSystem Create()
        {
#if CSHARPC_TARGET_WINDOWS || CSHARPC_TARGET_WASI
            throw new NotSupportedException("Environment.OSVersion on this platform is not supported by csharpc yet.");
#else
            string release = UnameField(2);
            int i = 0;
            int major = Next(release, ref i), minor = Next(release, ref i), build = Next(release, ref i), revision = Next(release, ref i);
            return (OperatingSystem)(object)new OperatingSystemInstance(PlatformID.Unix, new Version(major, minor, build, revision));
#endif
        }

        private static int Next(string text, ref int pos)
        {
            while (pos < text.Length && !char.IsAsciiDigit(text[pos])) pos++;
            int num = 0;
            for (; pos < text.Length && char.IsAsciiDigit(text[pos]); pos++)
            {
                if (num > (int.MaxValue - (text[pos] - '0')) / 10) { while (pos < text.Length && char.IsAsciiDigit(text[pos])) pos++; return int.MaxValue; }
                num = num * 10 + (text[pos] - '0');
            }
            return num;
        }

#if !CSHARPC_TARGET_WINDOWS && !CSHARPC_TARGET_WASI
        [DllImport("libc")] private static extern int uname(byte* buffer);

        /// <summary>struct utsname: sysname, nodename, release, version, machine (65 bytes each on Linux, 256 on macOS).</summary>
        internal static string UnameField(int index)
        {
#if CSHARPC_TARGET_MACOS
            const int FieldSize = 256;
#else
            const int FieldSize = 65;
#endif
            byte* buffer = stackalloc byte[FieldSize * 6];
            if (uname(buffer) != 0) return string.Empty;
            return Utf8.FromNative(buffer + index * FieldSize);
        }
#endif
    }
}
