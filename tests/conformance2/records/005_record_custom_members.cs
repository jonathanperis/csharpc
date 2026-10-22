var t = new Temp(21.5);
Console.WriteLine(t);
Console.WriteLine(t.Fahrenheit);
Console.WriteLine(t with { Celsius = 0 });
record Temp(double Celsius) {
    public double Fahrenheit => Celsius * 9 / 5 + 32;
    protected virtual bool PrintMembers(System.Text.StringBuilder sb) { sb.Append($"{Celsius}C"); return true; }
}
