// EqualityComparer<T>.Default.Equals/GetHashCode and Comparer<T>.Default.Compare, called directly and through a variable, for
// the element kinds the default comparers treat differently: primitives, NaN, strings, IEquatable<T> and plain structs, enums,
// nullables, tuples, decimal, DateTime, references and null. The direct form lowers inline (issue #223), the variable keeps the
// comparer object: both must agree with .NET. Hash values are printed only where .NET's are deterministic.
using System;
using System.Collections.Generic;

struct Eq : IEquatable<Eq>, IComparable<Eq>
{
    public int V;
    public Eq(int v) => V = v;
    public bool Equals(Eq other) { Console.Write("[Eq.Equals]"); return V / 10 == other.V / 10; }
    public override bool Equals(object o) => o is Eq e && Equals(e);
    public override int GetHashCode() { Console.Write("[Eq.Hash]"); return V / 10; }
    public int CompareTo(Eq other) { Console.Write("[Eq.CompareTo]"); return (V / 10).CompareTo(other.V / 10); }
}

struct Plain { public int A, B; public Plain(int a, int b) { A = a; B = b; } }

class Ref
{
    public string Name;
    public Ref(string n) => Name = n;
    public override bool Equals(object o) { Console.Write("[Ref.Equals]"); return o is Ref r && r.Name == Name; }
    public override int GetHashCode() { Console.Write("[Ref.Hash]"); return Name.Length; }
}

enum Color { Red = 1, Green = 2, Blue = 3 }

class Program
{
    static void Check<T>(string label, T a, T b)
    {
        bool eq = EqualityComparer<T>.Default.Equals(a, b);
        bool sameHash = EqualityComparer<T>.Default.GetHashCode(a) == EqualityComparer<T>.Default.GetHashCode(b);
        var viaVariable = EqualityComparer<T>.Default;
        bool eq2 = viaVariable.Equals(a, b);
        Console.WriteLine($" {label}: equals={eq} sameHash={sameHash} viaVariable={eq2}");
    }

    static int Hash<T>(T x) => x.GetHashCode();

    static void Order<T>(string label, T a, T b)
    {
        int c = Comparer<T>.Default.Compare(a, b);
        var viaVariable = Comparer<T>.Default;
        int c2 = viaVariable.Compare(a, b);
        Console.WriteLine($" {label}: compare={Math.Sign(c)} viaVariable={Math.Sign(c2)}");
    }

    static void Main()
    {
        Check("int", 5, 5);
        Check("int differ", 5, 6);
        Check("long", 1L << 40, 1L << 40);
        Check("double", 1.5, 1.5);
        Check("NaN", double.NaN, double.NaN);
        Check("0.0 vs -0.0", 0.0, -0.0);
        Check("float NaN", float.NaN, float.NaN);
        Check("char", 'a', 'a');
        Check("bool", true, false);
        Check("decimal 1.0 vs 1.00", 1.0m, 1.00m);
        Check("string", "abc", new string(new[] { 'a', 'b', 'c' }));
        Check("string null", (string)null, (string)null);
        Check("string vs null", "x", (string)null);
        Check("enum", Color.Green, Color.Green);
        Check("enum differ", Color.Red, Color.Blue);
        Check("int?", (int?)3, (int?)3);
        Check("int? null", (int?)null, (int?)null);
        Check("int? vs null", (int?)3, (int?)null);
        Check("tuple", (1, "a"), (1, "a"));
        Check("tuple differ", (1, "a"), (2, "a"));
        Check("IEquatable struct", new Eq(11), new Eq(19));
        Check("IEquatable struct differ", new Eq(11), new Eq(21));
        Check("plain struct", new Plain(1, 2), new Plain(1, 2));
        Check("plain struct differ", new Plain(1, 2), new Plain(1, 3));
        Check("DateTime", new DateTime(2024, 1, 2), new DateTime(2024, 1, 2));
        Check("TimeSpan", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4));
        Check("ref", new Ref("ab"), new Ref("ab"));
        Check("ref null", (Ref)null, (Ref)null);
        Check("ref vs null", new Ref("ab"), (Ref)null);
        Check("object", (object)"s", (object)"s");
        Check("object int boxes", (object)7, (object)7);
        Check("IComparable", (IComparable)3, (IComparable)3);

        Order("int", 1, 2);
        Order("int rev", 9, 2);
        Order("double NaN", double.NaN, 1.0);
        Order("double NaN both", double.NaN, double.NaN);
        Order("char", 'b', 'a');
        Order("string", "apple", "banana");
        Order("string null", (string)null, "a");
        Order("string both null", (string)null, (string)null);
        Order("enum", Color.Red, Color.Blue);
        Order("int?", (int?)null, (int?)1);
        Order("decimal", 1.5m, 1.25m);
        Order("DateTime", new DateTime(2020, 1, 1), new DateTime(2021, 1, 1));
        Order("IComparable struct", new Eq(11), new Eq(15));
        Order("tuple", (1, 2), (1, 3));
        Order("object string", (object)"a", (object)"b");

        // x.GetHashCode() on an unconstrained T: constrained. T callvirt object::GetHashCode, the form the framework's HashSet<T> hashes with.
        Console.WriteLine();
        Console.WriteLine($"hash int {Hash(42)} char {Hash('a')} long {Hash(1L << 40)} bool {Hash(true)} byte {Hash((byte)200)} sbyte {Hash((sbyte)-3)}");
        Console.WriteLine($"hash double {Hash(1.5)} zero {Hash(0.0) == Hash(-0.0)} nan {Hash(double.NaN) == Hash(double.NaN)} float {Hash(2.5f)}");
        Console.WriteLine($"hash enum {Hash(Color.Blue)} decimal {Hash(1.5m) == Hash(1.50m)} int? {Hash((int?)7)} null {Hash((int?)null)} Eq {Hash(new Eq(31))}");
        Console.WriteLine($"hash DateTime {Hash(new DateTime(2024, 1, 2)) == Hash(new DateTime(2024, 1, 2))} TimeSpan {Hash(TimeSpan.FromTicks(5_000_000_000))}");

        var set = new HashSet<Eq>(new[] { new Eq(11), new Eq(19), new Eq(25) });
        Console.WriteLine();
        Console.WriteLine("set " + set.Count);
        var dict = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        Console.WriteLine(dict["a"] + dict["b"]);
    }
}
