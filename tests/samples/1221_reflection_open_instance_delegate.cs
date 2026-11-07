using System;
using System.Collections.Generic;
using System.Reflection;

// MethodInfo.CreateDelegate without a target on an instance method: an open instance delegate whose
// first parameter is the receiver (System.Text.Json's source generator reads Roslyn's newer members this way).
MethodInfo isClosed = typeof(IShape).GetProperty("Sides")!.GetMethod!;
var sides = (Func<IShape, int>)isClosed.CreateDelegate(typeof(Func<IShape, int>));
Console.WriteLine($"{sides(new Square())} {sides(new Triangle())}");

// A virtual method on a class dispatches on the receiver; an override is reached through the base type.
MethodInfo describe = typeof(Shape).GetMethod("Describe")!;
var describeOpen = (Func<Shape, string, string>)describe.CreateDelegate(typeof(Func<Shape, string, string>));
Console.WriteLine(describeOpen(new Square(), "a"));
Console.WriteLine(describeOpen(new Triangle(), "b"));

// A non-virtual method, a void method with a parameter, a derived receiver type.
MethodInfo add = typeof(Bag).GetMethod("Add")!;
var addOpen = (Action<Bag, string>)add.CreateDelegate(typeof(Action<Bag, string>));
Bag bag = new();
addOpen(bag, "x");
addOpen(bag, "y");
Console.WriteLine(string.Join(",", bag.Items));
MethodInfo count = typeof(Bag).GetMethod("get_Count")!;
var countOpen = (Func<Bag, int>)count.CreateDelegate(typeof(Func<Bag, int>));
Console.WriteLine(countOpen(bag));
Console.WriteLine(countOpen.Target is null);

// The receiver may be typed more loosely than the declaring type only if the declaring type is assignable from it.
try { add.CreateDelegate(typeof(Action<string, string>)); }
catch (ArgumentException) { Console.WriteLine("receiver type"); }
try { add.CreateDelegate(typeof(Action<Bag>)); }
catch (ArgumentException) { Console.WriteLine("parameter count"); }

// A null receiver is a NullReferenceException when the delegate is invoked.
try { countOpen(null!); }
catch (NullReferenceException) { Console.WriteLine("null receiver"); }

// An enum and its underlying type are interchangeable in the signature (System.Text.Json's generator reads
// Roslyn's MemorySafetyRulesVersion enum property through a Func<IModuleSymbol, int>).
MethodInfo level = typeof(IShape).GetProperty("Level")!.GetMethod!;
var levelOpen = (Func<IShape, int>)level.CreateDelegate(typeof(Func<IShape, int>));
Console.WriteLine($"{levelOpen(new Square())} {levelOpen(new Triangle())}");

// With a target, the delegate is closed over it.
var closed = (Action<string>)add.CreateDelegate(typeof(Action<string>), bag);
closed("z");
Console.WriteLine(countOpen(bag));

enum Rules { Legacy = 1, Updated = 2 }

interface IShape { int Sides { get; } Rules Level { get; } }

abstract class Shape : IShape
{
    public abstract int Sides { get; }
    public virtual Rules Level => Rules.Legacy;
    public virtual string Describe(string tag) => $"{tag}:shape/{Sides}";
}

sealed class Square : Shape
{
    public override int Sides => 4;
    public override Rules Level => Rules.Updated;
    public override string Describe(string tag) => $"{tag}:square/{Sides}";
}

sealed class Triangle : Shape
{
    public override int Sides => 3;
}

sealed class Bag
{
    public List<string> Items { get; } = new();
    public void Add(string item) => Items.Add(item);
    public int Count => Items.Count;
}
