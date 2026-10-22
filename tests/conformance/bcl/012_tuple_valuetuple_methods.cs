var t = Tuple.Create(1, "a");
Console.WriteLine(t.Item1 + t.Item2);
var vt = ValueTuple.Create(2, 3.5);
Console.WriteLine(vt);
Console.WriteLine(KeyValuePair.Create("k", 1));
