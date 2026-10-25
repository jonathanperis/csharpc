// Console.ForegroundColor/BackgroundColor/ResetColor (dotnet/runtime ConsolePal.Unix.cs semantics): the
// colors are tracked (UnknownColor until set), invalid values throw ArgumentException, and escape
// sequences go out only when .NET would emit them: output not redirected and no environment override.
// The sequences are the ANSI SGR codes terminfo's setaf/setab/sgr0 give for xterm-like terminals.
// FORCE_COLOR / DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION make .NET consult the terminfo database
// even for redirected output: csharpc doesn't, so with one of them set changing a color throws NotSupportedException.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Console")]
    internal static class ConsoleColors
    {
        private const ConsoleColor UnknownColor = (ConsoleColor)(-1);
        private static ConsoleColor s_foreground = UnknownColor, s_background = UnknownColor;
        private static readonly object s_lock = new object();

        public static ConsoleColor ForegroundColor
        {
            get => s_foreground;
            set => Refresh(ref s_foreground, value);
        }

        public static ConsoleColor BackgroundColor
        {
            get => s_background;
            set => Refresh(ref s_background, value);
        }

        public static void ResetColor()
        {
            lock (s_lock)
            {
                s_foreground = UnknownColor;
                s_background = UnknownColor;
                if (EmitCodes()) Console.Out.Write("\u001b[0m");
            }
        }

        private static void Refresh(ref ConsoleColor field, ConsoleColor value)
        {
            if (((int)value & ~0xF) != 0 && value != UnknownColor)
                throw new ArgumentException("The ConsoleColor enum value was not defined on that enum. Please use a defined color from the enum.");
            lock (s_lock)
            {
                field = value;
                if (!EmitCodes()) return;
                var sb = new System.Text.StringBuilder("\u001b[0m");
                if (s_foreground != UnknownColor) sb.Append(Sequence(true, s_foreground));
                if (s_background != UnknownColor) sb.Append(Sequence(false, s_background));
                Console.Out.Write(sb.ToString());
            }
        }

        private static readonly byte[] s_ansi = { 0, 4, 2, 6, 1, 5, 3, 7, 8, 12, 10, 14, 9, 13, 11, 15 };

        private static string Sequence(bool foreground, ConsoleColor color)
        {
            int code = s_ansi[(int)color];
            return code < 8 ? "\u001b[" + (foreground ? 30 : 40) + code + "m" : "\u001b[" + (foreground ? 90 : 100) + (code - 8) + "m";
        }

        private static bool EmitCodes()
        {
            // ConsoleUtils.GetColorOverrideFromEnvironment: FORCE_COLOR (or the legacy redirection switch) forces
            // codes from terminfo, which csharpc doesn't read; NO_COLOR turns them off.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FORCE_COLOR"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION")))
                throw new NotSupportedException("Console colors with FORCE_COLOR or DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION set are not supported by csharpc yet.");
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return false;
#if CSHARPC_TARGET_WINDOWS
            if (Console.IsOutputRedirected) return false;
            throw new NotSupportedException("Console colors on a Windows console are not supported by csharpc yet.");
#else
            return !Console.IsOutputRedirected;
#endif
        }
    }
}
