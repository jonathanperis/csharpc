Celsius c = new Celsius(100);
Fahrenheit f = (Fahrenheit)c;
double raw = f;
Console.WriteLine($"{f.V} {raw} {(int)(Celsius)(Fahrenheit)32.0}");
readonly struct Celsius(double v) { public double V => v; public static explicit operator Fahrenheit(Celsius c) => new(c.V * 9 / 5 + 32); public static explicit operator int(Celsius c) => (int)c.V; }
readonly struct Fahrenheit(double v) { public double V => v; public static implicit operator double(Fahrenheit f) => f.V; public static implicit operator Fahrenheit(double d) => new(d); public static explicit operator Celsius(Fahrenheit f) => new((f.V - 32) * 5 / 9); }
