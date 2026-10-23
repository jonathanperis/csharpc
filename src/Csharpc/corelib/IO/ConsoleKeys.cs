// System.Console key input (ReadKey, KeyAvailable, ConsoleKeyInfo), redirection queries and encodings.
// .NET's Unix console reads keys from a terminal in raw mode; with stdin redirected (a file, a pipe,
// /dev/null) ReadKey and KeyAvailable throw InvalidOperationException, which is what matters for
// programs run non-interactively ("Press any key to exit." + ReadKey()).
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.ConsoleKeyInfo")]
    public readonly struct ConsoleKeyInfoImpl : IEquatable<ConsoleKeyInfo>
    {
        private readonly char _keyChar;
        private readonly ConsoleKey _key;
        private readonly ConsoleModifiers _mods;

        public ConsoleKeyInfoImpl(char keyChar, ConsoleKey key, bool shift, bool alt, bool control)
        {
            // Same validation as .NET: ConsoleKey is a byte-sized enum.
            if ((int)key < 0 || (int)key > 255)
                throw new ArgumentOutOfRangeException(nameof(key), "Console key values must be between 0 and 255 inclusive.");
            _keyChar = keyChar;
            _key = key;
            _mods = (shift ? ConsoleModifiers.Shift : 0) | (alt ? ConsoleModifiers.Alt : 0) | (control ? ConsoleModifiers.Control : 0);
        }

        public char KeyChar => _keyChar;
        public ConsoleKey Key => _key;
        public ConsoleModifiers Modifiers => _mods;

        public override bool Equals(object value) => value is ConsoleKeyInfo info && Equals(info);
        public bool Equals(ConsoleKeyInfo obj) => obj.KeyChar == _keyChar && obj.Key == _key && obj.Modifiers == _mods;
        public static bool operator ==(ConsoleKeyInfoImpl a, ConsoleKeyInfoImpl b) => a._keyChar == b._keyChar && a._key == b._key && a._mods == b._mods;
        public static bool operator !=(ConsoleKeyInfoImpl a, ConsoleKeyInfoImpl b) => !(a == b);
        public override int GetHashCode() => _keyChar | ((int)_key << 16) | ((int)_mods << 24);
    }

    [ShadowMembers("System.Console")]
    internal static unsafe class ConsoleKeys
    {
        [DllImport("libc")] private static extern int isatty(int fd);
        [DllImport("libc")] private static extern int tcgetattr(int fd, byte* termios);
        [DllImport("libc")] private static extern int tcsetattr(int fd, int action, byte* termios);
        [DllImport("libc")] private static extern int poll(void* fds, nuint count, int timeout);
        [DllImport("__Internal")] private static extern void csrt_console_flush();

        private static Encoding s_outputEncoding, s_inputEncoding;

        public static bool IsInputRedirected => isatty(0) == 0;
        public static bool IsOutputRedirected => isatty(1) == 0;
        public static bool IsErrorRedirected => isatty(2) == 0;

        // .NET on Unix defaults to UTF-8 without a preamble (the charset of LANG; csharpc always writes UTF-8).
        public static Encoding OutputEncoding
        {
            get => s_outputEncoding ??= new UTF8Encoding(false);
            set => s_outputEncoding = CheckEncoding(value);
        }

        public static Encoding InputEncoding
        {
            get => s_inputEncoding ??= new UTF8Encoding(false);
            set => s_inputEncoding = CheckEncoding(value);
        }

        private static Encoding CheckEncoding(Encoding value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            // The console writer always emits UTF-8; any other encoding would change the bytes written.
            if (!(value is UTF8Encoding))
                throw new NotSupportedException("Console encodings other than UTF-8 are not supported by csharpc yet.");
            return value;
        }

        public static void Clear()
        {
            if (IsOutputRedirected) return;   // .NET only emits the terminal's clear sequence to a terminal
            Console.Write("\u001b[H\u001b[2J");
            csrt_console_flush();
        }

        public static bool KeyAvailable
        {
            get
            {
                if (IsInputRedirected)
                    throw new InvalidOperationException("Cannot see if a key has been pressed when either application does not have a console or when console input has been redirected from a file. Try Console.In.Peek.");
                return WaitForInput(0);
            }
        }

        public static ConsoleKeyInfo ReadKey() => ReadKey(false);

        public static ConsoleKeyInfo ReadKey(bool intercept)
        {
            if (IsInputRedirected)
                throw new InvalidOperationException("Cannot read keys when either application does not have a console or when console input has been redirected. Try Console.Read.");
            csrt_console_flush();
            // Raw mode for the duration of one key: no line buffering, no echo (we echo ourselves).
            byte* saved = stackalloc byte[TermiosSize];
            byte* raw = stackalloc byte[TermiosSize];
            bool restore = tcgetattr(0, saved) == 0;
            if (restore)
            {
                for (int i = 0; i < TermiosSize; i++) raw[i] = saved[i];
                *(uint*)(raw + LflagOffset) &= ~(ICANON | ECHO);
                raw[CcOffset + VMIN] = 1;
                raw[CcOffset + VTIME] = 0;
                tcsetattr(0, TCSANOW, raw);
            }
            ConsoleKeyInfo key;
            try { key = ReadOneKey(); }
            finally { if (restore) tcsetattr(0, TCSANOW, saved); }
            if (!intercept && key.KeyChar != '\0') Console.Write(key.KeyChar);
            return key;
        }

        // struct termios (Linux, glibc; same on x86-64 and AArch64): c_lflag @12, c_cc[32] @17, 60 bytes.
        private const int TermiosSize = 64, LflagOffset = 12, CcOffset = 17, VTIME = 5, VMIN = 6, TCSANOW = 0;
        private const uint ICANON = 2, ECHO = 8;

        private static bool WaitForInput(int timeoutMs)
        {
            // struct pollfd { int fd; short events; short revents; }, POLLIN = 1
            long pfd = 0;
            int* p = (int*)&pfd;
            p[0] = 0;
            ((short*)p)[2] = 1;
            return poll(&pfd, 1, timeoutMs) > 0;
        }

        private static int ReadByte()
        {
            byte b;
            return Posix.read(0, &b, 1) == 1 ? b : -1;
        }

        private static ConsoleKeyInfo ReadOneKey()
        {
            int c = ReadByte();
            if (c < 0) return new ConsoleKeyInfo('\0', 0, false, false, false);
            if (c == 27)
            {
                // An escape sequence follows immediately; a lone ESC is the Escape key.
                if (!WaitForInput(50)) return new ConsoleKeyInfo((char)27, ConsoleKey.Escape, false, false, false);
                int c1 = ReadByte();
                if (c1 == '[' || c1 == 'O') return ReadEscapeSequence();
                // ESC + key = Alt + key
                ConsoleKeyInfo k = MapByte(c1);
                return new ConsoleKeyInfo(k.KeyChar, k.Key, (k.Modifiers & ConsoleModifiers.Shift) != 0, true, (k.Modifiers & ConsoleModifiers.Control) != 0);
            }
            if (c >= 0x80) return new ConsoleKeyInfo(ReadUtf8(c), 0, false, false, false);
            return MapByte(c);
        }

        private static char ReadUtf8(int lead)
        {
            int extra = lead >= 0xF0 ? 3 : lead >= 0xE0 ? 2 : 1;
            int cp = lead & (0x3F >> extra);
            for (int i = 0; i < extra; i++) cp = (cp << 6) | (ReadByte() & 0x3F);
            return cp > 0xFFFF ? '�' : (char)cp;
        }

        private static ConsoleKeyInfo ReadEscapeSequence()
        {
            int c = ReadByte();
            int param = 0;
            while (c >= '0' && c <= '9') { param = param * 10 + (c - '0'); c = ReadByte(); }
            ConsoleKey key = c switch
            {
                'A' => ConsoleKey.UpArrow, 'B' => ConsoleKey.DownArrow, 'C' => ConsoleKey.RightArrow, 'D' => ConsoleKey.LeftArrow,
                'H' => ConsoleKey.Home, 'F' => ConsoleKey.End,
                'P' => ConsoleKey.F1, 'Q' => ConsoleKey.F2, 'R' => ConsoleKey.F3, 'S' => ConsoleKey.F4,
                '~' => param switch
                {
                    1 or 7 => ConsoleKey.Home, 2 => ConsoleKey.Insert, 3 => ConsoleKey.Delete, 4 or 8 => ConsoleKey.End,
                    5 => ConsoleKey.PageUp, 6 => ConsoleKey.PageDown,
                    15 => ConsoleKey.F5, 17 => ConsoleKey.F6, 18 => ConsoleKey.F7, 19 => ConsoleKey.F8,
                    20 => ConsoleKey.F9, 21 => ConsoleKey.F10, 23 => ConsoleKey.F11, 24 => ConsoleKey.F12,
                    _ => 0,
                },
                _ => 0,
            };
            return new ConsoleKeyInfo('\0', key, false, false, false);
        }

        private static ConsoleKeyInfo MapByte(int c)
        {
            char ch = (char)c;
            if (c >= 'a' && c <= 'z') return new ConsoleKeyInfo(ch, (ConsoleKey)(c - 'a' + (int)ConsoleKey.A), false, false, false);
            if (c >= 'A' && c <= 'Z') return new ConsoleKeyInfo(ch, (ConsoleKey)(c - 'A' + (int)ConsoleKey.A), true, false, false);
            if (c >= '0' && c <= '9') return new ConsoleKeyInfo(ch, (ConsoleKey)(c - '0' + (int)ConsoleKey.D0), false, false, false);
            switch (c)
            {
                case '\r': case '\n': return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
                case '\t': return new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false);
                case ' ': return new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false);
                case 8: case 127: return new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false);
                case 27: return new ConsoleKeyInfo(ch, ConsoleKey.Escape, false, false, false);
                case '+': return new ConsoleKeyInfo(ch, ConsoleKey.OemPlus, false, false, false);
                case '-': return new ConsoleKeyInfo(ch, ConsoleKey.OemMinus, false, false, false);
                case ',': return new ConsoleKeyInfo(ch, ConsoleKey.OemComma, false, false, false);
                case '.': return new ConsoleKeyInfo(ch, ConsoleKey.OemPeriod, false, false, false);
            }
            // Ctrl+letter arrives as 1..26.
            if (c >= 1 && c <= 26) return new ConsoleKeyInfo(ch, (ConsoleKey)(c - 1 + (int)ConsoleKey.A), false, false, true);
            return new ConsoleKeyInfo(ch, 0, false, false, false);
        }
    }
}
