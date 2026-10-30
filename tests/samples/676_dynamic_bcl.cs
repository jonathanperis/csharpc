// dynamic over BCL types: enumerating an ExpandoObject, tuples, Math/string overloads by run-time type, DateTime,
// TimeSpan, KeyValuePair, arrays through LINQ, nullable values and char helpers.
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

class P
{
    static void Main()
    {
        dynamic expando = new ExpandoObject();
        expando.A = 1;
        expando.B = "two";
        foreach (var pair in expando) Console.WriteLine(pair.Key + "=" + pair.Value);
        IEnumerable<KeyValuePair<string, object>> entries = expando;
        Console.WriteLine(entries.Count());

        dynamic tuple = (1, "a");
        Console.WriteLine(tuple.Item1 + tuple.Item2);
        (int, string) back = tuple;
        Console.WriteLine(back.Item2);

        dynamic x = 2.345;
        Console.WriteLine(Math.Round(x, 2));
        x = 2.345m;
        Console.WriteLine(Math.Round(x, 1));
        Console.WriteLine(string.Format("{0}-{1}", x, 7));
        Console.WriteLine(string.Join("|", new object[] { x, 1 }));

        dynamic date = new DateTime(2024, 2, 29, 13, 45, 0);
        Console.WriteLine(date.Year + date.Month + date.Day);
        Console.WriteLine(date.AddDays(1).ToString("yyyy-MM-dd"));
        Console.WriteLine(date.DayOfWeek);
        dynamic span = TimeSpan.FromMinutes(90);
        Console.WriteLine(span.TotalHours);
        Console.WriteLine((date + span).Hour);

        dynamic kv = new KeyValuePair<string, int>("k", 3);
        Console.WriteLine(kv.Key + kv.Value);
        dynamic numbers = new[] { 3, 1, 2 };
        Console.WriteLine(string.Join(",", ((IEnumerable<int>)numbers).OrderBy(n => n)));
        dynamic maybe = (int?)null;
        Console.WriteLine(maybe == null);
        dynamic c = 'x';
        Console.WriteLine(char.IsLetter(c));
        Console.WriteLine(char.ToUpper(c));
    }
}
