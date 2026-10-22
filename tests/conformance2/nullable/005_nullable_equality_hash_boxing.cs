int? a = 3, b = 3, c = null;
Console.WriteLine(a.Equals(b) + " " + c.Equals(null) + " " + a.GetHashCode() + " " + c.GetHashCode());
object box = c;
Console.WriteLine(box is null);
Console.WriteLine(a.ToString() + "|" + c.ToString() + "|");
