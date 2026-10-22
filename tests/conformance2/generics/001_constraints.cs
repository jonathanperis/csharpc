Console.WriteLine(Desc(5) + " " + Desc(DayOfWeek.Sunday));
Console.WriteLine(Make<List<int>>().Count);
Console.WriteLine(Sz<long>() + " " + Sz<byte>());
static string Desc<T>(T v) where T : struct => $"{typeof(T).Name}:{v}";
static T Make<T>() where T : new() => new T();
static unsafe int Sz<T>() where T : unmanaged => sizeof(T);
static string En<T>(T e) where T : struct, Enum => e.ToString();
Console.WriteLine(En(ConsoleColor.Red));
