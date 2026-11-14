// Console.Out/Error/In as TextWriter/TextReader objects backed by the console fast paths. TextReader/TextWriter, StreamReader/StreamWriter
// and StringReader/StringWriter come from the framework's IL (upstream-types/System.IO.txt).
#nullable disable
using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal static class AsyncText
    {
        public static Task<T> Run<T>(Func<T> work)
        {
            try { return Task.FromResult(work()); }
            catch (Exception e) { return Task.FromException<T>(e); }
        }

        public static Task Run(Action work)
        {
            try { work(); return Task.CompletedTask; }
            catch (Exception e) { return Task.FromException(e); }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Console.Out / Console.Error / Console.In
    // ---------------------------------------------------------------------------------------------

    internal sealed class ConsoleOutWriter : TextWriter
    {
        [DllImport("__Internal")] private static extern void csrt_console_flush();
        public override Encoding Encoding => new UTF8Encoding(false);
        public override void Write(char value) => Console.Write(value);
        public override void Write(string value) => Console.Write(value);
        public override void WriteLine(string value) => Console.WriteLine(value);
        public override void WriteLine() => Console.WriteLine();
        // Each call is one autoflushed write, as .NET's console writer makes it (TextWriter's defaults would
        // write, and flush, one character at a time).
        public override void Write(char[] buffer, int index, int count) => Console.Write(new string(buffer, index, count));
        public override void Write(ReadOnlySpan<char> buffer) => Console.Write(new string(buffer));
        public override void WriteLine(ReadOnlySpan<char> buffer) => Console.WriteLine(new string(buffer));
        public override void WriteLine(char[] buffer, int index, int count) => Console.WriteLine(new string(buffer, index, count));
        public override void Flush() => csrt_console_flush();
    }

    internal sealed class ConsoleErrorWriter : TextWriter
    {
        [DllImport("__Internal")] private static extern void csrt_console_error_write_utf8(string text);
        public override Encoding Encoding => new UTF8Encoding(false);
        public override void Write(char value) => csrt_console_error_write_utf8(new string(value, 1));
        public override void Write(string value) { if (value != null) csrt_console_error_write_utf8(value); }
        public override void WriteLine(string value) => csrt_console_error_write_utf8((value ?? "") + Environment.NewLine);
    }

    internal sealed class ConsoleInReader : TextReader
    {
        [DllImport("__Internal")] private static extern IntPtr csrt_console_read_to_end_utf8();
        private int _peeked = -2;

        public override int Read()
        {
            if (_peeked != -2) { int p = _peeked; _peeked = -2; return p; }
            return Console.Read();
        }

        public override int Peek()
        {
            if (_peeked == -2) _peeked = Console.Read();
            return _peeked;
        }

        public override string ReadLine() => _peeked == -2 ? Console.ReadLine() : base.ReadLine();

        public override unsafe string ReadToEnd()
        {
            IntPtr p = csrt_console_read_to_end_utf8();
            string s = Utf8.FromNative((byte*)p);
            Libc.free((void*)p);
            return s;
        }
    }

    [ShadowMembers("System.Console")]
    internal static class ConsoleExtras
    {
        private static TextWriter s_out, s_error;
        private static TextReader s_in;

        public static TextWriter Out => s_out ??= (TextWriter)(object)new ConsoleOutWriter();
        public static TextWriter Error => s_error ??= (TextWriter)(object)new ConsoleErrorWriter();
        public static TextReader In => s_in ??= (TextReader)(object)new ConsoleInReader();
        public static Stream OpenStandardOutput() => new FileStream("/dev/stdout", FileMode.Append, FileAccess.Write);
        public static Stream OpenStandardInput() => new FileStream("/dev/stdin", FileMode.Open, FileAccess.Read);

        // A null buffer writes nothing (new string((char[])null) is "").
        public static void Write(char[] buffer) => Console.Write(new string(buffer));
        public static void WriteLine(char[] buffer) => Console.WriteLine(new string(buffer));
    }
}
