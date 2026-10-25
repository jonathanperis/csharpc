// .NET's CompareTo of sbyte, byte, short, ushort and char (and of enums over them) returns the difference
// of the values, not its sign: direct, boxed, generic, Comparer<T>.Default, enum and tuple forms.
using System;
using System.Collections.Generic;
enum B : byte { Lo = 0, Hi = 200 }
enum S : short { Lo = -300, Hi = 300 }
class Program
{
    static int G<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b);
    static void Main()
    {
        byte x = 200, y = 0; sbyte sx = -100, sy = 100; short hx = -300, hy = 300; ushort ux = 60000, uy = 1; char cx = 'z', cy = 'a';
        Console.WriteLine($"direct {x.CompareTo(y)} {sx.CompareTo(sy)} {hx.CompareTo(hy)} {ux.CompareTo(uy)} {cx.CompareTo(cy)} {true.CompareTo(false)}");
        Console.WriteLine($"boxed {((IComparable)x).CompareTo(y)} {((IComparable)sx).CompareTo(sy)} {((IComparable)hx).CompareTo(hy)} {((IComparable)ux).CompareTo(uy)} {((IComparable)cx).CompareTo(cy)} {((IComparable)true).CompareTo(false)}");
        Console.WriteLine($"generic {G(x, y)} {G(sx, sy)} {G(hx, hy)} {G(ux, uy)} {G(cx, cy)}");
        Console.WriteLine($"comparer {Comparer<byte>.Default.Compare(x, y)} {Comparer<sbyte>.Default.Compare(sx, sy)} {Comparer<short>.Default.Compare(hx, hy)} {Comparer<ushort>.Default.Compare(ux, uy)} {Comparer<char>.Default.Compare(cx, cy)}");
        Console.WriteLine($"enum {B.Hi.CompareTo(B.Lo)} {S.Lo.CompareTo(S.Hi)} {((IComparable)B.Hi).CompareTo(B.Lo)} {Comparer<B>.Default.Compare(B.Hi, B.Lo)} {Comparer<S>.Default.Compare(S.Lo, S.Hi)}");
        Console.WriteLine($"tuple {(x, 1).CompareTo((y, 1))}");
        Console.WriteLine($"comparer-object {Comparer<object>.Default.Compare(x, y)} {Comparer<object>.Default.Compare(B.Hi, B.Lo)}");
    }
}
