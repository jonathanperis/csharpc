var t = new Thermo();
t.Reading = 50; Console.WriteLine(t.Reading);
t.Reading = -500; Console.WriteLine(t.Reading);
t.Label = "  hot "; Console.WriteLine($"[{t.Label}]");
class Thermo { public double Reading { get; set => field = Math.Max(value, -273.15); } public string Label { get => field ?? "none"; set => field = value.Trim(); } }
