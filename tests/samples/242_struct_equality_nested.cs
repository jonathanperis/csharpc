// ValueType.Equals on nested structs, padded layouts, decimal/DateTime/nullable/tuple fields and
// fields whose type overrides Equals: every field compares with its own Equals, padding never counts.
#:property AllowUnsafeBlocks=true
using System;
using System.Collections.Generic;

// Padding: byte + long leaves 7 bytes of padding; make it differ between two equal values.
var p1 = new Padded { B = 1, L = 2 };
var p2 = new Padded { B = 1, L = 2 };
unsafe { ((byte*)&p1)[3] = 0xAB; }
Console.WriteLine($"{p1.Equals(p2)} {p1.GetHashCode() == p2.GetHashCode()} {new HashSet<Padded> { p1, p2 }.Count}");
var s1 = new Mixed { Flag = true, C = 'x', I = 3, S = 4 };
var s2 = new Mixed { Flag = true, C = 'x', I = 3, S = 4 };
unsafe { ((byte*)&s1)[1] = 0xCD; }
Console.WriteLine($"{s1.Equals(s2)} {s1.Equals(new Mixed { Flag = true, C = 'y', I = 3, S = 4 })}");

// Nested structs compare recursively (string and double rules apply inside).
var o1 = new Outer { In = new Inner { Text = "in" + 1, D = -0.0 }, N = 5 };
var o2 = new Outer { In = new Inner { Text = "in1", D = 0.0 }, N = 5 };
Console.WriteLine($"{o1.Equals(o2)} {o1.GetHashCode() == o2.GetHashCode()} {o1.Equals(new Outer { In = new Inner { Text = "in2" }, N = 5 })}");

// A field whose type overrides Equals/GetHashCode is compared through that override.
var m1 = new HasMod { M = new Mod10 { V = 3 }, Tag = 1 };
var m2 = new HasMod { M = new Mod10 { V = 23 }, Tag = 1 };
Console.WriteLine($"{m1.Equals(m2)} {m1.GetHashCode() == m2.GetHashCode()} {m1.Equals(new HasMod { M = new Mod10 { V = 4 }, Tag = 1 })}");
var dictionary = new Dictionary<HasMod, int> { [m1] = 1 };
dictionary[m2] = 2;
Console.WriteLine(dictionary.Count + " " + dictionary[new HasMod { M = new Mod10 { V = 13 }, Tag = 1 }]);
var r1 = new HasRefMod { R = new Ci("Hello"), K = 1 };
var r2 = new HasRefMod { R = new Ci("HELLO"), K = 1 };
Console.WriteLine($"{r1.Equals(r2)} {r1.GetHashCode() == r2.GetHashCode()} {r1.Equals(new HasRefMod { R = null, K = 1 })} {new HasRefMod { K = 1 }.Equals(new HasRefMod { K = 1 })}");

// decimal compares by value (1.0m == 1.00m), DateTime by ticks only, nullable and tuple fields by value.
var d1 = new Money { Amount = 1.0m, When = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), Maybe = 3, Pair = ("t" + 1, 2.0) };
var d2 = new Money { Amount = 1.00m, When = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local), Maybe = 3, Pair = ("t1", 2.0) };
Console.WriteLine($"{d1.Equals(d2)} {d1.GetHashCode() == d2.GetHashCode()} {d1.Equals(d2 with { Maybe = null })} {(d1 with { Maybe = null }).Equals(d2 with { Maybe = null })} {d1.Equals(d1 with { Pair = ("t1", double.NaN) })}");

// KeyValuePair has no Equals override: ValueType.Equals on its fields.
var kv = new KeyValuePair<string, int>("k" + 1, 1);
Console.WriteLine($"{kv.Equals(new KeyValuePair<string, int>("k1", 1))} {kv.Equals(new KeyValuePair<string, int>("k1", 2))} {kv.GetHashCode() == new KeyValuePair<string, int>("k1", 1).GetHashCode()}");

// Empty structs, enums and bools inside structs.
Console.WriteLine($"{new Empty().Equals(new Empty())} {new Empty().GetHashCode() == default(Empty).GetHashCode()} {new Flags { C = ConsoleColor.Red, On = true }.Equals(new Flags { C = ConsoleColor.Red, On = true })} {new Flags { C = ConsoleColor.Red }.Equals(new Flags { C = ConsoleColor.Blue })}");

struct Padded { public byte B; public long L; }
struct Mixed { public bool Flag; public char C; public int I; public short S; }
struct Inner { public string Text; public double D; }
struct Outer { public Inner In; public int N; }
struct Mod10
{
    public int V;
    public override bool Equals(object obj) => obj is Mod10 m && m.V % 10 == V % 10;
    public override int GetHashCode() => V % 10;
}
struct HasMod { public Mod10 M; public int Tag; }
sealed class Ci(string s)
{
    public readonly string S = s;
    public override bool Equals(object obj) => obj is Ci c && string.Equals(c.S, S, StringComparison.OrdinalIgnoreCase);
    public override int GetHashCode() => S.ToUpperInvariant().Length;
}
struct HasRefMod { public Ci R; public int K; }
struct Money
{
    public decimal Amount;
    public DateTime When;
    public int? Maybe;
    public (string, double) Pair;
}
struct Empty { }
struct Flags { public ConsoleColor C; public bool On; }
