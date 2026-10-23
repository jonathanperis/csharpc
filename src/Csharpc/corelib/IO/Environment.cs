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

        public static string CurrentDirectory
        {
            get => DirectoryImpl.GetCurrentDirectory();
            set => DirectoryImpl.SetCurrentDirectory(value);
        }

        public static bool Is64BitProcess => true;
        public static bool Is64BitOperatingSystem => true;
        public static int SystemPageSize => 4096;

        public static int ExitCode
        {
            get => csrt_get_exit_code();
            set => csrt_set_exit_code(value);
        }
    }
}
