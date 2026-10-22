Day d = Day.Wed;
Console.WriteLine(d);
Console.WriteLine((int)d);
Console.WriteLine(d + 2);
Console.WriteLine(Enum.GetName(typeof(Day), 1));
Console.WriteLine(Day.Fri > Day.Mon);
enum Day { Mon, Tue, Wed, Thu, Fri }
