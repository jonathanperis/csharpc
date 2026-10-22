Console.WriteLine(new List<int>().GetType().Name);
Console.WriteLine(new Dictionary<string, List<int>>().GetType().Name);
Console.WriteLine(typeof(Box<double>).Name);
Console.WriteLine(new Box<int>());
class Box<T> { }
