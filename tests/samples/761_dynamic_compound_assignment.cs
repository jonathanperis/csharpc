// Dynamic compound assignment (docs/adr/0019-dynamic.md): `x op= y` is the binder's plain `x op y` — a dynamic
// variable keeps the result's type (2 += 0.5 is 2.5, short += short is int) — and storing it back to a member,
// an indexer or a statically typed variable converts it explicitly, `x = (T)(x op y)`, in the checked context.
using System;
using System.Collections.Generic;

struct Money
{
    public int C;
    public static Money operator +(Money m, int x) => new Money { C = m.C + x };
    public override string ToString() => "M" + C;
}

class Holder { public int I; public byte B; public int P { get; set; } public Money M; public short S; }

class Program
{
    static void T(string label, Action a)
    {
        try { a(); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        dynamic half = 0.5, one = 1;
        T("dynamic local", () => { dynamic c = 2; c += 0.5; Console.WriteLine(c); });
        T("dynamic local, dynamic value", () => { dynamic c = 2; c += half; Console.WriteLine(c); });
        T("short += short", () => { dynamic s = (short)3; s += (short)1; Console.WriteLine(s.GetType().Name + " " + s); });
        T("string += int", () => { dynamic x = "a"; x += 1; Console.WriteLine(x); });
        T("int local", () => { int t = 2; t += half; Console.WriteLine(t); });
        T("int local, int", () => { int t = 2; t += one; Console.WriteLine(t); });
        T("byte local", () => { byte b = 1; b += one; Console.WriteLine(b); });
        T("long local", () => { long w = 1; w += half; Console.WriteLine(w); });
        T("field", () => { dynamic h = new Holder(); h.I += 0.5; Console.WriteLine(h.I); });
        T("property", () => { dynamic h = new Holder(); h.P += 2.5; Console.WriteLine(h.P); });
        T("byte field", () => { dynamic h = new Holder(); h.B += 1; h.B += one; Console.WriteLine(h.B); });
        T("field chain", () => { dynamic h = new Holder(); h.I -= one; h.I *= 3; h.I <<= 2; Console.WriteLine(h.I); });
        T("narrowing", () => { dynamic h = new Holder(); h.I += 3000000000L; Console.WriteLine(h.I); });
        T("operator type", () => { dynamic h = new Holder(); h.M += 1; Console.WriteLine(h.M); });
        T("list", () => { dynamic l = new List<int> { 1 }; l[0] += 0.5; Console.WriteLine(l[0]); });
        T("byte list", () => { dynamic l = new List<byte> { 1 }; l[0] += 300; Console.WriteLine(l[0]); });
        T("array", () => { dynamic arr = new int[2]; arr[0] += 1.5; Console.WriteLine(arr[0]); });
        T("string list", () => { dynamic l = new List<string> { "a" }; l[0] += 1; Console.WriteLine(l[0]); });
        T("increments keep the type", () => { dynamic s = (byte)3; s++; Console.WriteLine(s.GetType().Name + " " + s); });
        T("field increment", () => { dynamic h = new Holder(); h.B++; Console.WriteLine(h.B); });
        T("checked field", () => { checked { dynamic h = new Holder(); h.B += 300; Console.WriteLine(h.B); } });
        T("checked local", () => { checked { byte b = 0; b += (dynamic)300; Console.WriteLine(b); } });
        T("checked short", () => { checked { dynamic h = new Holder(); h.S -= 40000; Console.WriteLine(h.S); } });
        T("no conversion back", () => { dynamic h = new Holder(); h.I += "x"; Console.WriteLine(h.I); });
        T("no conversion to local", () => { int t = 0; t += (dynamic)"s"; });
        T("no operator", () => { dynamic x = new object(); x += 1; });
        T("no operator, field", () => { dynamic h = new Holder(); h.I += new object(); });
        T("no operator, local", () => { int t = 0; t += (dynamic)new object(); });
    }
}
