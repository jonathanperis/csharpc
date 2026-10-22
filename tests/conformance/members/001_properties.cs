var t = new Temp();
t.Celsius = 100;
Console.WriteLine(t.Fahrenheit);
Console.WriteLine(t.Celsius);
class Temp
{
    private double _c;
    public double Celsius { get => _c; set => _c = value; }
    public double Fahrenheit => _c * 9 / 5 + 32;
}
