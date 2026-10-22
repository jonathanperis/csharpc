var a = new DateTime(2024, 2, 28);
Console.WriteLine(a.AddDays(1).ToString("yyyy-MM-dd") + " " + a.AddDays(2).ToString("yyyy-MM-dd") + " " + DateTime.IsLeapYear(2024));
var span = new DateTime(2024, 3, 1, 12, 0, 0) - a;
Console.WriteLine($"{span} {span.TotalHours} {span.Days}");
Console.WriteLine(a.DayOfWeek + " " + a.DayOfYear + " " + a.AddMonths(1).ToString("MMM d"));
