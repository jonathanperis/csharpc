using Shared;

object a = FactoryA.Array(), b = FactoryB.Array();
Console.WriteLine($"{FactoryA.IsArray(a)} {FactoryB.IsArray(a)} {FactoryA.IsArray(b)} {FactoryB.IsArray(b)}");
Console.WriteLine(a.GetType() == b.GetType());
Console.WriteLine(FactoryA.ArrayType() == FactoryB.ArrayType());
Console.WriteLine(a.GetType() == FactoryA.ArrayType());
var objects = (object[])a;
objects[0] = FactoryA.Node();
try { objects[1] = FactoryB.Node(); Console.WriteLine("stored " + objects[1]); }
catch (ArrayTypeMismatchException) { Console.WriteLine("mismatch"); }
Console.WriteLine(objects[0]);
Console.WriteLine($"{FactoryA.IsNode(FactoryB.Node())} {FactoryB.IsNode(FactoryB.Node())}");
object ta = FactoryA.Task(), tb = FactoryB.Task();
Console.WriteLine($"{FactoryA.IsTask(tb)} {FactoryB.IsTask(tb)} {ta.GetType() == tb.GetType()}");
object la = FactoryA.List(), lb = FactoryB.List();
Console.WriteLine($"{la.GetType() == lb.GetType()} {la is IEnumerable<object>}");
