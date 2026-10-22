// Which user Equals/GetHashCode overrides the default struct equality calls, in which order, and
// when it stops: ValueType.Equals walks the fields in declaration order and stops at the first
// difference; ValueType.GetHashCode hashes only the first non-null reference field or the first
// non-reference field. Also base.Equals/base.GetHashCode from a struct override, Nullable fields,
// EqualityComparer null handling and record struct equality.
using System;
using System.Collections.Generic;

var x = new Two { A = new Loud(1), B = new Loud(2) };
var y = new Two { A = new Loud(1), B = new Loud(3) };
Console.WriteLine("Equals: " + x.Equals(y));
Console.WriteLine("Equals: " + new Two { A = new Loud(9), B = new Loud(2) }.Equals(x));
Console.WriteLine("Equals: " + new Two { A = null, B = new Loud(2) }.Equals(new Two { A = null, B = new Loud(2) }));
Console.WriteLine("Equals: " + new Two { A = null }.Equals(x));
Console.WriteLine("Equals: " + x.Equals(new Two { A = new Loud(1) }));

Console.WriteLine("-- hash");
_ = x.GetHashCode();                                         // A
_ = new Two { A = null, B = new Loud(2) }.GetHashCode();     // B (A is null)
_ = new Two().GetHashCode();                                 // nothing
_ = new IntFirst { A = new Loud(7) }.GetHashCode();          // nothing: the int comes first
_ = new DoubleFirst { A = new Loud(8) }.GetHashCode();       // nothing: the double comes first
_ = new Nest { I = new Two { B = new Loud(4) }, After = new Loud(5) }.GetHashCode();  // 4, never 5
_ = new Nest { After = new Loud(5) }.GetHashCode();          // nothing: the nested struct ends the search
_ = new ModFirst { M = new Mod { V = 1 }, A = new Loud(6) }.GetHashCode();  // Mod.GetHashCode only
_ = new NullableFirst { M = null, A = new Loud(6) }.GetHashCode();          // nothing
_ = new NullableFirst { M = new Mod { V = 2 }, A = new Loud(6) }.GetHashCode();
_ = new TupleFirst { T = (new Loud(10), new Loud(11)) }.GetHashCode();

Console.WriteLine("-- nullable");
Console.WriteLine(new NullableFirst { M = new Mod { V = 1 } }.Equals(new NullableFirst { M = null }));
Console.WriteLine(new NullableFirst { M = null }.Equals(new NullableFirst { M = new Mod { V = 1 } }));
Console.WriteLine(new NullableFirst { M = new Mod { V = 1 } }.Equals(new NullableFirst { M = new Mod { V = 11 } }));
Mod? some = new Mod { V = 5 }, none = null;
Console.WriteLine($"{some.Equals(new Mod { V = 15 })} {none.Equals(null)} {some.Equals(null)} {none.GetHashCode()}");
Console.WriteLine(EqualityComparer<Mod?>.Default.GetHashCode(none));

Console.WriteLine("-- tuple");
Console.WriteLine(new TupleFirst { T = (new Loud(1), new Loud(2)) }.Equals(new TupleFirst { T = (new Loud(3), new Loud(2)) }));

Console.WriteLine("-- base");
var b1 = new ViaBase { S = "b" + 1, L = new Loud(12) };
var b2 = new ViaBase { S = "b1", L = new Loud(12) };
Console.WriteLine(b1.Equals(b2) + " " + b1.Equals((object)"b1") + " " + (b1.GetHashCode() == b2.GetHashCode()));

Console.WriteLine("-- comparer");
Console.WriteLine(EqualityComparer<Loud>.Default.Equals(new Loud(13), null));
Console.WriteLine(EqualityComparer<Loud>.Default.Equals(null, new Loud(14)));
Console.WriteLine(EqualityComparer<Two>.Default.Equals(x, y));
Console.WriteLine(new List<Two> { y, x }.IndexOf(x));

Console.WriteLine("-- record struct");
Console.WriteLine(new Rec("p", new Loud(15)) == new Rec("q", new Loud(15)));
Console.WriteLine(new Rec("p", new Loud(15)) == new Rec("p", new Loud(15)));
Console.WriteLine(new Rec2(new Eqt { V = 1 }, 'c') == new Rec2(new Eqt { V = 1 }, 'c'));
Console.WriteLine(new Rec3(1, 'A', -2L, true).GetHashCode());

sealed class Loud(int id)
{
    public override bool Equals(object obj) { Console.WriteLine($"  Loud{id}.Equals({(obj is Loud ? "Loud" : obj ?? "null")})"); return obj is Loud l && l.GetId() == id; }
    public override int GetHashCode() { Console.WriteLine($"  Loud{id}.GetHashCode"); return id; }
    public int GetId() => id;
}
struct Two { public Loud A; public Loud B; }
struct IntFirst { public int N; public Loud A; }
struct DoubleFirst { public double D; public Loud A; }
struct Nest { public Two I; public Loud After; }
struct Mod
{
    public int V;
    public override bool Equals(object obj) { Console.WriteLine($"  Mod.Equals({(obj is null ? "null" : "Mod")})"); return obj is Mod m && m.V % 10 == V % 10; }
    public override int GetHashCode() { Console.WriteLine("  Mod.GetHashCode"); return V % 10; }
}
struct ModFirst { public Mod M; public Loud A; }
struct NullableFirst { public Mod? M; public Loud A; }
struct TupleFirst { public (Loud, Loud) T; }
struct ViaBase
{
    public string S;
    public Loud L;
    public override bool Equals(object obj) { Console.WriteLine("  ViaBase.Equals"); return base.Equals(obj); }
    public override int GetHashCode() => base.GetHashCode();
}
struct Eqt : IEquatable<Eqt>
{
    public int V;
    public bool Equals(Eqt other) { Console.WriteLine("  IEquatable<Eqt>.Equals"); return V == other.V; }
    public override bool Equals(object obj) { Console.WriteLine("  Eqt.Equals(object)"); return obj is Eqt e && e.V == V; }
    public override int GetHashCode() => V;
}
record struct Rec(string Name, Loud L);
record struct Rec2(Eqt E, char C);
record struct Rec3(int I, char C, long L, bool B);
