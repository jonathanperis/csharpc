using System.Runtime.InteropServices;

// P/Invoke through "libc", which resolves on Linux and macOS alike (unlike "libm.so.6"), so the
// C ABI is exercised on every platform: struct returns in a GPR and a GPR pair, int arguments,
// and DllNotFoundException for a library that does not exist.
var d = Native.div(-7, 2);
Console.WriteLine($"div {d.quot} {d.rem}");
var l = Native.ldiv(long.MinValue + 1, 3);
Console.WriteLine($"ldiv {l.quot} {l.rem}");
Console.WriteLine($"toupper {(char)Native.toupper('q')} abs {Native.abs(-12)}");
try { Native.missing(); } catch (DllNotFoundException) { Console.WriteLine("DllNotFoundException"); }

struct DivT { public int quot; public int rem; }
struct LDivT { public long quot; public long rem; }

static class Native
{
    [DllImport("libc")] public static extern DivT div(int numerator, int denominator);
    [DllImport("libc")] public static extern LDivT ldiv(long numerator, long denominator);
    [DllImport("libc")] public static extern int toupper(int c);
    [DllImport("libc")] public static extern int abs(int value);
    [DllImport("libdoesnotexist")] public static extern void missing();
}
