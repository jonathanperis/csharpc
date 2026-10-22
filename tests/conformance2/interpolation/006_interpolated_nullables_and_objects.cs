int? a = null, b = 7;
object? o = null;
Console.WriteLine($"[{a}] [{b}] [{o}] [{new P()}] [{DayOfWeek.Monday}]");
class P { public override string ToString() => "P!"; }
