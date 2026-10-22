int x = 5;
object a = x, b = x;
Console.WriteLine(ReferenceEquals(a, b));
Console.WriteLine(a.Equals(b));
Console.WriteLine(a == b);
object c = a;
Console.WriteLine(ReferenceEquals(a, c));
