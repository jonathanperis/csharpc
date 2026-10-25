// System.Diagnostics.Debug: its members are [Conditional("DEBUG")], so Roslyn drops the calls when the
// program is built without DEBUG (Release); in Debug builds they go through the DebugProvider
// (DebugProvider.cs), as in .NET: Write/WriteLine/Print reach an attached debugger only (nothing on
// stdout or stderr) unless System.Diagnostics.Trace installed its provider, and a failed assertion
// prints "Process terminated. Assertion failed." with the messages to stderr and fails fast (SIGABRT,
// exit status 134, on Unix). Windows: the fail-fast exit status doesn't match .NET's yet
// (tests/platform-skips.txt).
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Diagnostics.Debug")]
    public static unsafe class DebugImpl
    {
        // .NET's Debug: everything goes through the provider, which System.Diagnostics.Trace replaces with
        // its own (Debug output then reaches Trace's listeners).
        private static System.Diagnostics.DebugProvider s_provider = new System.Diagnostics.DebugProvider();
        private static int s_indentLevel;
        private static int s_indentSize = 4;

        public static System.Diagnostics.DebugProvider GetProvider() => s_provider;

        public static System.Diagnostics.DebugProvider SetProvider(System.Diagnostics.DebugProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            return System.Threading.Interlocked.Exchange(ref s_provider, provider);
        }

        public static bool AutoFlush { get => true; set { } }

        public static int IndentLevel
        {
            get => s_indentLevel;
            set
            {
                s_indentLevel = value < 0 ? 0 : value;
                s_provider.OnIndentLevelChanged(s_indentLevel);
            }
        }

        public static int IndentSize
        {
            get => s_indentSize;
            set
            {
                s_indentSize = value < 0 ? 0 : value;
                s_provider.OnIndentSizeChanged(s_indentSize);
            }
        }

        public static void Assert(bool condition) => Assert(condition, string.Empty, string.Empty);
        public static void Assert(bool condition, string message) => Assert(condition, message, string.Empty);
        public static void Assert(bool condition, string message, string detailMessage) { if (!condition) Fail(message, detailMessage); }

        public static void Assert(bool condition, string message, string detailMessageFormat, params object[] args) =>
            Assert(condition, message, string.Format(detailMessageFormat, args));

        public static void Fail(string message) => Fail(message, string.Empty);
        public static void Fail(string message, string detailMessage) => s_provider.Fail(message, detailMessage);

        public static void Write(string message) => s_provider.Write(message);
        public static void Write(object value) => Write(value?.ToString());
        public static void Write(string message, string category) { if (category == null) Write(message); else Write(category + ": " + message); }
        public static void Write(object value, string category) => Write(value?.ToString(), category);
        public static void WriteLine(string message) => s_provider.WriteLine(message);
        public static void WriteLine(object value) => WriteLine(value?.ToString());
        public static void WriteLine(string message, string category) { if (category == null) WriteLine(message); else WriteLine(category + ": " + message); }
        public static void WriteLine(object value, string category) => WriteLine(value?.ToString(), category);
        public static void WriteLine(string format, params object[] args) => WriteLine(string.Format(null, format, args));
        public static void Print(string message) => WriteLine(message);
        public static void Print(string format, params object[] args) => WriteLine(string.Format(null, format, args));
        public static void WriteIf(bool condition, string message) { if (condition) Write(message); }
        public static void WriteIf(bool condition, object value) { if (condition) Write(value); }
        public static void WriteIf(bool condition, string message, string category) { if (condition) Write(message, category); }
        public static void WriteIf(bool condition, object value, string category) { if (condition) Write(value, category); }
        public static void WriteLineIf(bool condition, string message) { if (condition) WriteLine(message); }
        public static void WriteLineIf(bool condition, object value) { if (condition) WriteLine(value); }
        public static void WriteLineIf(bool condition, string message, string category) { if (condition) WriteLine(message, category); }
        public static void WriteLineIf(bool condition, object value, string category) { if (condition) WriteLine(value, category); }
        public static void Indent() => IndentLevel++;
        public static void Unindent() => IndentLevel--;
        public static void Flush() { }
        public static void Close() { }

        /// <summary>The default provider's failed assertion (DebugProvider.Fail / FailCore).</summary>
        internal static void FailDefault(string message, string detailMessage) => FailCore(message, detailMessage);

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
