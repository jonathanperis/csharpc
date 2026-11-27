#:property AllowUnsafeBlocks=true
// System.Runtime.InteropServices.NFloat (Apple's CGFloat; a double on 64-bit targets), served from the framework's IL: arithmetic,
// conversions, classification, comparison, formatting and parsing, and NFloat (and structs of it, like CGPoint) across
// unmanaged function pointers, where it is blittable.
using System;
using System.Globalization;
using System.Runtime.InteropServices;

unsafe
{
    var inv = CultureInfo.InvariantCulture;
    NFloat x = 1.5f, y = (NFloat)2.25;
    Console.WriteLine($"{(x + y).ToString(inv)} {(x * y).ToString(inv)} {(y - x).ToString(inv)} {(y / x).ToString(inv)} {(-x).ToString(inv)} {(y % x).ToString(inv)}");
    Console.WriteLine($"{x < y} {x >= y} {x == (NFloat)1.5} {x.Equals(y)} {x.CompareTo(y)} {NFloat.Size} {sizeof(NFloat)}");
    Console.WriteLine($"{(double)NFloat.MaxValue == double.MaxValue} {NFloat.IsNaN(NFloat.NaN)} {NFloat.IsInfinity(NFloat.PositiveInfinity)} {NFloat.IsNegative(NFloat.NegativeZero)}");
    Console.WriteLine($"{(int)(NFloat)7.9} {(long)(NFloat)(-3.5)} {((float)y).ToString(inv)} {((decimal)(NFloat)0.25).ToString(inv)} {((NFloat)(Half)0.5).ToString(inv)}");
    Console.WriteLine($"{((NFloat)0.1).ToString(inv)} {((NFloat)1234.5678).ToString("F2", inv)} {((NFloat)1e300).ToString(inv)}");
    Console.WriteLine($"{NFloat.Parse("3.25", inv).ToString(inv)} {NFloat.TryParse("oops", inv, out var bad)} {bad.ToString(inv)}");
    Console.WriteLine($"{NFloat.Max(x, y).ToString(inv)} {NFloat.Abs((NFloat)(-2)).ToString(inv)} {NFloat.Sqrt((NFloat)16).ToString(inv)} {NFloat.Round((NFloat)2.5).ToString(inv)}");

    delegate* unmanaged<NFloat, NFloat, NFloat> hypot = &Native.Hypot;
    Console.WriteLine(hypot(3, 4).ToString(inv));
    delegate* unmanaged<CGPoint, NFloat, CGPoint> scale = &Native.Scale;
    var p = scale(new CGPoint { X = 1.5f, Y = -2 }, 2);
    Console.WriteLine($"{p.X.ToString(inv)} {p.Y.ToString(inv)} {sizeof(CGPoint)} {Marshal.SizeOf<CGPoint>()}");
}

struct CGPoint { public NFloat X, Y; }

static class Native
{
    [UnmanagedCallersOnly] public static NFloat Hypot(NFloat a, NFloat b) => NFloat.Sqrt(a * a + b * b);
    [UnmanagedCallersOnly] public static CGPoint Scale(CGPoint p, NFloat by) => new CGPoint { X = p.X * by, Y = p.Y * by };
}
