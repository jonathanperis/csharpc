// Type.GetEvent / GetEvents and EventInfo over compile-time metadata: names, handler types, accessors (public and
// not), static events, inherited events (hiding by name, private ones of base types left out, FlattenHierarchy),
// explicit interface implementations, virtual events and their overrides, binding flags, DeclaredOnly,
// AddEventHandler/RemoveEventHandler (and their errors for a non-public event), IsSpecialName, Attributes,
// ReflectedType/DeclaringType, ToString, identity of the objects, and a [DynamicallyAccessedMembers] flow.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

const DynamicallyAccessedMemberTypes AllEvents = DynamicallyAccessedMemberTypes.PublicEvents | DynamicallyAccessedMemberTypes.NonPublicEvents;

ShowAll(typeof(Base));
ShowAll(typeof(Derived));
ShowAll(typeof(ISignal));
ShowAll(typeof(Impl));
ShowAll(typeof(Statics));
ShowAll(typeof(StaticsDerived));

static void ShowAll([DynamicallyAccessedMembers(AllEvents)] Type type)
{
    Console.WriteLine("== " + type);
    Show(type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
    Show(type, BindingFlags.NonPublic | BindingFlags.Instance);
    Show(type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    Show(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
    Show(type, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
}

static void Show([DynamicallyAccessedMembers(AllEvents)] Type type, BindingFlags flags)
{
    Console.WriteLine($"-- {flags}: " + string.Join(", ", type.GetEvents(flags).Select(e => $"{e.Name}@{e.DeclaringType!.Name}")));
}

Console.WriteLine("== GetEvents()");
foreach (var e in typeof(Derived).GetEvents())
    Describe(e);

Console.WriteLine("== GetEvent");
Lookup(typeof(Base), "Changed");
Lookup(typeof(Derived), "Changed");
Lookup(typeof(Derived), "changed");
Lookup(typeof(Base), "Hidden");
Lookup(typeof(Derived), "Hidden");
Lookup(typeof(Derived), "Counted");
Lookup(typeof(Derived), "Missing");
Lookup(typeof(Base), "Overridable");
Lookup(typeof(Derived), "Overridable");
Lookup(typeof(Impl), "Raised");
Lookup(typeof(ISignal), "Raised");
Lookup(typeof(Statics), "Tick");
Lookup(typeof(StaticsDerived), "Tick");
Lookup(typeof(Base), "Secret");
Lookup(typeof(Derived), "Secret");
Lookup(typeof(Derived), "Guarded");

static void Lookup([DynamicallyAccessedMembers(AllEvents)] Type type, string name)
{
    foreach (var flags in new[] { BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                 BindingFlags.NonPublic | BindingFlags.Instance, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy })
    {
        try
        {
            var found = type.GetEvent(name, flags);
            Console.WriteLine($"{type.Name}.GetEvent({name}, {flags}) = {(found is null ? "null" : found + " @" + found.DeclaringType!.Name + "/" + found.ReflectedType!.Name)}");
        }
        catch (Exception ex) { Console.WriteLine($"{type.Name}.GetEvent({name}, {flags}) !! {ex.GetType().Name}: {ex.Message}"); }
    }
}
Console.WriteLine(typeof(Base).GetEvent("Changed") is { Name: "Changed" });
Console.WriteLine(typeof(Base).GetEvent("Changed") == typeof(Base).GetEvent("Changed"));
Console.WriteLine(ReferenceEquals(typeof(Base).GetEvent("Changed"), typeof(Base).GetEvents().First(e => e.Name == "Changed")));
Console.WriteLine(typeof(Base).GetEvent("Changed") == typeof(Derived).GetEvent("Changed"));

Console.WriteLine("== handlers");
var instance = new Derived();
var changed = typeof(Derived).GetEvent("Changed")!;
int calls = 0;
EventHandler handler = (_, _) => calls++;
changed.AddEventHandler(instance, handler);
changed.AddEventHandler(instance, handler);
instance.RaiseChanged();
changed.RemoveEventHandler(instance, handler);
instance.RaiseChanged();
Console.WriteLine("calls " + calls);
var tick = typeof(Statics).GetEvent("Tick")!;
tick.AddEventHandler(null, new Action<int>(n => Console.WriteLine("tick " + n)));
Statics.RaiseTick(3);
tick.AddEventHandler(null, null);
foreach (var action in new Action[]
{
    () => typeof(Derived).GetEvent("Changed")!.AddEventHandler(null, handler),
    () => changed.AddEventHandler(new object(), handler),
    () => changed.AddEventHandler(instance, new Action<int>(_ => { })),
    () => typeof(Base).GetEvent("Secret", BindingFlags.NonPublic | BindingFlags.Instance)!.AddEventHandler(new Base(), handler),
    () => typeof(Base).GetEvent("Secret", BindingFlags.NonPublic | BindingFlags.Instance)!.RemoveEventHandler(new Base(), handler),
    () => Console.WriteLine(typeof(Base).GetEvent("Secret", BindingFlags.NonPublic | BindingFlags.Instance)!.ToString()),
    () => typeof(Base).GetEvent(null!),
})
{
    try { action(); }
    catch (Exception ex) { Console.WriteLine($"{ex.GetType().Name}: {ex.Message}"); }
}
var overridable = new Derived();
typeof(Base).GetEvent("Overridable")!.AddEventHandler(overridable, handler);
Console.WriteLine(overridable.OverridableCount);

Console.WriteLine("== members");
DescribeDeclared(typeof(Base));
DescribeDeclared(typeof(Impl));
DescribeDeclared(typeof(Statics));
DescribeDeclared(typeof(Plain));

static void DescribeDeclared([DynamicallyAccessedMembers(AllEvents)] Type type)
{
    foreach (var e in type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        Describe(e);
}

Console.WriteLine("== attributes");
foreach (var e in typeof(Plain).GetEvents())
{
    Console.WriteLine($"{e.Name}: " + string.Join(", ", e.GetCustomAttributes(false).Select(a => a.GetType().Name)) + $" | defined tag={e.IsDefined(typeof(TagAttribute), false)} obsolete={e.IsDefined(typeof(ObsoleteAttribute), true)}"
        + $" | {e.GetCustomAttribute<TagAttribute>()?.Name} | {string.Join(",", e.GetCustomAttributesData().Select(d => d.AttributeType.Name + "(" + string.Join(",", d.ConstructorArguments.Select(c => c.Value)) + ")"))}");
    Console.WriteLine("  accessor attributes: " + string.Join(", ", e.AddMethod!.GetCustomAttributes(false).Select(a => a.GetType().Name)) + " / " + string.Join(", ", e.RemoveMethod!.GetCustomAttributes(false).Select(a => a.GetType().Name))
        + $" | {e.AddMethod!.GetMethodImplementationFlags()} {e.AddMethod!.Attributes}");
}
Console.WriteLine(typeof(Plain).GetEvent("Tagged")!.AddMethod == typeof(Plain).GetMethod("add_Tagged"));
Console.WriteLine(typeof(Plain).GetEvent("Tagged")!.RemoveMethod == typeof(Plain).GetMethod("remove_Tagged"));
Console.WriteLine(typeof(Plain).GetEvent("Tagged")!.GetAddMethod() == typeof(Plain).GetEvent("Tagged")!.AddMethod);

Console.WriteLine("== dynamically accessed");
Dump(typeof(Plain));
Dump(typeof(Derived));
Console.WriteLine(typeof(Derived).GetEvents(BindingFlags.NonPublic | BindingFlags.Instance).Length);

static void Dump([DynamicallyAccessedMembers(AllEvents)] Type type)
{
    Console.WriteLine("dump " + type.Name + ": " + string.Join(", ", type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(e => e.Name)));
}

static void Describe(EventInfo e)
{
    Console.WriteLine($"{e.Name}: type={e.EventHandlerType} multicast={e.IsMulticast} {e.MemberType} declaring={e.DeclaringType} reflected={e.ReflectedType} attributes={e.Attributes} special={e.IsSpecialName}");
    Console.WriteLine($"  add={e.AddMethod?.Name} remove={e.RemoveMethod?.Name} raise={e.RaiseMethod?.Name} publicAdd={e.GetAddMethod()?.Name} publicRemove={e.GetRemoveMethod()?.Name} publicRaise={e.GetRaiseMethod()?.Name}");
    Console.WriteLine($"  addPublic={e.AddMethod?.IsPublic} static={e.AddMethod?.IsStatic} virtual={e.AddMethod?.IsVirtual} others={e.GetOtherMethods(true).Length}/{e.GetOtherMethods().Length}");
    Console.WriteLine($"  param={e.AddMethod?.GetParameters().Length} {e.AddMethod?.GetParameters()[0].ParameterType} return={e.AddMethod?.ReturnType}");
    try { Console.WriteLine("  " + e); }
    catch (Exception ex) { Console.WriteLine($"  !! {ex.GetType().Name}: {ex.Message}"); }
}

class Base
{
    public event EventHandler? Changed;
    public event EventHandler? Hidden;
    public event Action<int>? Counted;
    private event EventHandler? Secret;
    protected event EventHandler? Guarded;
    internal event EventHandler? Internal;
    public virtual event EventHandler? Overridable;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    public void Touch() { Hidden?.Invoke(this, EventArgs.Empty); Counted?.Invoke(1); Secret?.Invoke(this, EventArgs.Empty); Guarded?.Invoke(this, EventArgs.Empty); Internal?.Invoke(this, EventArgs.Empty); Overridable?.Invoke(this, EventArgs.Empty); }
}

class Derived : Base
{
    public new event EventHandler? Hidden;
    public event EventHandler? Own;
    private int _overridable;
    public int OverridableCount => _overridable;
    public override event EventHandler? Overridable
    {
        add => _overridable++;
        remove => _overridable--;
    }
    public void Raise() { Hidden?.Invoke(this, EventArgs.Empty); Own?.Invoke(this, EventArgs.Empty); }
}

interface ISignal
{
    event EventHandler Raised;
}

class Impl : ISignal
{
    event EventHandler ISignal.Raised { add { } remove { } }
    public event EventHandler? Other;
    public void Use() => Other?.Invoke(this, EventArgs.Empty);
}

class Statics
{
    public static event Action<int>? Tick;
    private static event EventHandler? Quiet;
    public event EventHandler? PerInstance;
    public static void RaiseTick(int n) { Tick?.Invoke(n); Quiet?.Invoke(null, EventArgs.Empty); }
    public void Fire() => PerInstance?.Invoke(this, EventArgs.Empty);
}

class StaticsDerived : Statics
{
    public event EventHandler? Another;
    public void Use() => Another?.Invoke(this, EventArgs.Empty);
}

[AttributeUsage(AttributeTargets.Event)]
sealed class TagAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

#nullable disable
class Plain
{
    [Tag("first")]
    public event EventHandler Tagged;
    [Obsolete("old")]
    public event EventHandler Old;
    public event EventHandler Untouched;
    public void Use() { Tagged?.Invoke(this, EventArgs.Empty); Old?.Invoke(this, EventArgs.Empty); Untouched?.Invoke(this, EventArgs.Empty); }
}
