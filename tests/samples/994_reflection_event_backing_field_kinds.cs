// Which events have a backing field in Type.GetFields: only field-like ones. An abstract event, an interface's, an
// explicit interface implementation and an event with accessors have none; an override or `new` event declared
// like a field gets its own (private fields of base classes are never inherited, so GetField on the derived type
// finds the derived field), and the order among fields, statics last, in a nullable-enabled file.
#nullable enable
#pragma warning disable CS0067, CS0169, CS0649, CS0414
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

List(typeof(Shape));
List(typeof(Circle));
List(typeof(Square));
List(typeof(IDrawable));
List(typeof(Canvas));
List(typeof(Mixed));

static void List([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] Type type)
{
    Console.WriteLine($"{type.Name}: {string.Join(", ", type.GetFields(All).Select(f => $"{f.Name}@{f.DeclaringType!.Name}{(f.IsStatic ? "/static" : "")}/{f.FieldType.Name}"))}");
    Console.WriteLine($"  declared: {string.Join(", ", type.GetFields(All | BindingFlags.DeclaredOnly).Select(f => f.Name))}");
}

Console.WriteLine(typeof(Circle).GetField("Moved", All)?.DeclaringType);
Console.WriteLine(typeof(Square).GetField("Moved", All)?.DeclaringType);
Console.WriteLine(typeof(Shape).GetField("Resized", All)?.DeclaringType);
Console.WriteLine(typeof(Circle).GetField("Resized", All) is null);
Console.WriteLine(typeof(Canvas).GetField("Drawn", All) is null);
Console.WriteLine(typeof(Canvas).GetField("Changed", All) is null);

// Separate slots per class: setting Circle's own Moved field doesn't touch the one Shape declares.
var circle = new Circle();
var circleMoved = typeof(Circle).GetField("Moved", All)!;
var shapeMoved = typeof(Shape).GetField("Moved", All)!;
circleMoved.SetValue(circle, new Action(() => Console.WriteLine("circle slot")));
Console.WriteLine(shapeMoved.GetValue(circle) is null);
circle.RaiseCircle();
circle.RaiseShape();
shapeMoved.SetValue(circle, new Action(() => Console.WriteLine("shape slot")));
circle.RaiseCircle();
circle.RaiseShape();

// Statics are shared and read without an instance; setting one runs the declaring type's initializer first.
var mixedTick = typeof(Mixed).GetField("Tick", All)!;
Console.WriteLine(mixedTick.GetValue(null) is null);
Mixed.Tick += () => Console.WriteLine("tick");
Mixed.RaiseTick();
Console.WriteLine(((Delegate)mixedTick.GetValue(null)!).GetInvocationList().Length);

abstract class Shape
{
    public abstract event Action? Resized;
    public event Action? Moved;
    public void RaiseShape() => Moved?.Invoke();
}

class Circle : Shape
{
    public override event Action? Resized;
    public new event Action? Moved;
    public void RaiseCircle() => Moved?.Invoke();
}

class Square : Shape
{
    private Action? _resized;
    public override event Action? Resized { add => _resized += value; remove => _resized -= value; }
}

interface IDrawable
{
    event Action? Drawn;
}

class Canvas : IDrawable
{
    private Action? _drawn;
    event Action? IDrawable.Drawn { add => _drawn += value; remove => _drawn -= value; }
    public event Action? Changed;
    public int Width;
}

class Mixed
{
    public static event Action? Tick;
    public static int Counter = 1;
    public string Label = "";
    public event Action? Alarm;
    public static void RaiseTick() => Tick?.Invoke();
}
