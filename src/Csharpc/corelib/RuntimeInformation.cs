// System.Runtime.InteropServices.RuntimeInformation and OSPlatform, as dotnet/runtime computes them
// (RuntimeInformation.Unix.cs/.Windows.cs, ProductVersionInfoGenerator): the framework description is
// the product version of the runtime csharpc compiles against, the runtime identifier and architecture
// come from the target, and the OS description is read at run time.
#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Runtime.InteropServices.RuntimeInformation")]
    internal static unsafe class RuntimeInformationImpl
    {
#if CSHARPC_TARGET_WASM32
        public static Architecture ProcessArchitecture => Architecture.Wasm;
        private const string Arch = "wasm";
#elif CSHARPC_TARGET_ARM64
        public static Architecture ProcessArchitecture => Architecture.Arm64;
        private const string Arch = "arm64";
#else
        public static Architecture ProcessArchitecture => Architecture.X64;
        private const string Arch = "x64";
#endif
        // What the host (or NativeAOT's compiled-in configuration) passes as RUNTIME_IDENTIFIER.
#if CSHARPC_TARGET_WINDOWS
        public static string RuntimeIdentifier => "win-" + Arch;
#elif CSHARPC_TARGET_MACOS
        public static string RuntimeIdentifier => "osx-" + Arch;
#elif CSHARPC_TARGET_WASI
        public static string RuntimeIdentifier => "wasi-wasm";
#else
        public static string RuntimeIdentifier => "linux-" + Arch;
#endif

        /// <summary>The OS runs the process natively (csharpc emits no code for emulated architectures).</summary>
        public static Architecture OSArchitecture => ProcessArchitecture;

        public static string FrameworkDescription => ".NET " + CoreIntrinsics.FrameworkProductVersion();

        public static bool IsOSPlatform(OSPlatform osPlatform) => OperatingSystem.IsOSPlatform(osPlatform.ToString());

        private static string s_osDescription;

        public static string OSDescription => s_osDescription ??= ReadOSDescription();

#if CSHARPC_TARGET_WASI
        private static string ReadOSDescription() => "WASI";
#elif CSHARPC_TARGET_WINDOWS
        [DllImport("ntdll")] private static extern int RtlGetVersion(byte* info);

        /// <summary>RtlGetVersion's RTL_OSVERSIONINFOW, as Environment.OSVersion reads it.</summary>
        private static string ReadOSDescription()
        {
            byte* info = stackalloc byte[276];
            for (int i = 0; i < 276; i++) info[i] = 0;
            *(uint*)info = 276;
            if (RtlGetVersion(info) != 0) return "Microsoft Windows";
            uint major = *(uint*)(info + 4), minor = *(uint*)(info + 8), build = *(uint*)(info + 12);
            string servicePack = Utf16.FromNative((char*)(info + 20));
            string text = "Microsoft Windows " + major + "." + minor + "." + build;
            return string.IsNullOrEmpty(servicePack) ? text : text + " " + servicePack;
        }
#elif CSHARPC_TARGET_MACOS
        [DllImport("libc")] private static extern int sysctlbyname(byte* name, byte* value, nuint* length, byte* newValue, nuint newLength);

        /// <summary>"macOS " + Environment.OSVersion.Version: NSProcessInfo's major.minor.patch (kern.osproductversion).</summary>
        private static string ReadOSDescription()
        {
            byte* name = stackalloc byte[24];
            string key = "kern.osproductversion";
            for (int i = 0; i < key.Length; i++) name[i] = (byte)key[i];
            name[key.Length] = 0;
            byte* value = stackalloc byte[64];
            nuint length = 64;
            if (sysctlbyname(name, value, &length, null, 0) != 0) return "macOS";
            value[63] = 0;
            string[] parts = Utf8.FromNative(value).Split('.');
            int major = parts.Length > 0 && int.TryParse(parts[0], out int a) ? a : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out int b) ? b : 0;
            int patch = parts.Length > 2 && int.TryParse(parts[2], out int c) ? c : 0;
            return "macOS " + major + "." + minor + "." + patch;
        }
#else
        [DllImport("libc")] private static extern int uname(byte* buffer);

        /// <summary>/etc/os-release's PRETTY_NAME (else NAME[ VERSION]), else uname's "sysname release version".</summary>
        private static string ReadOSDescription() => PrettyName("/etc/os-release") ?? UnixVersion();

        private static string PrettyName(string path)
        {
            if (!File.Exists(path)) return null;
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return null; }
            string name = null, version = null;
            foreach (string line in lines)
            {
                if (Field(line, "PRETTY_NAME=") is { Length: > 0 } pretty) return pretty;
                if (Field(line, "NAME=") is { } n) name = n;
                else if (Field(line, "VERSION=") is { } v) version = v;
            }
            if (!string.IsNullOrEmpty(name)) return string.IsNullOrEmpty(version) ? name : name + " " + version;
            return null;
        }

        private static string Field(string line, string prefix)
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string value = line.Substring(prefix.Length);
            // Enclosing quotes are removed.
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[0] == value[value.Length - 1])
                value = value.Substring(1, value.Length - 2);
            return value;
        }

        private static string UnixVersion()
        {
            // struct utsname: sysname, nodename, release, version, machine, domainname, 65 bytes each.
            byte* buffer = stackalloc byte[6 * 65];
            if (uname(buffer) != 0) return "";
            return Utf8.FromNative(buffer) + " " + Utf8.FromNative(buffer + 2 * 65) + " " + Utf8.FromNative(buffer + 3 * 65);
        }
#endif
    }

    [Shadows("System.Runtime.InteropServices.OSPlatform")]
    public readonly struct OSPlatformImpl : IEquatable<OSPlatform>
    {
        private readonly string _osPlatform;

        private OSPlatformImpl(string osPlatform, bool _) { _osPlatform = osPlatform; }

        private static OSPlatform Make(string name) => (OSPlatform)(object)new OSPlatformImpl(name, false);

        public static OSPlatform FreeBSD => Make("FREEBSD");
        public static OSPlatform Linux => Make("LINUX");
        public static OSPlatform OSX => Make("OSX");
        public static OSPlatform Windows => Make("WINDOWS");

        public static OSPlatform Create(string osPlatform)
        {
            if (osPlatform == null) throw new ArgumentNullException("osPlatform");
            if (osPlatform.Length == 0) throw new ArgumentException("The value cannot be an empty string.", "osPlatform");
            return Make(osPlatform);
        }

        public bool Equals(OSPlatform other) => string.Equals(_osPlatform, other.ToString(), StringComparison.OrdinalIgnoreCase);
        public override bool Equals(object obj) => obj is OSPlatform other && Equals(other);
        public override int GetHashCode() => _osPlatform == null ? 0 : _osPlatform.GetHashCode(StringComparison.OrdinalIgnoreCase);
        public override string ToString() => _osPlatform ?? string.Empty;

        public static bool operator ==(OSPlatformImpl left, OSPlatformImpl right) => string.Equals(left._osPlatform, right._osPlatform, StringComparison.OrdinalIgnoreCase);
        public static bool operator !=(OSPlatformImpl left, OSPlatformImpl right) => !(left == right);
    }
}
