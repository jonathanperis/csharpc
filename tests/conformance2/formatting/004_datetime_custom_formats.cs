var d = new DateTime(2024, 3, 7, 9, 5, 3, 45);
Console.WriteLine(d.ToString("yyyy-MM-dd HH:mm:ss.fff"));
Console.WriteLine(d.ToString("d/M/yy h:m tt"));
Console.WriteLine(d.ToString("dddd, MMMM dd"));
Console.WriteLine(d.ToString("o"));
Console.WriteLine(d.ToString("s"));
Console.WriteLine(new TimeSpan(1, 2, 3, 4, 5).ToString());
Console.WriteLine(TimeSpan.FromMinutes(90).ToString(@"hh\:mm"));
