var d = new DateTime(2024, 2, 28);
Console.WriteLine(d.AddDays(1).ToString("yyyy-MM-dd"));
Console.WriteLine(d.DayOfWeek);
var t = TimeSpan.FromMinutes(90);
Console.WriteLine(t.TotalHours + " " + t);
Console.WriteLine((new DateTime(2024, 3, 1) - d).Days);
Console.WriteLine(DateTime.IsLeapYear(2024));
