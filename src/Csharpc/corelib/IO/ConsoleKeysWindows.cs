// System.Console key input and redirection queries on Windows, through the console API as in .NET
// (ConsolePal.Windows): a standard handle is redirected unless it is a character device with a console
// mode, keys come from ReadConsoleInputW, and ConsoleKey values are the virtual-key codes.
#if CSHARPC_TARGET_WINDOWS
#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class ConsoleKeys
    {
        [DllImport("kernel32")] private static extern nint GetStdHandle(int which);
        [DllImport("kernel32")] private static extern uint GetFileType(nint handle);
        [DllImport("kernel32")] private static extern int GetConsoleMode(nint handle, uint* mode);
        [DllImport("kernel32", SetLastError = true)] private static extern int GetConsoleScreenBufferInfo(nint handle, byte* info);
        [DllImport("kernel32")] private static extern int FillConsoleOutputCharacterW(nint handle, char c, uint length, uint coord, uint* written);
        [DllImport("kernel32")] private static extern int FillConsoleOutputAttribute(nint handle, ushort attribute, uint length, uint coord, uint* written);
        [DllImport("kernel32")] private static extern int SetConsoleCursorPosition(nint handle, uint coord);
        [DllImport("kernel32")] private static extern int ReadConsoleInputW(nint handle, byte* records, uint length, uint* read);
        [DllImport("kernel32")] private static extern int PeekConsoleInputW(nint handle, byte* records, uint length, uint* read);

        private const int STD_INPUT = -10, STD_OUTPUT = -11, STD_ERROR = -12;
        private const uint FILE_TYPE_CHAR = 2;

        private static bool IsRedirected(int which)
        {
            nint handle = GetStdHandle(which);
            if (handle == 0 || handle == -1) return true;
            if (GetFileType(handle) != FILE_TYPE_CHAR) return true;
            uint mode;
            return GetConsoleMode(handle, &mode) == 0;  // a character device that isn't a console (NUL)
        }

        public static bool IsInputRedirected => IsRedirected(STD_INPUT);
        public static bool IsOutputRedirected => IsRedirected(STD_OUTPUT);
        public static bool IsErrorRedirected => IsRedirected(STD_ERROR);

        public static void Clear()
        {
            csrt_console_flush();
            nint handle = GetStdHandle(STD_OUTPUT);
            // CONSOLE_SCREEN_BUFFER_INFO: dwSize @0 (X, Y shorts), wAttributes @8.
            byte* info = stackalloc byte[24];
            if (GetConsoleScreenBufferInfo(handle, info) == 0)
                throw new IOException(Posix.ErrorMessage(0x10000 + Marshal.GetLastPInvokeError()));
            uint cells = (uint)(*(short*)info * *(short*)(info + 2));
            uint written;
            FillConsoleOutputCharacterW(handle, ' ', cells, 0, &written);
            FillConsoleOutputAttribute(handle, *(ushort*)(info + 8), cells, 0, &written);
            SetConsoleCursorPosition(handle, 0);
        }

        public static bool KeyAvailable
        {
            get
            {
                if (IsInputRedirected)
                    throw new InvalidOperationException("Cannot see if a key has been pressed when either application does not have a console or when console input has been redirected from a file. Try Console.In.Peek.");
                nint handle = GetStdHandle(STD_INPUT);
                byte* record = stackalloc byte[20];
                while (true)
                {
                    uint n;
                    if (PeekConsoleInputW(handle, record, 1, &n) == 0 || n == 0) return false;
                    if (IsKeyDown(record)) return true;
                    ReadConsoleInputW(handle, record, 1, &n);  // drop mouse, focus and key-up events
                }
            }
        }

        public static ConsoleKeyInfo ReadKey() => ReadKey(false);

        public static ConsoleKeyInfo ReadKey(bool intercept)
        {
            if (IsInputRedirected)
                throw new InvalidOperationException("Cannot read keys when either application does not have a console or when console input has been redirected. Try Console.Read.");
            csrt_console_flush();
            nint handle = GetStdHandle(STD_INPUT);
            byte* record = stackalloc byte[20];
            while (true)
            {
                uint n;
                if (ReadConsoleInputW(handle, record, 1, &n) == 0)
                    throw new InvalidOperationException("Cannot read keys when either application does not have a console or when console input has been redirected. Try Console.Read.");
                if (n == 0 || !IsKeyDown(record)) continue;
                // KEY_EVENT_RECORD @4: wVirtualKeyCode @10, uChar @14, dwControlKeyState @16.
                ushort key = *(ushort*)(record + 10);
                char c = *(char*)(record + 14);
                uint state = *(uint*)(record + 16);
                // Bare modifier keys (Shift, Ctrl, Alt, Caps Lock) don't produce a key, as in .NET.
                if (c == '\0' && (key == 0x10 || key == 0x11 || key == 0x12 || key == 0x14)) continue;
                var info = new ConsoleKeyInfo(c, (ConsoleKey)key, (state & 0x10) != 0, (state & 0x3) != 0, (state & 0xC) != 0);
                if (!intercept && c != '\0') Console.Write(c);
                return info;
            }
        }

        private static bool IsKeyDown(byte* record) => *(ushort*)record == 1 /* KEY_EVENT */ && *(int*)(record + 4) != 0;
    }
}
#endif
