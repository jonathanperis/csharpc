using System.Runtime.InteropServices;

// By-value structs across the C ABI: integer pairs come back in general-purpose registers, and
// complex numbers are all-float/all-double structs (SSE registers on x86-64, homogeneous
// floating-point aggregates in SIMD registers on AArch64).
var d = Native.div(7, 2);
Console.WriteLine($"div {d.quot} {d.rem}");
var l = Native.ldiv(-17, 5);
Console.WriteLine($"ldiv {l.quot} {l.rem}");
Console.WriteLine($"cabs {Native.cabs(new Complex { Re = 3, Im = 4 })}");
Console.WriteLine($"cabsf {Native.cabsf(new ComplexF { Re = 5, Im = 12 })}");
var r = Native.csqrt(new Complex { Re = -4, Im = 0 });
Console.WriteLine($"csqrt {r.Re} {r.Im}");
var rf = Native.csqrtf(new ComplexF { Re = 3, Im = 4 });
Console.WriteLine($"csqrtf {rf.Re} {rf.Im}");
var c = Native.conj(new Complex { Re = 1.5, Im = -2.25 });
Console.WriteLine($"conj {c.Re} {c.Im}");
var cf = Native.conjf(new ComplexF { Re = -0.5f, Im = 8f });
Console.WriteLine($"conjf {cf.Re} {cf.Im}");

struct DivT { public int quot; public int rem; }
struct LDivT { public long quot; public long rem; }
struct Complex { public double Re; public double Im; }
struct ComplexF { public float Re; public float Im; }

static class Native
{
    [DllImport("libc")] public static extern DivT div(int numerator, int denominator);
    [DllImport("libc")] public static extern LDivT ldiv(long numerator, long denominator);
    [DllImport("libm.so.6")] public static extern double cabs(Complex z);
    [DllImport("libm.so.6")] public static extern float cabsf(ComplexF z);
    [DllImport("libm.so.6")] public static extern Complex csqrt(Complex z);
    [DllImport("libm.so.6")] public static extern ComplexF csqrtf(ComplexF z);
    [DllImport("libm.so.6")] public static extern Complex conj(Complex z);
    [DllImport("libm.so.6")] public static extern ComplexF conjf(ComplexF z);
}
