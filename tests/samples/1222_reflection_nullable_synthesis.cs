// [NullableContext] and [Nullable] on the members of source in a nullable-annotated file, as the JIT lists them
// (csc writes them into the metadata; Roslyn's symbols hide them). csharpc reproduces csc's algorithm: the bytes of each
// type (0 oblivious, 1 not annotated, 2 annotated, in preorder), the most common value per method and per type as the
// context (ties go to 0, then 1; the base type and the interfaces vote 0), and [Nullable] only where a member differs
// from its container's context. Every shape below is exact; what csharpc can't reproduce stays NotSupportedException
// (sample 1071).
#nullable enable
#pragma warning disable CS0649, CS0169, CS0414, CS0067, CS8618, CS8603, CS0108
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
class MyAttribute : Attribute { }

static class Program
{
    static string Text(IEnumerable<CustomAttributeData> data) => string.Join(",", data.Select(d => d.AttributeType.Name.Replace("Attribute", "") + "(" + string.Join(" ", d.ConstructorArguments.Select(a =>
        a.Value is IEnumerable<CustomAttributeTypedArgument> e ? "[" + string.Join(" ", e.Select(x => x.Value)) + "]" : a.Value)) + ")"));

    static void Dump([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        // A static class gets [Extension] from csc when it has extension methods; the methods are what this checks.
        Console.WriteLine($"{type.Name}: {(type.IsAbstract && type.IsSealed ? "" : Text(type.GetCustomAttributesData()))}");
        foreach (var f in type.GetFields(all).OrderBy(f => f.Name, StringComparer.Ordinal))
            Console.WriteLine($"  field {f.Name}: {Text(f.GetCustomAttributesData())}");
        foreach (var c in type.GetConstructors(all).OrderBy(c => c.GetParameters().Length))
        {
            Console.WriteLine($"  ctor {c.GetParameters().Length}: {Text(c.GetCustomAttributesData())}");
            foreach (var p in c.GetParameters()) Console.WriteLine($"    p {p.Name}: {Text(p.GetCustomAttributesData())}");
        }
        foreach (var m in type.GetMethods(all).OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.GetParameters().Length))
        {
            Console.WriteLine($"  method {m.Name}: {Text(m.GetCustomAttributesData())}");
            foreach (var p in m.GetParameters()) Console.WriteLine($"    p {p.Name}: {Text(p.GetCustomAttributesData())}");
        }
        foreach (var p in type.GetProperties(all).OrderBy(p => p.Name, StringComparer.Ordinal))
            Console.WriteLine($"  property {p.Name}: {Text(p.GetCustomAttributesData())}");
        foreach (var e in type.GetEvents(all).OrderBy(e => e.Name, StringComparer.Ordinal))
            Console.WriteLine($"  event {e.Name}: {Text(e.GetCustomAttributesData())}");
        if (type.IsGenericType)
            foreach (var g in type.GetGenericTypeDefinition().GetGenericArguments())
                Console.WriteLine($"  type parameter {g.Name}: {Text(g.GetCustomAttributesData())}");
    }

    static void Main()
    {
        Dump(typeof(T1));
        Dump(typeof(T2));
        Dump(typeof(T3));
        Dump(typeof(T4));
        Dump(typeof(T5));
        Dump(typeof(T6));
        Dump(typeof(T7));
        Dump(typeof(T9<string>));
        Dump(typeof(T10));
        Dump(typeof(T11));
        Dump(typeof(Gen<string>));
        Dump(typeof(T12));
        Dump(typeof(T13));
        Dump(typeof(I1));
        Dump(typeof(T14));
        Dump(typeof(T15));
        Dump(typeof(T16));
        Dump(typeof(S1));
        Dump(typeof(T17));
        Dump(typeof(T18<string>));
        Dump(typeof(T19<string>));
        Dump(typeof(T20<string>));
        Dump(typeof(T21<string>));
        Dump(typeof(T24<int>));
        Dump(typeof(IA));
        Dump(typeof(T25));
        Dump(typeof(T26));
        Dump(typeof(T27));
        Dump(typeof(T28));
        Dump(typeof(T29));
        Dump(typeof(T30));
        Dump(typeof(T33));
        Dump(typeof(T39));
        Dump(typeof(T40));
        Dump(typeof(T42));
        Dump(typeof(U1));
        Dump(typeof(U2));
        Dump(typeof(U5));
        Dump(typeof(Outer2));
        Dump(typeof(U6));
        Dump(typeof(U7));
        Dump(typeof(U8));
        Dump(typeof(U11));
        Dump(typeof(U14));
        Dump(typeof(U15));
        Dump(typeof(U16));
        Dump(typeof(U17));
        Dump(typeof(U18<string>));
        Dump(typeof(U19<int>));
        Dump(typeof(IB));
        Dump(typeof(IC<string>));
        Dump(typeof(V1));
        Dump(typeof(V2));
        Dump(typeof(V3));
        Dump(typeof(V4));
        Dump(typeof(V5));
        Dump(typeof(V6));
        Dump(typeof(V7));
        Dump(typeof(V8));
        Dump(typeof(W1));
        Dump(typeof(W2));
        Dump(typeof(W3));
        Dump(typeof(W4));
        Dump(typeof(W5));
        Dump(typeof(W8));
        Dump(typeof(W9));
        Dump(typeof(X1<int>));
        Dump(typeof(X4));
        Dump(typeof(X5));
        Dump(typeof(X6));
        Dump(typeof(X7<string>));
        Dump(typeof(X8<string>));
        Dump(typeof(X9));
        Dump(typeof(Y1));
        Dump(typeof(Y0));
        Dump(typeof(Y2));
        Dump(typeof(Y3));
        Dump(typeof(Y4));
        Dump(typeof(Y5));
        Dump(typeof(Y6));
        Dump(typeof(U5.Nest));
        Dump(typeof(Outer2.In1));
        Dump(typeof(Outer2.In1.In2));
        Dump(typeof(Outer2.In3));
    }
}

class T1 { public string A = ""; public string B = ""; public string? C; public string? D; }
class T2 { public string A = ""; public string? B; public string? C; public string D = ""; public string? E; }
class T3 { public string A = ""; public string B = ""; public string? C; }
class T4 { public string? A; public string B = ""; }
class T5 { public int N; public string S = ""; public int? M; }
class T6 { public List<string?> L = new(); public List<string>? L2; public string[] Arr = []; public string?[] Arr2 = []; public string[]? Arr3; public Dictionary<string, List<string?>> D = new(); }
class T7 { public string M(string a, string? b) => a; public string? N(string a) => a; public void V(string a, string b) { } public void W(string? a, string? b) { } public int I(int x) => x; }
class T9<T> { public T F = default!; public T? G; public void M(T t) { } }
class T10 { public string P { get; set; } = ""; public string? Q { get; set; } public string? R => null; }
class T11 { public T11(string a) { } public T11(string? a, string b) { } }
class Gen<T> { }
class T12 : Gen<string?> { }
class T13 : IEquatable<T13?> { public bool Equals(T13? other) => false; }
interface I1 { string M(string? x); string P { get; } }
class T14 { public event EventHandler? E; public event EventHandler F = delegate { }; }
class T15 { public (string, string?) T; public Action<string?> Act = _ => { }; public Func<string, string?> Fn = s => s; }
class T16 { public static string S = ""; public const string K = "x"; public readonly string? RO; public object O = ""; public object? OO; }
struct S1 { public string A; public string? B; }
class T17 { public Dictionary<string, string?>.KeyCollection K = null!; public KeyValuePair<string, string?> KV; public Nullable<int> NI; public List<int?> LI = new(); public string?[][] JA = []; }
class T18<T> { public T A = default!; public T B = default!; public List<T> L = new(); public List<T?> L2 = new(); }
class T19<T> where T : class { public T A = default!; public List<T> L = new(); }
class T20<T> where T : class? { public T A = default!; public T? B; }
class T21<T> where T : notnull { public T A = default!; }
class T24<T> where T : struct { public T A; public T? B; public List<T> L = new(); }
interface IA { string P { get; set; } string? Q { get; } }
abstract class T25 { public abstract string M(); public virtual string? V() => null; public abstract string? P { get; } }
class T26 : T25 { public override string M() => ""; public override string? P => null; }
class T27 { public T27? Next; public T27 Prev = null!; public Type? Ty; public Type Ty2 = null!; }
class T28 { public string?[]?[] Z = []; public (int, string) A; public (string?, string) B; public ValueTuple<string, string> C; }
class T29 { public Action<string, string?> A = null!; public Action<string, string> B = null!; }
class T30 { public IEnumerable<string>? E; public IList<string?> L = null!; public IDictionary<string, IList<string>> M = null!; public Tuple<string, string?> TT = null!; }
class T33 { public void M(params string[] a) { } public void N(string a = "x", string? b = null) { } }
class T39 { public event Action<string?>? E; public event Action<string> F = delegate { }; public event EventHandler? G { add { } remove { } } }
class T40 { public const string? K = null; public static readonly string R = ""; public volatile string? V; }
struct T42 { public string A; public string? B; public string M(string a) => a; }
class U1 { [My] public string A = ""; [My] public string? B; public string C = ""; }
class U2 { [My] public void M([My] string a, string b) { } public string R() => ""; }
class U5 { public string A = ""; public string? B; public class Nest { public string? X; public string? Y; public string? Z; } }
class Outer2 { public string? A; public string? B; public class In1 { public string C = ""; public string D = ""; public class In2 { public string? E; public string? F; public void M(string a) { } } } public struct In3 { public string? G; } }
class U6 : IA { string IA.P { get; set; } = ""; string? IA.Q => null; }
class U7 { public string A = ""; public string B = ""; }
partial class U8 { public string A = ""; }
partial class U8 { public string? B; public string? C; public string? D; }
class U11 { public static string operator +(U11 a, string b) => b; public static implicit operator string(U11 a) => ""; public static explicit operator U11(string a) => null!; }
abstract class U14 { protected abstract string? A { get; } public virtual string B { get; } = ""; internal string? C; private string D = ""; }
class U15 { public int[] A = []; public int[]? B; public IEnumerable<int> C = []; public IEnumerable<int?>? D; public List<(int, string)> E = []; }
class U16 { public string? M(string? a) => a; }
class U17 { public string A = null!; public string M() => A; public static string S(string a) => a; public static string? T(string? a) => a; }
class U18<T> { public T M(T a) => a; public T? N(T? a) => a; public T O(T a, T b) => a; }
class U19<T> where T : struct { public T M(T a) => a; public T? N(T? a) => a; public string S(string a, T t) => a; }
interface IB { void M(string a, string? b); string? N { get; set; } event EventHandler? E; }
interface IC<T> { T M(T a); }
static class V1 { public static string? E(this string? s) => s; public static string F(this string s) => s; }
class V2 { public void P(params string?[] a) { } public void Q(params string[]? a) { } public void R([My] params string?[] a) { } public void S(string a = "x", string? b = null) { } public void T(string a, string b, params string[] c) { } }
class V3 { public string? A { get; set; } [My] public string? B { get; set; } [field: My] public string? C { get; set; } }
class V4 { public string? A; [My] public string? B; public string C = ""; public string D = ""; }
class V5 { public void M([My] string? a, string b, string c) { } [My] public string? N(string? a) => a; }
class V6 { public event EventHandler? E; [My] public event EventHandler? F; public event EventHandler? G; }
class V7 { public void M(out string a, out string? b, ref string c, ref string? d) { a = ""; b = null; } }
class V8 { public string M1(string a) => a; public string M2(string a) => a; public string? M3(string? a) => a; public string? M4(string? a) => a; public string M5() => ""; public void M6(string x, string y, string? z) { } }
abstract class W1 { public abstract string? P { get; } }
abstract class W2 { public abstract string? P { get; } public abstract string? Q { get; } }
abstract class W3 { public abstract string? P { get; set; } }
interface W4 { string? P { get; } }
interface W5 { event EventHandler? E; }
class W8 { public W8(string? a) { } public W8(string? a, string? b) { } }
class W9 { public string? A { get; set; } public string B { get; set; } = ""; public string C { get; set; } = ""; }
class X1<T> where T : struct { public void M(T a, T b, string c) { } public void N(T a, string? d) { } }
class X4 { public string? A; public string? B; public string? C; }
class X5 : IDisposable { public string? A; public string? B; public void Dispose() { } }
class X6 : Gen<string?> { public string? A; public string? B; }
class X7<T> { public string? A; public string? B; }
class X8<T> where T : class { public string? A; public string? B; }
class X9 { public string? A; public string? B; public int N; public int M() => 0; }
class Y1 : IEnumerable<string>
{ public string? A; public string? B; public IEnumerator<string> GetEnumerator() => null!; System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!; }
class Y0 { }
class Y2 : Y0, IDisposable { public string? A; public string? B; public void Dispose() { } }
class Y3 : IComparable<Y3> { public string? A; public string? B; public int CompareTo(Y3? o) => 0; }
class Y4 : IDisposable, ICloneable { public string? A; public string? B; public string? C; public void Dispose() { } public object Clone() => this; }
class Y5 : Y2 { public string? A2; public string? B2; }
interface Y6 : IDisposable { string? P { get; } string? Q { get; } }
