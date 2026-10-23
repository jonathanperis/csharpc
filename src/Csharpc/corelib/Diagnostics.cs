// System.Diagnostics.Debug: its members are [Conditional("DEBUG")], so Roslyn drops the calls when the
// program is built without DEBUG (Release); in Debug builds they behave like .NET's DebugProvider:
// Write/WriteLine/Print go to an attached debugger only (nothing on stdout or stderr), and a failed
// assertion prints "Process terminated. Assertion failed." with the messages to stderr and fails fast
// (SIGABRT, exit status 134, on Unix). Windows: the fail-fast exit status doesn't match .NET's yet
// (tests/platform-skips.txt).
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Diagnostics.Debug")]
    public static unsafe class DebugImpl
    {
        public static bool AutoFlush { get; set; }
        public static int IndentLevel { get; set; }
        public static int IndentSize { get; set; } = 4;

        public static void Assert(bool condition) { if (!condition) FailCore("", null); }
        public static void Assert(bool condition, string message) { if (!condition) FailCore(message, null); }
        public static void Assert(bool condition, string message, string detailMessage) { if (!condition) FailCore(message, detailMessage); }

        public static void Assert(bool condition, string message, string detailMessageFormat, params object[] args)
        {
            if (!condition) FailCore(message, string.Format(detailMessageFormat, args));
        }

        public static void Fail(string message) => FailCore(message, null);
        public static void Fail(string message, string detailMessage) => FailCore(message, detailMessage);

        // Debugger output only: without an attached debugger .NET prints nothing.
        public static void Write(string message) { }
        public static void Write(object value) { }
        public static void Write(string message, string category) { }
        public static void Write(object value, string category) { }
        public static void WriteLine(string message) { }
        public static void WriteLine(object value) { }
        public static void WriteLine(string message, string category) { }
        public static void WriteLine(object value, string category) { }
        public static void WriteLine(string format, params object[] args) { }
        public static void Print(string message) { }
        public static void Print(string format, params object[] args) { }
        public static void WriteIf(bool condition, string message) { }
        public static void WriteIf(bool condition, object value) { }
        public static void WriteLineIf(bool condition, string message) { }
        public static void WriteLineIf(bool condition, object value) { }
        public static void Indent() => IndentLevel++;
        public static void Unindent() { if (IndentLevel > 0) IndentLevel--; }
        public static void Flush() { }
        public static void Close() { }

        private static void FailCore(string message, string detailMessage)
        {
            Console.Out.Flush();
            var text = new System.Text.StringBuilder("Process terminated.").Append(Environment.NewLine)
                .Append("Assertion failed.").Append(Environment.NewLine);
            if (!string.IsNullOrEmpty(message)) text.Append(message).Append(Environment.NewLine);
            if (!string.IsNullOrEmpty(detailMessage)) text.Append(detailMessage).Append(Environment.NewLine);
            Console.Error.Write(text.ToString());
            Console.Error.Flush();
            FailFast();
        }

#if CSHARPC_TARGET_WINDOWS
        [DllImport("kernel32")] private static extern void ExitProcess(uint exitCode);
        private static void FailFast() => ExitProcess(0xC0000409);  // as RaiseFailFastException reports it
#else
        [DllImport("libc")] private static extern void abort();
        private static void FailFast() => abort();
#endif
    }
}
