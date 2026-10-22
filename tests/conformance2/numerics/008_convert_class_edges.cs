Console.WriteLine(Convert.ToInt32(2.5) + " " + Convert.ToInt32(3.5) + " " + Convert.ToInt32("-12") + " " + Convert.ToString(255, 2) + " " + Convert.ToInt32("ff", 16));
Console.WriteLine(Convert.ToBoolean("True") + " " + Convert.ToDouble("1.5") + " " + Convert.ToChar(65));
try { Convert.ToInt32("x"); } catch (FormatException) { Console.WriteLine("FormatException"); }
