Celsius c = new Celsius(100);
Fahrenheit f = (Fahrenheit)c;
Console.WriteLine(f.Degrees);
readonly record struct Celsius(double Degrees) { public static explicit operator Fahrenheit(Celsius c) => new(c.Degrees * 9 / 5 + 32); }
readonly record struct Fahrenheit(double Degrees);
