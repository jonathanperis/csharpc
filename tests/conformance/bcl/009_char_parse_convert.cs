Console.WriteLine(char.IsDigit('5') + " " + char.IsLetter('x') + " " + char.ToUpper('q'));
Console.WriteLine(int.Parse("123") + long.Parse("-5"));
Console.WriteLine(double.Parse("2.5", System.Globalization.CultureInfo.InvariantCulture) * 2);
Console.WriteLine(int.TryParse("x", out var v) + " " + v);
Console.WriteLine(Convert.ToString(255, 2) + " " + Convert.ToInt32("ff", 16));
Console.WriteLine(bool.Parse("True"));
