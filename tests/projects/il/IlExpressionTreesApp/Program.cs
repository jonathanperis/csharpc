using IlExpressionTreesLib;

var add = Trees.AddCaptured(5);
Console.WriteLine(add);
Console.WriteLine(add.Compile()(10));

var filter = Trees.Filter("b", 1);
Console.WriteLine(filter);
var isMatch = filter.Compile();
Console.WriteLine($"{isMatch(new Box("b", 2))} {isMatch(new Box("b", 1))} {isMatch(new Box("c", 5))}");

var arithmetic = Trees.Arithmetic();
Console.WriteLine(arithmetic);
Console.WriteLine(arithmetic.Compile()(new Box("x", 4) { Size = Size.Large }));

var create = Trees.Create();
Console.WriteLine(create);
Console.WriteLine(create.Compile()(7));

var query = Trees.Query();
Console.WriteLine(query);
Console.WriteLine(query.Compile()(new[] { new Box("d", 4), new Box("a", 2), new Box("z", 3) }));

var nullable = Trees.Nullable();
Console.WriteLine(nullable);
var describe = nullable.Compile();
Console.WriteLine(describe(3, "s") + " " + describe(null, null));

var typeTests = Trees.TypeTests();
Console.WriteLine(typeTests);
var classify = typeTests.Compile();
Console.WriteLine($"{classify(new Box("boxed", 1))} {classify("text")} {classify(42)}");

var quoted = Trees.Quoted();
Console.WriteLine(quoted);
var inner = quoted.Compile()(6);
Console.WriteLine(inner + " -> " + inner.Compile()(7));

var arrays = Trees.Arrays();
Console.WriteLine(arrays);
Console.WriteLine(string.Join(",", arrays.Compile()(v => v * 100)));

foreach (var tree in Trees.InLoop())
{
    Console.WriteLine(tree + " = " + tree.Compile().DynamicInvoke());
}
