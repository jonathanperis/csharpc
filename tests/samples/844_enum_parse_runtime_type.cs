// Enum.Parse(Type, string, bool) with a Type known only at run time, as Npgsql's connection string builder
// parses "SSL Mode=Disable" into its property's enum type: names (any case when asked), numbers, [Flags]
// lists, and .NET's errors.
using System.IO;

var types = new List<Type> { typeof(DayOfWeek), typeof(FileAccess), typeof(ConsoleColor), typeof(int) };
Try(types[0], "friday", true);
Try(types[0], "Friday", false);
Try(types[0], "friday", false);
Try(types[0], " 3 ", false);
Try(types[1], "Read, Write", false);
Try(types[2], "DarkCyan", false);
Try(types[2], "", false);
Try(types[3], "1", false);
Try(types[0], null!, false);
Try(null!, "Friday", false);

static void Try(Type type, string text, bool ignoreCase)
{
    try
    {
        object value = Enum.Parse(type, text, ignoreCase);
        Console.WriteLine($"{value.GetType().Name}.{value} ({Convert.ToInt64(value)})");
    }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}
