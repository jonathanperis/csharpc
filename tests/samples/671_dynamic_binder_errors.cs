// RuntimeBinderException messages and run-time binding rules (null arguments bind like the null literal, boxed
// struct receivers are mutated in place, inaccessible types bind as their nearest accessible base).
using System;
using System.Collections.Generic;
using Microsoft.CSharp.RuntimeBinder;

struct S { public int X; public void Inc(int a) { X += a; } public void Bump() { X++; } }
class Outer { private class Hidden { public int Val = 7; public int Get() => 1; } public static object Make() => new Hidden(); public class Pub { public int Q = 3; } }
class Ex
{
    public void M(string s) => Console.WriteLine("M(string)");
    public void M(object o) => Console.WriteLine("M(object)");
    public void N(string s) => Console.WriteLine("N(string)");
    public void Two(int a, string b) { }
    public void Gen<T>(T x) => Console.WriteLine("Gen " + typeof(T));
    public int Prop { get; set; }
    public static void St(int x) => Console.WriteLine("St int");
    public void V() { } public Ex() { } public Ex(string s) { }
}
class Program
{
    static void T(string label, Action a)
    {
        try { a(); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().FullName}: {e.Message}"); }
    }
    static void Main()
    {
        S st = default; dynamic one = 1;
        st.Inc(one); Console.WriteLine("st.X after st.Inc(d) = " + st.X);
        dynamic ds = new S(); ds.Bump(); Console.WriteLine("ds.X after ds.Bump() = " + ds.X);
        dynamic ex = new Ex(); dynamic nul = null;
        ex.M(nul);
        T("null to N", () => ex.N(nul));
        T("null recv", () => nul.Foo());
        T("null get", () => { var x = nul.Foo; });
        T("null + 1", () => { var x = nul + 1; });
        T("1 + null", () => { var x = one + nul; });
        T("int.Foo", () => one.Foo());
        T("list.Foo", () => ((dynamic)new List<int>()).Foo());
        T("dict.Foo", () => { var q = ((dynamic)new Dictionary<string, List<int>>()).Foo; });
        T("arr.Foo", () => ((dynamic)new int[1]).Foo());
        T("arr2.Foo", () => ((dynamic)new string[1, 2]).Foo());
        T("hidden", () => ((dynamic)Outer.Make()).Get());
        T("hidden val", () => Console.WriteLine(((dynamic)Outer.Make()).Val));
        T("pub nested", () => ((dynamic)new Outer.Pub()).Zz());
        T("anon", () => ((dynamic)new { A = 1, B = "x" }).Zz());
        T("anon ok", () => Console.WriteLine(((dynamic)new { A = 1, B = "x" }).B));
        T("tuple", () => ((dynamic)(1, "a")).Zz());
        T("nullable", () => ((dynamic)(int?)5).Zz());
        T("enum", () => ((dynamic)DayOfWeek.Monday).Zz());
        T("ex.Two(1)", () => ex.Two(1));
        T("ex.Two(1,2)", () => ex.Two(1, 2));
        T("ex.N(1)", () => ex.N(1));
        T("ex.Gen", () => ex.Gen(one));
        T("ex.Gen<long>", () => ex.Gen<long>(one));
        T("ex.Prop()", () => ex.Prop());
        T("ex.V result", () => { var r = ex.V(); });
        T("ex.St", () => ex.St(1));
        T("int + bool", () => { var r = one + true; });
        T("-string", () => { var r = -(dynamic)"s"; });
        T("conv", () => { string s = one; });
        T("conv expl", () => { var s = (string)one; });
        T("conv long", () => { int i = (dynamic)5L; });
        T("conv long expl", () => { int i = (int)(dynamic)5L; Console.WriteLine(i); });
        T("index int", () => { var r = one[0]; });
        T("index list", () => { var r = ((dynamic)new List<int> { 4 })[0]; Console.WriteLine(r); });
        T("invoke int", () => one(1));
        T("ctor", () => new Ex(one));
        T("set readonly", () => { ((dynamic)"abc").Length = 3; });
        T("string.Length", () => Console.WriteLine(((dynamic)"abc").Length));
        T("prop set wrong type", () => { ex.Prop = "x"; });
        T("checked", () => { dynamic m = int.MaxValue; var r = checked(m + 1); });
        T("unchecked", () => { dynamic m = int.MaxValue; var r = m + 1; Console.WriteLine(r); });
        T("byte+byte", () => { dynamic b = (byte)1; var r = b + b; Console.WriteLine(r.GetType()); });
        T("short", () => { dynamic b = (short)1; Console.WriteLine((b + 1L).GetType()); });
        T("str concat", () => { Console.WriteLine(one + "x" + nul); });
        T("eq", () => { Console.WriteLine(one == 1); Console.WriteLine(one == nul); Console.WriteLine(nul == null); });
        T("div0", () => { var r = one / 0; });
        T("ambig", () => { new Program().Amb(nul); });
        T("ToString", () => Console.WriteLine(one.ToString()));
        T("GetType", () => Console.WriteLine(one.GetType()));
        T("Equals", () => Console.WriteLine(one.Equals(1)));
        T("CompareTo", () => Console.WriteLine(one.CompareTo(2)));
    }
    void Amb(string s) { } void Amb(Uri u) { }
}
