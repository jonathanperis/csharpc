// Member reflection through `typeof(X)` held in a local variable, which NativeAOT's trimming analysis tracks
// (`var t = typeof(C); t.GetFields()` keeps C's fields): a local initialised or assigned typeof(X), queried
// directly, passed to a [DynamicallyAccessedMembers] parameter, or typeof(T) of a generic method. Each type below
// is reached only through such a local. Includes the backing fields of field-like events (listed by Type.GetFields)
// in a file with nullable annotations enabled.
#nullable enable
#pragma warning disable CS0067, CS0169, CS0649, CS0414
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

Type alpha = typeof(Alpha);
Console.WriteLine("alpha: " + string.Join(", ", alpha.GetFields(All).Select(f => f.Name + ":" + f.FieldType.Name + ":" + f.Attributes)));

var beta = typeof(Beta);
Console.WriteLine("beta: " + string.Join(", ", beta.GetProperties().Select(p => p.Name)));

// Two assignments: either type may be the one queried.
var gamma = typeof(Gamma);
if (Environment.TickCount64 < 0) gamma = typeof(Delta);
Console.WriteLine("gamma: " + gamma.GetMethod("Hello")!.Invoke(null, null));
Console.WriteLine("gamma methods: " + string.Join(", ", gamma.GetMethods(All).Select(m => m.Name).OrderBy(n => n)));

// A local read more than once (the C# compiler keeps it in a local slot instead of the evaluation stack).
var zeta = typeof(Zeta);
Console.WriteLine("zeta: " + zeta.GetFields(All).Length);
Console.WriteLine("zeta again: " + zeta.GetFields(All).Select(f => f.Name).Count());
Console.WriteLine("zeta methods: " + zeta.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length);

// A [DynamicallyAccessedMembers] parameter receiving the local.
var epsilon = typeof(Epsilon);
ShowFields(epsilon);

static void ShowFields([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] Type type) =>
    Console.WriteLine(type.Name + ": " + string.Join(", ", type.GetFields(All).Select(f => f.Name)));

// typeof(T) of a generic method, through a local.
Inspect<Eta>();
static void Inspect<T>()
{
    var t = typeof(T);
    Console.WriteLine("eta: " + string.Join(", ", t.GetFields(All).Select(f => f.Name)));
}

// A constant name through a local: GetField finds the event's backing field.
var theta = typeof(Theta);
var field = theta.GetField("Raised", All)!;
var instance = new Theta();
field.SetValue(instance, new Action(() => Console.WriteLine("raised through the backing field")));
instance.Fire();

class Alpha
{
    public int Id;
    public event EventHandler? Changed;
    public static string Name = "alpha";
    private long _hidden;
    public static event Action<int>? Counted;
}

class Beta
{
    public int One { get; set; }
    public string? Two { get; init; }
}

class Gamma
{
    public static string Hello() => "hello from gamma";
    public static int Other() => 1;
}

class Delta
{
    public static string Hello() => "hello from delta";
}

class Zeta
{
    public int A;
    private string? _b;
    public event Action? E;
}

class Epsilon
{
    public double X;
    public event EventHandler? Moved;
}

class Eta
{
    public bool Flag;
    public event Action<string>? Named;
}

class Theta
{
    public event Action? Raised;
    public void Fire() => Raised?.Invoke();
}
