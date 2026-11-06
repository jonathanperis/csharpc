// Type.GetFields/GetField list the compiler-generated private backing field of every field-like event, as .NET does
// (Roslyn hides it from its symbols): named like the event, of the handler type, private, static for a static event, in
// declaration order among the fields. Covers the order, binding flags, DeclaredOnly and inheritance (a base class's
// private backing field isn't listed), a virtual and an internal event, an event with explicit accessors (no field),
// a generic class, the FieldAttributes and compiler attributes, and GetValue/SetValue reading and replacing the
// delegate (the program's own `+=` and invocation see the change).
#nullable disable
#pragma warning disable CS0067, CS0169, CS0649, CS0414
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

const DynamicallyAccessedMemberTypes AllFields = DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields;

ShowAll(typeof(Widget));
ShowAll(typeof(Gadget));
ShowAll(typeof(Box<int>));
ShowAll(typeof(Plain));

static void ShowAll([DynamicallyAccessedMembers(AllFields)] Type type)
{
    Console.WriteLine("== " + type);
    foreach (var flags in new[]
    {
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static,
        BindingFlags.NonPublic | BindingFlags.Instance,
        BindingFlags.NonPublic | BindingFlags.Static,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy,
    })
        Console.WriteLine($"-- {flags}: " + string.Join(", ", type.GetFields(flags).Select(f => $"{f.Name}@{f.DeclaringType.Name}")));
}

foreach (var f in typeof(Widget).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
{
    Console.WriteLine($"{f.Name}: {f.FieldType} [{f.Attributes}] private={f.IsPrivate} static={f.IsStatic} initonly={f.IsInitOnly} special={f.IsSpecialName} literal={f.IsLiteral}");
    Console.WriteLine($"   {f} | {f.MemberType} {f.DeclaringType} {f.ReflectedType} {f.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)}");
    foreach (var a in f.CustomAttributes)
        Console.WriteLine($"   [{a.AttributeType.Name}] {string.Join(",", a.ConstructorArguments)}");
    Console.WriteLine($"   {f.GetCustomAttributes(false).Length} attributes");
}

Console.WriteLine("== GetField");
var flagsAll = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
Console.WriteLine(typeof(Widget).GetField("Changed") is null);
Console.WriteLine(typeof(Widget).GetField("Changed", BindingFlags.Public | BindingFlags.Instance) is null);
Console.WriteLine(typeof(Widget).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static) is null);
Console.WriteLine(typeof(Widget).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance)?.Name);
Console.WriteLine(typeof(Widget).GetField("changed", BindingFlags.NonPublic | BindingFlags.Instance) is null);
Console.WriteLine(typeof(Widget).GetField("changed", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase)?.Name);
Console.WriteLine(typeof(Widget).GetField("Tick", flagsAll)?.IsStatic);
Console.WriteLine(typeof(Widget).GetField("Custom", flagsAll) is null);
Console.WriteLine(typeof(Gadget).GetField("Changed", flagsAll) is null);
Console.WriteLine(typeof(Gadget).GetField("Fired", flagsAll)?.DeclaringType);
Console.WriteLine(typeof(Gadget).GetField("Tick", flagsAll | BindingFlags.FlattenHierarchy) is null);
Console.WriteLine(typeof(Widget).GetField("Changed", flagsAll) == typeof(Widget).GetField("Changed", flagsAll));

Console.WriteLine("== GetValue / SetValue");
var widget = new Widget();
var changed = typeof(Widget).GetField("Changed", flagsAll)!;
Console.WriteLine(changed.GetValue(widget) is null);
widget.Changed += (s, e) => Console.WriteLine("first handler");
widget.Changed += (s, e) => Console.WriteLine("second handler");
var current = (Delegate)changed.GetValue(widget)!;
Console.WriteLine(current.GetType() + " " + current.GetInvocationList().Length + " " + ReferenceEquals(current, widget.Current));
widget.Raise();
changed.SetValue(widget, new EventHandler((s, e) => Console.WriteLine("replaced")));
widget.Raise();
changed.SetValue(widget, null);
Console.WriteLine(changed.GetValue(widget) is null);
widget.Raise();
GC.Collect();
changed.SetValue(widget, new EventHandler((s, e) => Console.WriteLine("kept alive")));
GC.Collect();
widget.Raise();
try { changed.SetValue(widget, "text"); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
try { changed.GetValue(null); } catch (TargetException e) { Console.WriteLine(e.GetType().Name); }
try { changed.GetValue(new Gadget2()); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
// Another instance has its own slot.
var other = new Widget();
Console.WriteLine(changed.GetValue(other) is null);

var tick = typeof(Widget).GetField("Tick", flagsAll)!;
Console.WriteLine(tick.GetValue(null) is null);
Widget.Tick += () => Console.WriteLine("static tick");
((Action)tick.GetValue(null)).Invoke();
tick.SetValue(null, (Action)(() => Console.WriteLine("swapped tick")));
Widget.RaiseTick();
tick.SetValue(null, null);
Console.WriteLine(tick.GetValue(null) is null);
Widget.RaiseTick();

var counted = typeof(Box<int>).GetField("Counted", flagsAll)!;
var box = new Box<int>();
box.Counted += n => Console.WriteLine("counted " + n);
((Action<int>)counted.GetValue(box)).Invoke(7);
counted.SetValue(box, (Action<int>)(n => Console.WriteLine("counted again " + n)));
box.Count(8);

public class Widget
{
    public int Id;
    public event EventHandler Changed;
    public static string Name = "w";
    private long _hidden;
    public static event Action Tick;
    public event EventHandler Custom { add { } remove { } }
    public virtual event Action Virtual;
    protected internal event Action<int> Counted;
    internal static event Action<string> InternalTick;
    private const int Limit = 3;

    public Delegate Current => Changed;
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    public static void RaiseTick() => Tick?.Invoke();
}

public class Gadget : Widget
{
    public event Action Fired;
    public override event Action Virtual;
}

public class Gadget2 { }

public class Box<T>
{
    public event Action<T> Counted;
    public static event Action<T> StaticCounted;
    public void Count(T value) => Counted?.Invoke(value);
}

public class Plain
{
    public int A;
    public string B;
}
