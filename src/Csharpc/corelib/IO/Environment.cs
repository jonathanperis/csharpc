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

        public static string CurrentDirectory
        {
            get => DirectoryImpl.GetCurrentDirectory();
            set => DirectoryImpl.SetCurrentDirectory(value);
        }

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

        public static string UserName => GetEnvironmentVariable("USER") ?? "";
        public static int ProcessId => Posix.getpid();
        public static bool Is64BitProcess => true;
        public static bool Is64BitOperatingSystem => true;
        public static string CommandLine => string.Join(" ", Environment.GetCommandLineArgs());
        public static string SystemDirectory => "";
        public static int SystemPageSize => 4096;

        public static int ExitCode
        {
            get => csrt_get_exit_code();
            set => csrt_set_exit_code(value);
        }
    }
}
