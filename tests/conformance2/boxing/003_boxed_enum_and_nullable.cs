object e = DayOfWeek.Friday;
Console.WriteLine(e);
Console.WriteLine(e is DayOfWeek d ? (int)d : -1);
int? some = 3, none = null;
object bs = some, bn = none;
Console.WriteLine($"{bs} {bn == null} {bs.GetType().Name}");
