// Enumerable.ExceptBy / IntersectBy / UnionBy and the three-sequence Zip.
using System;
using System.Collections.Generic;
using System.Linq;

record Person(string Name, int Age);

class Program
{
    static void Main()
    {
        var people = new[] { new Person("Ann", 30), new Person("bob", 25), new Person("Cy", 30), new Person("Dee", 41) };
        Console.WriteLine(string.Join(",", people.ExceptBy(new[] { 30 }, p => p.Age).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", people.IntersectBy(new[] { 30, 41, 41 }, p => p.Age).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", people.ExceptBy(new[] { "BOB" }, p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.Name)));
        var more = new[] { new Person("Eve", 25), new Person("Fay", 19) };
        Console.WriteLine(string.Join(",", people.UnionBy(more, p => p.Age).Select(p => p.Name)));
        Console.WriteLine(string.Join(" ", new[] { 1, 2, 3, 4 }.Zip("abc", new[] { true, false, true, true }).Select(t => $"{t.First}{t.Second}{t.Third}")));
        var deferred = people.ExceptBy(Enumerable.Range(0, 0), p => p.Age);
        Console.WriteLine(deferred.Count());
        try { people.ExceptBy<Person, int>(null!, p => p.Age); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
        try { ((int[])null!).Zip("a", "b"); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
        try { people.UnionBy(more, (Func<Person, int>)null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
    }
}
