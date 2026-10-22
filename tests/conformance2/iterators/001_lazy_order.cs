static IEnumerable<int> Gen() { for (int i = 0; i < 3; i++) { Console.WriteLine("yield " + i); yield return i; } Console.WriteLine("end"); }
var e = Gen();
Console.WriteLine("created");
foreach (var x in e) Console.WriteLine("got " + x);
