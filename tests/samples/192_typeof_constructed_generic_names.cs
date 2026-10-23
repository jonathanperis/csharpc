// typeof / GetType() names of constructed generic types carry their type arguments, as
// Type.ToString prints them, while List<int> and List<string> stay distinct types.
using System.Collections.Generic;

Console.WriteLine(typeof(List<string>));
Console.WriteLine(typeof(Dictionary<int, string[]>));
Console.WriteLine(new List<int>().GetType());
Console.WriteLine(typeof(List<int>) == typeof(List<string>));
Console.WriteLine(typeof(KeyValuePair<string, List<int>>).Name);
