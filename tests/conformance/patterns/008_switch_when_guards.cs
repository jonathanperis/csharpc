foreach (var n in new[] { 3, 4, 15 })
{
    var r = n switch { var x when x % 15 == 0 => "fizzbuzz", var x when x % 3 == 0 => "fizz", _ => n.ToString() };
    Console.WriteLine(r);
}
