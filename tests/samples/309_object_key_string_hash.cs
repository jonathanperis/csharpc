// Strings used through object (Dictionary<object, ...>, HashSet<object>, object.GetHashCode) hash by
// content, so equal strings built at run time find each other. (Regression: the string method
// table's GetHashCode slot was the identity hash.)
using System;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        object literal = "year";
        object built = new string(new[] { 'y', 'e', 'a', 'r' });
        Console.WriteLine(literal.Equals(built));
        Console.WriteLine(literal.GetHashCode() == built.GetHashCode());
        Console.WriteLine(EqualityComparer<object>.Default.GetHashCode(literal) == EqualityComparer<object>.Default.GetHashCode(built));

        var table = new Dictionary<object, int> { { "alpha", 1 }, { 42, 2 } };
        string key = string.Concat("al", "pha".ToString());
        Console.WriteLine(table.ContainsKey(key) + " " + table[key] + " " + table.ContainsKey(40 + 2));

        var set = new HashSet<object>();
        for (int i = 0; i < 3; i++) set.Add("item" + (i % 2));
        Console.WriteLine(set.Count + " " + set.Contains("item" + 1));
    }
}
