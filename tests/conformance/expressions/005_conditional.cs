int x = 7;
Console.WriteLine(x > 5 ? "big" : "small");
var r = x % 2 == 0 ? 1.5 : 2;
Console.WriteLine(r);
ref int pick = ref (x > 0 ? ref x : ref x);
Console.WriteLine(pick);
