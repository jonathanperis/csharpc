// Type.IsAssignableFrom / IsAssignableTo / IsInstanceOfType / IsSubclassOf with the CLR's rules:
// base classes, interfaces (inherited, generic, variant), arrays (covariance, primitive storage
// classes), value types, Nullable<T>, delegates, null arguments.
using System;
using System.Collections;
using System.Collections.Generic;

interface IShape { double Area(); }
interface INamed { string Name { get; } }
interface ISolid : IShape { }
abstract class Shape : IShape { public abstract double Area(); }
class Circle : Shape, INamed { public string Name => "circle"; public override double Area() => 3.0; }
sealed class Ball : Circle, ISolid { }
struct Point : IEquatable<Point>, IComparable { public bool Equals(Point p) => true; public int CompareTo(object o) => 0; }
enum Color { Red, Green }
enum Small : byte { A }
interface IProducer<out T> { T Make(); }
interface IConsumer<in T> { void Take(T value); }
class Producer<T> : IProducer<T> { public T Make() => default!; }
class Consumer<T> : IConsumer<T> { public void Take(T value) { } }
class Box<T> { }
class StringBox : Box<string> { }

static class Program
{
    static void Check(string label, bool value) => Console.WriteLine($"{label,-44} {value}");

    static void Main()
    {
        Console.WriteLine("-- classes and interfaces");
        Check("Shape <- Circle", typeof(Shape).IsAssignableFrom(typeof(Circle)));
        Check("Circle <- Shape", typeof(Circle).IsAssignableFrom(typeof(Shape)));
        Check("Shape <- Ball", typeof(Shape).IsAssignableFrom(typeof(Ball)));
        Check("IShape <- Ball", typeof(IShape).IsAssignableFrom(typeof(Ball)));
        Check("IShape <- ISolid", typeof(IShape).IsAssignableFrom(typeof(ISolid)));
        Check("ISolid <- IShape", typeof(ISolid).IsAssignableFrom(typeof(IShape)));
        Check("INamed <- Ball", typeof(INamed).IsAssignableFrom(typeof(Ball)));
        Check("ISolid <- Circle", typeof(ISolid).IsAssignableFrom(typeof(Circle)));
        Check("object <- IShape", typeof(object).IsAssignableFrom(typeof(IShape)));
        Check("IShape <- object", typeof(IShape).IsAssignableFrom(typeof(object)));
        Check("Circle <- Circle", typeof(Circle).IsAssignableFrom(typeof(Circle)));
        Check("Box<string> <- StringBox", typeof(Box<string>).IsAssignableFrom(typeof(StringBox)));
        Check("Box<object> <- StringBox", typeof(Box<object>).IsAssignableFrom(typeof(StringBox)));
        Check("IDisposable <- Ball", typeof(IDisposable).IsAssignableFrom(typeof(Ball)));

        Console.WriteLine("-- value types");
        Check("object <- int", typeof(object).IsAssignableFrom(typeof(int)));
        Check("ValueType <- Point", typeof(ValueType).IsAssignableFrom(typeof(Point)));
        Check("IEquatable<Point> <- Point", typeof(IEquatable<Point>).IsAssignableFrom(typeof(Point)));
        Check("IComparable <- Point", typeof(IComparable).IsAssignableFrom(typeof(Point)));
        Check("IComparable<int> <- int", typeof(IComparable<int>).IsAssignableFrom(typeof(int)));
        Check("IFormattable <- double", typeof(IFormattable).IsAssignableFrom(typeof(double)));
        Check("long <- int", typeof(long).IsAssignableFrom(typeof(int)));
        Check("Enum <- Color", typeof(Enum).IsAssignableFrom(typeof(Color)));
        Check("int <- Color", typeof(int).IsAssignableFrom(typeof(Color)));
        Check("IConvertible <- Color", typeof(IConvertible).IsAssignableFrom(typeof(Color)));
        Check("int? <- int", typeof(int?).IsAssignableFrom(typeof(int)));
        Check("int <- int?", typeof(int).IsAssignableFrom(typeof(int?)));
        Check("int? <- long", typeof(int?).IsAssignableFrom(typeof(long)));
        Check("IComparable <- int?", typeof(IComparable).IsAssignableFrom(typeof(int?)));
        Check("object <- string", typeof(object).IsAssignableFrom(typeof(string)));
        Check("IEnumerable<char> <- string", typeof(IEnumerable<char>).IsAssignableFrom(typeof(string)));

        Console.WriteLine("-- generic collections and variance");
        Check("IEnumerable<int> <- List<int>", typeof(IEnumerable<int>).IsAssignableFrom(typeof(List<int>)));
        Check("IEnumerable <- List<int>", typeof(IEnumerable).IsAssignableFrom(typeof(List<int>)));
        Check("IList<int> <- List<long>", typeof(IList<int>).IsAssignableFrom(typeof(List<long>)));
        Check("IEnumerable<object> <- List<string>", typeof(IEnumerable<object>).IsAssignableFrom(typeof(List<string>)));
        Check("IEnumerable<object> <- List<int>", typeof(IEnumerable<object>).IsAssignableFrom(typeof(List<int>)));
        Check("IList<object> <- List<string>", typeof(IList<object>).IsAssignableFrom(typeof(List<string>)));
        Check("IReadOnlyDictionary<string,int> <- Dictionary", typeof(IReadOnlyDictionary<string, int>).IsAssignableFrom(typeof(Dictionary<string, int>)));
        Check("IProducer<Shape> <- Producer<Circle>", typeof(IProducer<Shape>).IsAssignableFrom(typeof(Producer<Circle>)));
        Check("IProducer<Circle> <- Producer<Shape>", typeof(IProducer<Circle>).IsAssignableFrom(typeof(Producer<Shape>)));
        Check("IProducer<IShape> <- IProducer<Ball>", typeof(IProducer<IShape>).IsAssignableFrom(typeof(IProducer<Ball>)));
        Check("IConsumer<Ball> <- Consumer<Shape>", typeof(IConsumer<Ball>).IsAssignableFrom(typeof(Consumer<Shape>)));
        Check("IConsumer<Shape> <- Consumer<Ball>", typeof(IConsumer<Shape>).IsAssignableFrom(typeof(Consumer<Ball>)));
        Check("IProducer<object> <- Producer<int>", typeof(IProducer<object>).IsAssignableFrom(typeof(Producer<int>)));
        Check("Func<object> <- Func<string>", typeof(Func<object>).IsAssignableFrom(typeof(Func<string>)));
        Check("Func<string> <- Func<object>", typeof(Func<string>).IsAssignableFrom(typeof(Func<object>)));
        Check("Action<string> <- Action<object>", typeof(Action<string>).IsAssignableFrom(typeof(Action<object>)));
        Check("Delegate <- Action", typeof(Delegate).IsAssignableFrom(typeof(Action)));
        Check("MulticastDelegate <- Func<int>", typeof(MulticastDelegate).IsAssignableFrom(typeof(Func<int>)));

        Console.WriteLine("-- arrays");
        Check("object[] <- string[]", typeof(object[]).IsAssignableFrom(typeof(string[])));
        Check("Shape[] <- Ball[]", typeof(Shape[]).IsAssignableFrom(typeof(Ball[])));
        Check("Ball[] <- Shape[]", typeof(Ball[]).IsAssignableFrom(typeof(Shape[])));
        Check("IShape[] <- Circle[]", typeof(IShape[]).IsAssignableFrom(typeof(Circle[])));
        Check("object[] <- int[]", typeof(object[]).IsAssignableFrom(typeof(int[])));
        Check("int[] <- uint[]", typeof(int[]).IsAssignableFrom(typeof(uint[])));
        Check("int[] <- Color[]", typeof(int[]).IsAssignableFrom(typeof(Color[])));
        Check("byte[] <- Small[]", typeof(byte[]).IsAssignableFrom(typeof(Small[])));
        Check("long[] <- int[]", typeof(long[]).IsAssignableFrom(typeof(int[])));
        Check("char[] <- ushort[]", typeof(char[]).IsAssignableFrom(typeof(ushort[])));
        Check("Array <- int[]", typeof(Array).IsAssignableFrom(typeof(int[])));
        Check("Array <- int[,]", typeof(Array).IsAssignableFrom(typeof(int[,])));
        Check("int[] <- int[,]", typeof(int[]).IsAssignableFrom(typeof(int[,])));
        Check("object[,] <- string[,]", typeof(object[,]).IsAssignableFrom(typeof(string[,])));
        Check("IList<int> <- int[]", typeof(IList<int>).IsAssignableFrom(typeof(int[])));
        Check("IReadOnlyList<object> <- string[]", typeof(IReadOnlyList<object>).IsAssignableFrom(typeof(string[])));
        Check("IList<IShape> <- Ball[]", typeof(IList<IShape>).IsAssignableFrom(typeof(Ball[])));
        Check("IEnumerable<object> <- int[]", typeof(IEnumerable<object>).IsAssignableFrom(typeof(int[])));
        Check("IList<int> <- int[,]", typeof(IList<int>).IsAssignableFrom(typeof(int[,])));
        Check("ICloneable <- string[]", typeof(ICloneable).IsAssignableFrom(typeof(string[])));
        Check("IStructuralEquatable <- int[]", typeof(IStructuralEquatable).IsAssignableFrom(typeof(int[])));
        Check("IEnumerable <- int[][]", typeof(IEnumerable).IsAssignableFrom(typeof(int[][])));
        Check("object[] <- int[][]", typeof(object[]).IsAssignableFrom(typeof(int[][])));

        Console.WriteLine("-- IsAssignableTo / IsInstanceOfType / IsSubclassOf");
        Check("Ball to IShape", typeof(Ball).IsAssignableTo(typeof(IShape)));
        Check("Shape to Circle", typeof(Shape).IsAssignableTo(typeof(Circle)));
        Check("int to null", typeof(int).IsAssignableTo(null));
        Check("null from", typeof(object).IsAssignableFrom(null));
        object[] values = [new Ball(), new Circle(), 42, "text", new Point(), Color.Green, new int[3], new List<string>(), null!];
        foreach (var value in values)
            Console.WriteLine($"{value?.GetType().Name ?? "null",-10} IShape={typeof(IShape).IsInstanceOfType(value)} ISolid={typeof(ISolid).IsInstanceOfType(value)} " +
                $"ValueType={typeof(ValueType).IsInstanceOfType(value)} int?={typeof(int?).IsInstanceOfType(value)} IEnumerable={typeof(IEnumerable).IsInstanceOfType(value)} " +
                $"IComparable={typeof(IComparable).IsInstanceOfType(value)}");
        Check("Ball subclass of Shape", typeof(Ball).IsSubclassOf(typeof(Shape)));
        Check("Ball subclass of Ball", typeof(Ball).IsSubclassOf(typeof(Ball)));
        Check("Ball subclass of IShape", typeof(Ball).IsSubclassOf(typeof(IShape)));
        Check("int subclass of ValueType", typeof(int).IsSubclassOf(typeof(ValueType)));
        Check("Color subclass of Enum", typeof(Color).IsSubclassOf(typeof(Enum)));
        Check("string[] subclass of Array", typeof(string[]).IsSubclassOf(typeof(Array)));
        Check("object subclass of object", typeof(object).IsSubclassOf(typeof(object)));
        Check("StringBox subclass of Box<string>", typeof(StringBox).IsSubclassOf(typeof(Box<string>)));
        try { typeof(Ball).IsSubclassOf(null!); }
        catch (ArgumentNullException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
        Type? missing = null;
        try { missing!.IsAssignableFrom(typeof(int)); }
        catch (NullReferenceException e) { Console.WriteLine(e.GetType().Name); }

        // A typical library check: does a runtime type implement a generic interface?
        foreach (var type in new[] { typeof(List<int>), typeof(Dictionary<string, Shape>), typeof(string), typeof(int[]), typeof(Ball) })
            Console.WriteLine($"{type.Name}: collection={typeof(IEnumerable).IsAssignableFrom(type)} disposable={typeof(IDisposable).IsAssignableFrom(type)}");
    }
}
