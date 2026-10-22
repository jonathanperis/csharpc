// Enum statics with Type arguments (GetValues/GetNames/IsDefined/Parse/GetUnderlyingType), enum
// format strings (G/D/X/F, also through composite formatting of boxed enums), Enum hash codes,
// CompareTo/Equals, ValueType virtuals and the runtime type names of structs and enums.
using System.Collections.Generic;
using System.Linq;
using System;

class Part0
{
    enum Color { Red, Green = 5, Blue }
    [Flags] enum Perm : byte { None = 0, Read = 1, Write = 2, Exec = 4, All = Read | Write | Exec }
    enum Neg : long { Min = long.MinValue, Zero = 0, Max = long.MaxValue }
    struct Pt { public int X; public int Y; public Pt(int y) { X = 0; Y = y; } }
    struct Pt2 { public int X; public override string ToString() => $"Pt2({X})"; }
    record struct RS(int A);

    static void L1() { foreach (Color c in (Color[])Enum.GetValues(typeof(Color))) Console.Write(c + " "); Console.WriteLine(); }
    static void L2() { foreach (var c in Enum.GetValues<Color>()) Console.Write((int)c + " "); Console.WriteLine(); }
    static void L3() { Console.WriteLine(string.Join(",", Enum.GetNames(typeof(Perm)))); }
    static void L4() { Console.WriteLine(string.Join(",", Enum.GetNames<Neg>())); }
    static void L5() { Console.WriteLine(Enum.IsDefined(typeof(Color), 5) + " " + Enum.IsDefined(typeof(Color), "Blue") + " " + Enum.IsDefined(Color.Red) + " " + Enum.IsDefined((Color)3)); }
    static void L6() { var p = Perm.Read | Perm.Exec; Console.WriteLine($"{p} {p:G} {p:D} {p:X} {p:F} {Perm.All} {(Perm)8} {(Perm)9} {Perm.None}"); }
    static void L7() { var c = (Color)7; Console.WriteLine($"{c} {c:D} {c:X} {Color.Green:X} {Color.Blue.ToString("F")} {Color.Blue.ToString("d")}"); }
    static void L8() { Console.WriteLine(new Pt { X = 1 }.ToString() + " " + new Pt2 { X = 3 } + " " + new RS(4)); }
    static void L9() { object o = new Pt(); Console.WriteLine(o); Console.WriteLine(o.GetType().Name + " " + o.GetType().FullName); }
    static void L10() { Console.WriteLine(typeof(Color).Name + " " + typeof(Color).FullName + " " + typeof(int).Name + " " + typeof(string).FullName + " " + typeof(System.Collections.Generic.List<int>).Name); }
    static void L11() { Console.WriteLine(Enum.GetValues(typeof(Perm)).Length + " " + Enum.GetValues<Perm>().Max()); }
    static void L12() { Console.WriteLine(Enum.Parse<Color>("Blue") + " " + Enum.Parse(typeof(Perm), "Read, Write") + " " + Enum.TryParse<Color>("x", out var c) + " " + c); }
    static void L13() { Console.WriteLine(Enum.GetName(Color.Green) + " " + Enum.GetName(typeof(Color), 6) + " " + Enum.GetUnderlyingType(typeof(Perm))); }
    static void L14() { var a = (Color[])Enum.GetValues(typeof(Color)); Array.Reverse(a); Console.WriteLine(string.Join(" ", a)); }
    static void L15() { Console.WriteLine($"{Neg.Min:X} {Neg.Min:D} {(Perm)255} {(Perm)255:X}"); }
    static void L16() { Console.WriteLine(Color.Blue.CompareTo(Color.Red) + " " + Color.Blue.Equals(Color.Blue) + " " + Color.Blue.GetHashCode() + " " + Perm.All.HasFlag(Perm.Write)); }
    static void L17() { Console.WriteLine(new Pt().GetType() + " " + typeof(Pt).Name + " " + typeof(Part0).Name); }
    public static void Run() { L1(); L2(); L3(); L4(); L5(); L6(); L7(); L8(); L9(); L10(); L11(); L12(); L13(); L14(); L15(); L16(); L17(); }
}
enum E : short { A = -2, B = 10 }
[Flags] enum F { None = 0, R = 1, W = 2, RW = 3, X = 4 }
enum Dup { A = 1, B = 1, C = 2 }
class Part1
{
    public static void Run()
    {
        Console.WriteLine($"{E.A:X} {E.A:x} {E.B:x} {E.A:D} {E.B:F} {(E)3:F} {(F)7:F} {(F)7:G} {(F)8:F} {(F)0:F} {(F)3} {(F)7}");
        Console.WriteLine($"{Dup.B} {Dup.A:F} {(Dup)3:F} {(Dup)3}");
        Console.WriteLine(string.Format("{0:D} {0:X} {0} {1:F}", E.B, (F)5));
        try { Console.WriteLine(E.A.ToString("Q")); } catch (FormatException e) { Console.WriteLine(e.Message); }
        Console.WriteLine(E.A.ToString("") + E.A.ToString((string?)null));
        object o = F.R | F.X;
        Console.WriteLine(o);
    }
}
enum S : short { A = -2 } enum B : sbyte { A = -2 } enum U : byte { A = 200 } enum L : long { A = -5_000_000_000 } enum UL : ulong { A = ulong.MaxValue - 3 } enum I { A = -7 } enum C : ushort { A = 60000 } enum UI : uint { A = 4000000000 }
class Part2 { public static void Run() {
Console.WriteLine($"{S.A.GetHashCode()} {B.A.GetHashCode()} {U.A.GetHashCode()} {L.A.GetHashCode()} {UL.A.GetHashCode()} {I.A.GetHashCode()} {C.A.GetHashCode()} {UI.A.GetHashCode()}");
Console.WriteLine($"{((short)-2).GetHashCode()} {((sbyte)-2).GetHashCode()} {((long)-5_000_000_000).GetHashCode()} {(ulong.MaxValue - 3).GetHashCode()} {((ushort)60000).GetHashCode()} {(4000000000u).GetHashCode()}");
object o = S.A; Console.WriteLine(o.GetHashCode() + " " + o.Equals(S.A) + " " + o.Equals((short)-2) + " " + S.A.Equals(o));
}}
enum Suit { Clubs, Diamonds, Hearts, Spades }
enum Rank { Two, Three, Four }
class Part3
{
    static IEnumerable<Suit> Suits() => (Enum.GetValues(typeof(Suit)) as IEnumerable<Suit>)!;
    static IEnumerable<Rank> Ranks() => (Enum.GetValues(typeof(Rank)) as IEnumerable<Rank>)!;
    public static void Run()
    {
        var deck = from s in Suits() from r in Ranks() select new { s, r };
        foreach (var c in deck) Console.WriteLine(c);
        Console.WriteLine(Suits().Count());
        System.ValueType v = 5;
        Console.WriteLine(v.ToString() + " " + v.Equals(5) + " " + v.GetHashCode());
        System.ValueType w = Suit.Hearts;
        Console.WriteLine("x " + w.ToString());
    }
}
class Program { static void Main() { Part0.Run(); Part1.Run(); Part2.Run(); Part3.Run(); } }
