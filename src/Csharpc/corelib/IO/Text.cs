// System.IO text: TextReader/TextWriter, StreamReader/StreamWriter, StringReader/StringWriter, and
// Console.Out/Error/In as real TextWriter/TextReader objects (backed by the console fast paths).
#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.IO.TextReader")]
    public abstract class TextReaderImpl : IDisposable
    {
        public virtual int Peek() => -1;
        public virtual int Read() => -1;

        public virtual int Read(char[] buffer, int index, int count)
        {
            int n = 0;
            for (; n < count; n++)
            {
                int c = Read();
                if (c < 0) break;
                buffer[index + n] = (char)c;
            }
            return n;
        }

        public virtual int ReadBlock(char[] buffer, int index, int count) => Read(buffer, index, count);

        public virtual string ReadLine()
        {
            var sb = new StringBuilder();
            while (true)
            {
                int c = Read();
                if (c < 0) return sb.Length > 0 ? sb.ToString() : null;
                if (c == '\n') return sb.ToString();
                if (c == '\r')
                {
                    if (Peek() == '\n') Read();
                    return sb.ToString();
                }
                sb.Append((char)c);
            }
        }

        public virtual string ReadToEnd()
        {
            var sb = new StringBuilder();
            int c;
            while ((c = Read()) >= 0) sb.Append((char)c);
            return sb.ToString();
        }

        public void Dispose() => Dispose(true);
        public virtual void Close() => Dispose(true);
        protected virtual void Dispose(bool disposing) { }
    }

    [Shadows("System.IO.TextWriter")]
    public abstract class TextWriterImpl : IDisposable
    {
        private string _newLine = Environment.NewLine;
        // .NET's TextWriter formats numbers and composite formats with this provider (null: the current culture).
        private readonly IFormatProvider _internalFormatProvider;

        protected TextWriterImpl() { }
        protected TextWriterImpl(IFormatProvider formatProvider) { _internalFormatProvider = formatProvider; }

        public abstract Encoding Encoding { get; }

        public virtual string NewLine
        {
            get => _newLine;
            set => _newLine = value ?? Environment.NewLine;
        }

        public virtual IFormatProvider FormatProvider => _internalFormatProvider ?? System.Globalization.CultureInfo.CurrentCulture;

        public virtual void Write(char value) { }

        public virtual void Write(string value)
        {
            if (value == null) return;
            for (int i = 0; i < value.Length; i++) Write(value[i]);
        }

        public virtual void Write(char[] buffer)
        {
            if (buffer != null) Write(buffer, 0, buffer.Length);
        }

        public virtual void Write(char[] buffer, int index, int count)
        {
            for (int i = 0; i < count; i++) Write(buffer[index + i]);
        }

        public virtual void Write(bool value) => Write(value ? "True" : "False");
        public virtual void Write(int value) => Write(value.ToString(_internalFormatProvider));
        public virtual void Write(uint value) => Write(value.ToString(_internalFormatProvider));
        public virtual void Write(long value) => Write(value.ToString(_internalFormatProvider));
        public virtual void Write(ulong value) => Write(value.ToString(_internalFormatProvider));
        public virtual void Write(float value) => Write(value.ToString(_internalFormatProvider));
        public virtual void Write(double value) => Write(value.ToString(_internalFormatProvider));
        public virtual void Write(decimal value) => Write(value.ToString(_internalFormatProvider));
        public virtual void Write(object value)
        {
            if (value == null) return;
            if (value is IFormattable f) Write(f.ToString(null, _internalFormatProvider));
            else Write(value.ToString());
        }
        public virtual void Write(string format, object arg0) => Write(string.Format(_internalFormatProvider, format, arg0));
        public virtual void Write(string format, object arg0, object arg1) => Write(string.Format(_internalFormatProvider, format, arg0, arg1));
        public virtual void Write(string format, object arg0, object arg1, object arg2) => Write(string.Format(_internalFormatProvider, format, arg0, arg1, arg2));
        public virtual void Write(string format, params object[] arg) => Write(string.Format(_internalFormatProvider, format, arg));

        public virtual void WriteLine() => Write(_newLine);
        public virtual void WriteLine(char value) { Write(value); WriteLine(); }
        public virtual void WriteLine(string value) { Write(value); WriteLine(); }
        public virtual void WriteLine(char[] buffer) { Write(buffer); WriteLine(); }
        public virtual void WriteLine(char[] buffer, int index, int count) { Write(buffer, index, count); WriteLine(); }
        public virtual void WriteLine(bool value) { Write(value); WriteLine(); }
        public virtual void WriteLine(int value) { Write(value); WriteLine(); }
        public virtual void WriteLine(uint value) { Write(value); WriteLine(); }
        public virtual void WriteLine(long value) { Write(value); WriteLine(); }
        public virtual void WriteLine(ulong value) { Write(value); WriteLine(); }
        public virtual void WriteLine(float value) { Write(value); WriteLine(); }
        public virtual void WriteLine(double value) { Write(value); WriteLine(); }
        public virtual void WriteLine(decimal value) { Write(value); WriteLine(); }
        public virtual void WriteLine(object value) { Write(value); WriteLine(); }
        public virtual void WriteLine(string format, object arg0) => WriteLine(string.Format(_internalFormatProvider, format, arg0));
        public virtual void WriteLine(string format, object arg0, object arg1) => WriteLine(string.Format(_internalFormatProvider, format, arg0, arg1));
        public virtual void WriteLine(string format, object arg0, object arg1, object arg2) => WriteLine(string.Format(_internalFormatProvider, format, arg0, arg1, arg2));
        public virtual void WriteLine(string format, params object[] arg) => WriteLine(string.Format(_internalFormatProvider, format, arg));

        public virtual void Flush() { }
        public void Dispose() => Dispose(true);
        public virtual void Close() => Dispose(true);
        protected virtual void Dispose(bool disposing) { }
    }

    // ---------------------------------------------------------------------------------------------
    // StreamReader / StreamWriter
    // ---------------------------------------------------------------------------------------------

    [Shadows("System.IO.StreamReader")]
    public unsafe class StreamReaderImpl : TextReaderImpl
    {
        private Stream _stream;
        private Encoding _encoding;
        private readonly bool _detectEncoding, _leaveOpen;
        private bool _checkPreamble = true;
        private readonly byte[] _bytes;
        private int _byteLen;            // undecoded bytes carried at the start of _bytes
        private char[] _chars;
        private int _charPos, _charLen;
        private bool _eof;

        public StreamReaderImpl(Stream stream) : this(stream, new UTF8Encoding(false), true, 4096, false) { }
        public StreamReaderImpl(Stream stream, bool detectEncodingFromByteOrderMarks) : this(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks, 4096, false) { }
        public StreamReaderImpl(Stream stream, Encoding encoding) : this(stream, encoding, true, 4096, false) { }
        public StreamReaderImpl(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks) : this(stream, encoding, detectEncodingFromByteOrderMarks, 4096, false) { }
        public StreamReaderImpl(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize) : this(stream, encoding, detectEncodingFromByteOrderMarks, bufferSize, false) { }
        public StreamReaderImpl(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize, bool leaveOpen)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (!stream.CanRead) throw new ArgumentException("Stream was not readable.");
            _stream = stream;
            _encoding = encoding ?? new UTF8Encoding(false);
            _detectEncoding = detectEncodingFromByteOrderMarks;
            _leaveOpen = leaveOpen;
            if (bufferSize < 128) bufferSize = bufferSize <= 0 ? 4096 : 128;
            _bytes = new byte[bufferSize];
            _chars = new char[bufferSize + 1];
        }

        public StreamReaderImpl(string path) : this(path, new UTF8Encoding(false), true) { }
        public StreamReaderImpl(string path, bool detectEncodingFromByteOrderMarks) : this(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks) { }
        public StreamReaderImpl(string path, Encoding encoding) : this(path, encoding, true) { }
        public StreamReaderImpl(string path, Encoding encoding, bool detectEncodingFromByteOrderMarks)
            : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096), encoding, detectEncodingFromByteOrderMarks, 4096, false) { }

        public virtual Stream BaseStream => _stream;
        public virtual Encoding CurrentEncoding => _encoding;

        public bool EndOfStream
        {
            get
            {
                if (_stream == null) throw new ObjectDisposedException(null, "Cannot read from a closed TextReader.");
                return _charPos >= _charLen && !Fill();
            }
        }

        public void DiscardBufferedData()
        {
            _byteLen = 0;
            _charPos = _charLen = 0;
            _eof = false;
        }

        /// <summary>Decodes the next chunk; false at end of stream.</summary>
        private bool Fill()
        {
            if (_stream == null) throw new ObjectDisposedException(null, "Cannot read from a closed TextReader.");
            _charPos = _charLen = 0;
            while (_charLen == 0)
            {
                if (_eof && _byteLen == 0) return false;
                int read = _eof ? 0 : _stream.Read(_bytes, _byteLen, _bytes.Length - _byteLen);
                if (read == 0) _eof = true;
                int total = _byteLen + read;
                fixed (byte* b = _bytes)
                {
                    int start = 0;
                    if (_checkPreamble && (total >= 4 || _eof)) { start = DetectPreamble(b, total); _checkPreamble = false; }
                    else if (_checkPreamble) { _byteLen = total; continue; }
                    int usable = total - start;
                    int tail = _eof ? 0 : ((EncodingImpl)(object)_encoding).IncompleteTail(b + start, usable);
                    int decodeCount = usable - tail;
                    if (decodeCount > 0)
                    {
                        int needed = _encoding.GetMaxCharCount(decodeCount);
                        if (_chars.Length < needed) _chars = new char[needed];
                        fixed (char* c = _chars) _charLen = ((EncodingImpl)(object)_encoding).DecodeBytes(b + start, decodeCount, c);
                    }
                    for (int i = 0; i < tail; i++) b[i] = b[start + decodeCount + i];
                    _byteLen = tail;
                    if (_eof && decodeCount == 0) _byteLen = 0;
                }
            }
            return true;
        }

        private int DetectPreamble(byte* b, int count)
        {
            if (!_detectEncoding)
            {
                // Still skip the encoding's own preamble (.NET does).
                byte[] pre = _encoding.GetPreamble();
                if (pre.Length > 0 && count >= pre.Length)
                {
                    for (int i = 0; i < pre.Length; i++) if (b[i] != pre[i]) return 0;
                    return pre.Length;
                }
                return 0;
            }
            if (count >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { _encoding = new UTF8Encoding(true); return 3; }
            if (count >= 2 && b[0] == 0xFF && b[1] == 0xFE) { _encoding = new UnicodeEncoding(false, true); return 2; }
            if (count >= 2 && b[0] == 0xFE && b[1] == 0xFF) { _encoding = new UnicodeEncoding(true, true); return 2; }
            return 0;
        }

        public override int Peek()
        {
            if (_charPos >= _charLen && !Fill()) return -1;
            return _chars[_charPos];
        }

        public override int Read()
        {
            if (_charPos >= _charLen && !Fill()) return -1;
            return _chars[_charPos++];
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            int total = 0;
            while (count > 0)
            {
                if (_charPos >= _charLen && !Fill()) break;
                int n = _charLen - _charPos;
                if (n > count) n = count;
                Array.Copy(_chars, _charPos, buffer, index, n);
                _charPos += n; index += n; count -= n; total += n;
            }
            return total;
        }

        public override string ReadLine()
        {
            if (_charPos >= _charLen && !Fill()) return null;
            StringBuilder sb = null;
            while (true)
            {
                int start = _charPos;
                for (int i = _charPos; i < _charLen; i++)
                {
                    char c = _chars[i];
                    if (c == '\n' || c == '\r')
                    {
                        string line;
                        if (sb == null) line = new string(_chars, start, i - start);
                        else { sb.Append(new string(_chars, start, i - start)); line = sb.ToString(); }
                        _charPos = i + 1;
                        if (c == '\r' && (_charPos < _charLen || Fill()) && _chars[_charPos] == '\n') _charPos++;
                        return line;
                    }
                }
                sb ??= new StringBuilder();
                sb.Append(new string(_chars, start, _charLen - start));
                if (!Fill()) return sb.ToString();
            }
        }

        public override string ReadToEnd()
        {
            var sb = new StringBuilder();
            do { sb.Append(new string(_chars, _charPos, _charLen - _charPos)); } while (Fill());
            _charPos = _charLen;
            return sb.ToString();
        }

        protected override void Dispose(bool disposing)
        {
            if (_stream != null && !_leaveOpen) _stream.Dispose();
            _stream = null;
        }
    }

    [Shadows("System.IO.StreamWriter")]
    public unsafe class StreamWriterImpl : TextWriterImpl
    {
        private Stream _stream;
        private readonly Encoding _encoding;
        private readonly bool _leaveOpen;
        private readonly char[] _chars;
        private int _charPos;
        private byte[] _bytes;
        private bool _preambleChecked;

        public StreamWriterImpl(Stream stream) : this(stream, new UTF8Encoding(false), 1024, false) { }
        public StreamWriterImpl(Stream stream, Encoding encoding) : this(stream, encoding, 1024, false) { }
        public StreamWriterImpl(Stream stream, Encoding encoding, int bufferSize) : this(stream, encoding, bufferSize, false) { }
        public StreamWriterImpl(Stream stream, Encoding encoding, int bufferSize, bool leaveOpen)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (!stream.CanWrite) throw new ArgumentException("Stream was not writable.");
            _stream = stream;
            _encoding = encoding ?? new UTF8Encoding(false);
            _leaveOpen = leaveOpen;
            _chars = new char[bufferSize <= 0 ? 1024 : bufferSize];
        }

        public StreamWriterImpl(string path) : this(path, false, new UTF8Encoding(false), 4096) { }
        public StreamWriterImpl(string path, bool append) : this(path, append, new UTF8Encoding(false), 4096) { }
        public StreamWriterImpl(string path, bool append, Encoding encoding) : this(path, append, encoding, 4096) { }
        public StreamWriterImpl(string path, bool append, Encoding encoding, int bufferSize)
            : this(new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 4096), encoding, bufferSize, false) { }

        public bool AutoFlush { get; set; }
        public virtual Stream BaseStream => _stream;
        public override Encoding Encoding => _encoding;

        public override void Write(char value)
        {
            if (_charPos == _chars.Length) FlushChars();
            _chars[_charPos++] = value;
            if (AutoFlush) Flush();
        }

        public override void Write(string value)
        {
            if (value == null) return;
            int index = 0, count = value.Length;
            while (count > 0)
            {
                if (_charPos == _chars.Length) FlushChars();
                int n = _chars.Length - _charPos;
                if (n > count) n = count;
                for (int i = 0; i < n; i++) _chars[_charPos + i] = value[index + i];
                _charPos += n; index += n; count -= n;
            }
            if (AutoFlush) Flush();
        }

        public override void Write(char[] buffer, int index, int count)
        {
            while (count > 0)
            {
                if (_charPos == _chars.Length) FlushChars();
                int n = _chars.Length - _charPos;
                if (n > count) n = count;
                Array.Copy(buffer, index, _chars, _charPos, n);
                _charPos += n; index += n; count -= n;
            }
            if (AutoFlush) Flush();
        }

        public override void WriteLine(string value)
        {
            Write(value);
            Write(NewLine);
        }

        private void FlushChars()
        {
            if (_stream == null) throw new ObjectDisposedException(null, "Cannot write to a closed TextWriter.");
            if (!_preambleChecked)
            {
                _preambleChecked = true;
                byte[] preamble = _encoding.GetPreamble();
                if (preamble.Length > 0 && (!_stream.CanSeek || _stream.Position == 0)) _stream.Write(preamble, 0, preamble.Length);
            }
            if (_charPos == 0) return;
            int max = _encoding.GetMaxByteCount(_charPos);
            if (_bytes == null || _bytes.Length < max) _bytes = new byte[max];
            int n;
            fixed (char* c = _chars) fixed (byte* b = _bytes) n = ((EncodingImpl)(object)_encoding).EncodeChars(c, _charPos, b);
            _stream.Write(_bytes, 0, n);
            _charPos = 0;
        }

        public override void Flush()
        {
            FlushChars();
            _stream.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (_stream == null) return;
            try { FlushChars(); _stream.Flush(); }
            finally
            {
                if (!_leaveOpen) _stream.Dispose();
                _stream = null;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // StringReader / StringWriter
    // ---------------------------------------------------------------------------------------------

    [Shadows("System.IO.StringReader")]
    public class StringReaderImpl : TextReaderImpl
    {
        private string _s;
        private int _pos;

        public StringReaderImpl(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            _s = s;
        }

        public override int Peek() => _pos < _s.Length ? _s[_pos] : -1;
        public override int Read() => _pos < _s.Length ? _s[_pos++] : -1;

        public override string ReadLine()
        {
            if (_pos >= _s.Length) return null;
            int start = _pos;
            while (_pos < _s.Length)
            {
                char c = _s[_pos];
                if (c == '\n' || c == '\r')
                {
                    string line = _s.Substring(start, _pos - start);
                    _pos++;
                    if (c == '\r' && _pos < _s.Length && _s[_pos] == '\n') _pos++;
                    return line;
                }
                _pos++;
            }
            return _s.Substring(start);
        }

        public override string ReadToEnd()
        {
            string rest = _pos == 0 ? _s : _s.Substring(_pos);
            _pos = _s.Length;
            return rest;
        }
    }

    [Shadows("System.IO.StringWriter")]
    public class StringWriterImpl : TextWriterImpl
    {
        private readonly StringBuilder _sb;

        public StringWriterImpl() { _sb = new StringBuilder(); }
        public StringWriterImpl(StringBuilder sb) { _sb = sb ?? throw new ArgumentNullException("sb"); }
        public StringWriterImpl(IFormatProvider formatProvider) : base(formatProvider) { _sb = new StringBuilder(); }
        public StringWriterImpl(StringBuilder sb, IFormatProvider formatProvider) : base(formatProvider) { _sb = sb ?? throw new ArgumentNullException("sb"); }

        public override Encoding Encoding => new UnicodeEncoding(false, false);
        public virtual StringBuilder GetStringBuilder() => _sb;
        public override void Write(char value) => _sb.Append(value);
        public override void Write(string value) { if (value != null) _sb.Append(value); }
        public override void Write(char[] buffer, int index, int count) => _sb.Append(new string(buffer, index, count));
        public override void WriteLine(string value) { if (value != null) _sb.Append(value); _sb.Append(NewLine); }
        public override string ToString() => _sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // Console.Out / Console.Error / Console.In
    // ---------------------------------------------------------------------------------------------

    internal sealed class ConsoleOutWriter : TextWriterImpl
    {
        [DllImport("__Internal")] private static extern void csrt_console_flush();
        public override Encoding Encoding => new UTF8Encoding(false);
        public override void Write(char value) => Console.Write(value);
        public override void Write(string value) => Console.Write(value);
        public override void WriteLine(string value) => Console.WriteLine(value);
        public override void WriteLine() => Console.WriteLine();
        public override void Flush() => csrt_console_flush();
    }

    internal sealed class ConsoleErrorWriter : TextWriterImpl
    {
        [DllImport("__Internal")] private static extern void csrt_console_error_write_utf8(string text);
        public override Encoding Encoding => new UTF8Encoding(false);
        public override void Write(char value) => csrt_console_error_write_utf8(new string(value, 1));
        public override void Write(string value) { if (value != null) csrt_console_error_write_utf8(value); }
        public override void WriteLine(string value) => csrt_console_error_write_utf8((value ?? "") + Environment.NewLine);
    }

    internal sealed class ConsoleInReader : TextReaderImpl
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
