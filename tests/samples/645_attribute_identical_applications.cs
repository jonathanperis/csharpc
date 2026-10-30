// Identical attribute applications on different members: csharpc shares one factory per (constructor,
// arguments), but every GetCustomAttributes call must still return new instances, and applications
// that differ only in an argument's type, an array element or a named argument must stay distinct.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
sealed class TagAttribute : Attribute
{
    public object Value;
    public string Note { get; set; }
    public TagAttribute(object value) { Value = value; }
    public override string ToString() => $"Tag({Value ?? "null"}:{(Value is Type ? "Type" : Value?.GetType().Name)}{(Note != null ? "," + Note : "")})";
}

class Sample
{
    [Tag(1)] public int A { get; set; }
    [Tag(1)] public int B { get; set; }
    [Tag(1L)] public int C { get; set; }
    [Tag(1, Note = "n")] public int D { get; set; }
    [Tag(new[] { 1, 2 })] public int E { get; set; }
    [Tag(new[] { 1, 3 })] public int F { get; set; }
    [Tag(typeof(int))] public int G { get; set; }
    [Tag(typeof(long))] public int H { get; set; }
    [Tag(null)] public int I { get; set; }
    [Tag("x")] [Tag("x")] public int J { get; set; }
}

class Program
{
    static void Main()
    {
        Type t = Keep<Sample>();
        foreach (var name in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" })
        {
            var attrs = t.GetProperty(name).GetCustomAttributes<TagAttribute>();
            foreach (var a in attrs)
            {
                string value = a.Value is int[] arr ? "[" + string.Join(",", arr) + "]" : a.ToString();
                Console.Write(name + " " + value + "  ");
            }
            Console.WriteLine();
        }
        var a1 = t.GetProperty("A").GetCustomAttribute<TagAttribute>();
        var a2 = t.GetProperty("A").GetCustomAttribute<TagAttribute>();
        var b1 = t.GetProperty("B").GetCustomAttribute<TagAttribute>();
        Console.WriteLine($"{ReferenceEquals(a1, a2)} {ReferenceEquals(a1, b1)} {a1.Value.Equals(b1.Value)}");
        a1.Value = 99;
        Console.WriteLine(t.GetProperty("B").GetCustomAttribute<TagAttribute>().Value);
    }

    static Type Keep<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>() => typeof(T);
}
