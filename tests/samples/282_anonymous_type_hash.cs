// Anonymous types: GetHashCode is Roslyn's synthesized one (seed from the field names, then
// hash * -1521134295 + EqualityComparer<T>.Default.GetHashCode(field)), deterministic for these
// member types; Equals and ToString as synthesized by Roslyn too.
#nullable disable
using System;

enum Color { Red, Green = 5, Blue }
enum Big : long { X = 1L << 40 }

class Program
{
    static void Main()
    {
        Console.WriteLine(new { A = 1 }.GetHashCode());
        Console.WriteLine(new { B = 1 }.GetHashCode());
        Console.WriteLine(new { A = 1, B = 2 }.GetHashCode());
        Console.WriteLine(new { }.GetHashCode());
        Console.WriteLine(new { Flag = true, Off = false }.GetHashCode());
        Console.WriteLine(new { Ch = 'x' }.GetHashCode());
        Console.WriteLine(new { C = Color.Green, D = Big.X }.GetHashCode());
        Console.WriteLine(new { S = (string)null, O = (object)null }.GetHashCode());
        Console.WriteLine(new { L = 1234567890123L, D = 1.5, F = 2.5f }.GetHashCode());
        Console.WriteLine(new { B = (byte)200, Sh = (short)-3, U = 7u }.GetHashCode());
        Console.WriteLine(new { N = (int?)5, M = (int?)null }.GetHashCode());
        Console.WriteLine(new { Inner = new { X = 1 } }.GetHashCode());
        Console.WriteLine(new { Inner = new { X = 1 } }.Equals(new { Inner = new { X = 1 } }));
        Console.WriteLine(new { Neg = -1 }.GetHashCode());
        Console.WriteLine(new { LongerPropertyName = 42, Ünï = 3 }.GetHashCode());
        Console.WriteLine(new { A = 1 }.Equals(new { A = 1 }));
        Console.WriteLine(new { A = 1 }.Equals(new { A = 2 }));
        Console.WriteLine(new { A = 1 }.Equals(new { B = 1 }));
        Console.WriteLine(new { A = 1 }.Equals(null));
        Console.WriteLine(new { A = "x", B = (string)null }.Equals(new { A = "x", B = (string)null }));
        Console.WriteLine(new { A = 1, B = "x", C = (object)null, D = 1.5, E = 'c', F = Color.Blue, G = true }.ToString());
        Console.WriteLine(new { }.ToString());
        var a = new { A = 1, B = 2 };
        Console.WriteLine($"{a}");
    }
}
