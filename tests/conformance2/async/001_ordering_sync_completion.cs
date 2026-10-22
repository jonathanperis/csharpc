static async Task<int> A() { Console.WriteLine("A start"); await Task.CompletedTask; Console.WriteLine("A end"); return 1; }
Console.WriteLine("before");
var t = A();
Console.WriteLine("after call");
Console.WriteLine(await t);
