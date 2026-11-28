#:property AllowUnsafeBlocks=true
// Structs by value in delegates marshalled to native code: Marshal.GetFunctionPointerForDelegate's pointer takes and returns
// them with the platform's C ABI (two and four doubles, two floats, one pointer-sized integer, an int and a double mixed,
// a 24-byte struct of ints that goes through memory, three bytes), and so does a delegate made with
// GetDelegateForFunctionPointer over an [UnmanagedCallersOnly] method; BOOLs mixed in stay 4-byte ints.
using System;
using System.Globalization;
using System.Runtime.InteropServices;

unsafe
{
    var inv = CultureInfo.InvariantCulture;

    var mid = (delegate* unmanaged<Point, Point, Point>)Marshal.GetFunctionPointerForDelegate(new Mid((a, b) => new Point { X = (a.X + b.X) / 2, Y = (a.Y + b.Y) / 2 }));
    var m = mid(new Point { X = 1, Y = 2 }, new Point { X = 3, Y = 6 });
    Console.WriteLine($"mid {m.X.ToString(inv)} {m.Y.ToString(inv)}");

    var grow = (delegate* unmanaged<Rect, double, Rect>)Marshal.GetFunctionPointerForDelegate(new Grow((r, by) => new Rect { X = r.X - by, Y = r.Y - by, W = r.W + 2 * by, H = r.H + 2 * by }));
    var g = grow(new Rect { X = 10, Y = 20, W = 30, H = 40 }, 5);
    Console.WriteLine($"grow {g.X.ToString(inv)} {g.Y.ToString(inv)} {g.W.ToString(inv)} {g.H.ToString(inv)}");

    var next = (delegate* unmanaged<Handle, Handle>)Marshal.GetFunctionPointerForDelegate(new Next(h => new Handle { Value = h.Value + 1 }));
    Console.WriteLine($"next {next(new Handle { Value = 41 }).Value}");

    var swap = (delegate* unmanaged<Pair, Pair>)Marshal.GetFunctionPointerForDelegate(new Swap(p => new Pair { A = p.B, B = p.A }));
    var s = swap(new Pair { A = 1.5f, B = -2.5f });
    Console.WriteLine($"swap {s.A.ToString(inv)} {s.B.ToString(inv)}");

    var scale = (delegate* unmanaged<Mixed, int, int, Mixed>)Marshal.GetFunctionPointerForDelegate(new Scale((x, by, check) => new Mixed { Count = x.Count * by, Weight = check != 0 ? x.Weight * by : -1 }));
    var x = scale(new Mixed { Count = 7, Weight = 0.5 }, 3, 1);
    Console.WriteLine($"scale {x.Count} {x.Weight.ToString(inv)}");

    var sum = (delegate* unmanaged<Triple, int>)Marshal.GetFunctionPointerForDelegate(new Sum(t => t.A + t.B + t.C + t.D + t.E + t.F));
    Console.WriteLine($"sum {sum(new Triple { A = 1, B = 2, C = 3, D = 4, E = 5, F = 6 })}");

    var shift = (delegate* unmanaged<Rgb, Rgb>)Marshal.GetFunctionPointerForDelegate(new Shift(c => new Rgb { R = c.G, G = c.B, B = c.R }));
    var c = shift(new Rgb { R = 1, G = 2, B = 3 });
    Console.WriteLine($"shift {c.R} {c.G} {c.B}");

    var inside = (delegate* unmanaged<Point, Rect, int>)Marshal.GetFunctionPointerForDelegate(new Inside((p, r) => p.X >= r.X && p.X < r.X + r.W && p.Y >= r.Y && p.Y < r.Y + r.H));
    Console.WriteLine($"inside {inside(new Point { X = 15, Y = 25 }, g)} {inside(new Point { X = 0, Y = 0 }, g)}");

    // The other direction: a delegate over a native function pointer.
    var wrapped = Marshal.GetDelegateForFunctionPointer<Grow>((IntPtr)(delegate* unmanaged<Rect, double, Rect>)&Native.Shrink);
    var w = wrapped(new Rect { X = 0, Y = 0, W = 10, H = 8 }, 1);
    Console.WriteLine($"wrapped {w.X.ToString(inv)} {w.Y.ToString(inv)} {w.W.ToString(inv)} {w.H.ToString(inv)}");
    var midAgain = Marshal.GetDelegateForFunctionPointer<Mid>((IntPtr)(delegate* unmanaged<Point, Point, Point>)&Native.Sum2);
    var ma = midAgain(new Point { X = 1, Y = 2 }, new Point { X = 10, Y = 20 });
    Console.WriteLine($"midAgain {ma.X.ToString(inv)} {ma.Y.ToString(inv)}");
}

struct Point { public double X, Y; }
struct Rect { public double X, Y, W, H; }
struct Handle { public nint Value; }
struct Pair { public float A, B; }
struct Mixed { public int Count; public double Weight; }
struct Triple { public int A, B, C, D, E, F; }
struct Rgb { public byte R, G, B; }

delegate Point Mid(Point a, Point b);
delegate Rect Grow(Rect r, double by);
delegate Handle Next(Handle h);
delegate Pair Swap(Pair p);
delegate Mixed Scale(Mixed x, int by, int check);
delegate int Sum(Triple t);
delegate Rgb Shift(Rgb c);
delegate bool Inside(Point p, Rect r);

static class Native
{
    [UnmanagedCallersOnly] public static Rect Shrink(Rect r, double by) => new Rect { X = r.X + by, Y = r.Y + by, W = r.W - 2 * by, H = r.H - 2 * by };
    [UnmanagedCallersOnly] public static Point Sum2(Point a, Point b) => new Point { X = a.X + b.X, Y = a.Y + b.Y };
}
