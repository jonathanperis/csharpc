// System.Environment members backed by libc (the rest are intrinsics: NewLine, Exit, TickCount...).
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Environment")]
    internal static unsafe class EnvironmentExtras
    {
        [DllImport("__Internal")] private static extern int csrt_get_exit_code();
        [DllImport("__Internal")] private static extern void csrt_set_exit_code(int code);

#if CSHARPC_TARGET_WINDOWS
        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetEnvironmentVariableW(string name, char* buffer, uint size);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern int SetEnvironmentVariableW(string name, string value);
        [DllImport("kernel32")] private static extern int GetComputerNameW(char* buffer, uint* size);
        [DllImport("kernel32")] private static extern uint GetSystemDirectoryW(char* buffer, uint size);
        [DllImport("kernel32")] private static extern uint GetCurrentProcessId();
        [DllImport("kernel32")] private static extern char* GetCommandLineW();
        [DllImport("advapi32")] private static extern int GetUserNameW(char* buffer, uint* size);

        public static string GetEnvironmentVariable(string variable)
        {
            if (variable == null) throw new ArgumentNullException("variable");
            uint size = 256;
            while (true)
            {
                char[] buffer = new char[size];
                uint n;
                fixed (char* p = buffer) n = GetEnvironmentVariableW(variable, p, size);
                if (n == 0) return null;  // not set (an empty value reads as unset too, as in .NET)
                if (n < size) return new string(buffer, 0, (int)n);
                size = n;
            }
        }

        public static void SetEnvironmentVariable(string variable, string value)
        {
            if (variable == null) throw new ArgumentNullException("variable");
            if (variable.Length == 0) throw new ArgumentException("String cannot be of zero length. (Parameter 'variable')");
            SetEnvironmentVariableW(variable, string.IsNullOrEmpty(value) ? null : value);
        }

        public static string MachineName
        {
            get
            {
                char* buffer = stackalloc char[64];
                uint size = 64;
                return GetComputerNameW(buffer, &size) != 0 ? new string(buffer, 0, (int)size) : "localhost";
            }
        }

        public static string UserName
        {
            get
            {
                char* buffer = stackalloc char[257];
                uint size = 257;
                return GetUserNameW(buffer, &size) != 0 && size > 0 ? new string(buffer, 0, (int)size - 1) : "Windows User";
            }
        }

        public static int ProcessId => (int)GetCurrentProcessId();
        public static string CommandLine => Utf16.FromNative(GetCommandLineW());

        public static string SystemDirectory
        {
            get
            {
                char* buffer = stackalloc char[260];
                uint n = GetSystemDirectoryW(buffer, 260);
                return n > 0 && n < 260 ? new string(buffer, 0, (int)n) : "";
            }
        }
#else
        public static string GetEnvironmentVariable(string variable)
        {
            if (variable == null) throw new ArgumentNullException("variable");
            byte* name = Utf8.ToNative(variable);
            byte* value = Posix.getenv(name);
            Libc.free(name);
            return Utf8.FromNative(value);
        }

        public static void SetEnvironmentVariable(string variable, string value)
        {
            if (variable == null) throw new ArgumentNullException("variable");
            if (variable.Length == 0) throw new ArgumentException("String cannot be of zero length. (Parameter 'variable')");
            byte* name = Utf8.ToNative(variable);
            if (string.IsNullOrEmpty(value)) Posix.unsetenv(name);
            else
            {
                byte* v = Utf8.ToNative(value);
                Posix.setenv(name, v, 1);
                Libc.free(v);
            }
            Libc.free(name);
        }

        public static string UserName => GetEnvironmentVariable("USER") ?? "";
#if CSHARPC_TARGET_WASI
        // WASI has no process ids or host names: the id is the one wasi-libc's getpid emulation reports.
        public static int ProcessId => 42;
        public static string MachineName => "localhost";
#else
        public static int ProcessId => Posix.getpid();
#endif
        public static string CommandLine => string.Join(" ", Environment.GetCommandLineArgs());
        public static string SystemDirectory => "";

#if !CSHARPC_TARGET_WASI
        public static string MachineName
        {
            get
            {
                byte* buffer = stackalloc byte[256];
                if (Posix.gethostname(buffer, 256) != 0) return "localhost";
                buffer[255] = 0;
                string name = Utf8.FromNative(buffer);
                int dot = name.IndexOf('.');
                return dot > 0 ? name.Substring(0, dot) : name;
            }
        }
#endif
#endif

        private static string s_processPath;

        /// <summary>The executable's full path, as .NET reads it: /proc/self/exe on Linux, _NSGetExecutablePath on
        /// macOS (with symbolic links resolved), GetModuleFileNameW on Windows; null where there is none (WASI).</summary>
        public static string ProcessPath => s_processPath ??= ReadProcessPath();

#if CSHARPC_TARGET_WINDOWS
        [DllImport("kernel32")] private static extern uint GetModuleFileNameW(IntPtr module, char* buffer, uint size);

        private static string ReadProcessPath()
        {
            for (uint size = 260; ; size *= 2)
            {
                char[] buffer = new char[size];
                uint n;
                fixed (char* p = buffer) n = GetModuleFileNameW(IntPtr.Zero, p, size);
                if (n == 0) return null;
                if (n < size) return new string(buffer, 0, (int)n);
            }
        }
#elif CSHARPC_TARGET_WASI
        private static string ReadProcessPath() => null;
#elif CSHARPC_TARGET_MACOS
        [DllImport("libc")] private static extern int _NSGetExecutablePath(byte* buffer, uint* size);
        [DllImport("libc")] private static extern byte* realpath(byte* path, byte* resolved);

        private static string ReadProcessPath()
        {
            uint size = 1024;
            byte* buffer = stackalloc byte[1024];
            if (_NSGetExecutablePath(buffer, &size) != 0) return null;
            byte* resolved = realpath(buffer, null);
            if (resolved == null) return Utf8.FromNative(buffer);
            string path = Utf8.FromNative(resolved);
            Libc.free(resolved);
            return path;
        }
#else
        [DllImport("libc")] private static extern nint readlink(byte* path, byte* buffer, nuint size);

        private static string ReadProcessPath()
        {
            byte* link = Utf8.ToNative("/proc/self/exe");
            try
            {
                for (int size = 256; size <= 65536; size *= 2)
                {
                    byte[] buffer = new byte[size];
                    nint n;
                    fixed (byte* p = buffer) n = readlink(link, p, (nuint)size);
                    if (n < 0) return null;
                    if (n < size) return System.Text.Encoding.UTF8.GetString(buffer, 0, (int)n);
                }
                return null;
            }
            finally { Libc.free(link); }
        }
#endif

        public static string CurrentDirectory
        {
            get => DirectoryImpl.GetCurrentDirectory();
            set => DirectoryImpl.SetCurrentDirectory(value);
        }

        public static bool Is64BitProcess => true;
        public static bool Is64BitOperatingSystem => true;
        public static int SystemPageSize => 4096;

        /// <summary>The runtime's product version without its prerelease label ("11.0.0-rc.2..." → 11.0.0), as .NET compiles it in.</summary>
        public static Version Version
        {
            get
            {
                string product = CoreIntrinsics.FrameworkProductVersion();
                int dash = product.IndexOf('-');
                string[] parts = (dash >= 0 ? product.Substring(0, dash) : product).Split('.');
                int major = int.Parse(parts[0]), minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                if (parts.Length > 3) return new Version(major, minor, int.Parse(parts[2]), int.Parse(parts[3]));
                if (parts.Length > 2) return new Version(major, minor, int.Parse(parts[2]));
                return new Version(major, minor);
            }
        }

        public static int ExitCode
        {
            get => csrt_get_exit_code();
            set => csrt_set_exit_code(value);
        }
    }
}
