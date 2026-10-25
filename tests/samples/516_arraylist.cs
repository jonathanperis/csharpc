// System.Collections.ArrayList: growth, members, sorting, enumeration and .NET's exceptions.
using System;
using System.Collections;
using System.Linq;

record Student(string FirstName, string LastName, int[] ExamScores);

class Program
{
    static void Try(string label, Action a)
    {
        try { a(); Console.WriteLine(label + ": ok"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var list = new ArrayList();
        Console.WriteLine($"{list.Count} {list.Capacity}");
        for (int i = 0; i < 5; i++) Console.Write($"{list.Add(i * 10)}:{list.Capacity} ");
        Console.WriteLine();
        list.Insert(0, "start");
        list.AddRange(new[] { 3.5, 1.25 });
        list.InsertRange(2, new object[] { 'x', null! });
        Console.WriteLine(string.Join(",", list.Cast<object>().Select(o => o ?? "null")));
        Console.WriteLine($"{list.Contains(20)} {list.Contains(null)} {list.Contains("nope")} {list.IndexOf(20)} {list.IndexOf(30, 5)} {list.LastIndexOf(3.5)}");
        list.Remove('x');
        list.RemoveAt(0);
        list.RemoveRange(0, 1);
        Console.WriteLine(string.Join(",", list.ToArray()));
        var numbers = new ArrayList { 5, 3, 9, 1, 7 };
        numbers.Sort();
        Console.WriteLine(string.Join(",", numbers.ToArray()) + " " + numbers.BinarySearch(7) + " " + numbers.BinarySearch(4));
        numbers.Reverse();
        Console.WriteLine(string.Join(",", numbers.ToArray()));
        numbers.Sort(new Desc());
        Console.WriteLine(string.Join(",", numbers.ToArray()));
        var words = new ArrayList { "pear", "Apple", "apple", "fig" };
        words.Sort();
        Console.WriteLine(string.Join(",", words.ToArray()));
        var range = numbers.GetRange(1, 3);
        Console.WriteLine($"{range.Count} {string.Join(",", range.ToArray())}");
        var clone = (ArrayList)numbers.Clone();
        clone[0] = 100;
        Console.WriteLine($"{numbers[0]} {clone[0]}");
        var copy = new object[7];
        numbers.CopyTo(copy, 1);
        Console.WriteLine(string.Join(",", copy.Select(o => o ?? "_")));
        var repeated = ArrayList.Repeat("r", 3);
        Console.WriteLine($"{string.Join("", repeated.ToArray())} {repeated.Capacity}");
        var fromCollection = new ArrayList(new[] { 1, 2, 3 });
        fromCollection.TrimToSize();
        Console.WriteLine($"{fromCollection.Count} {fromCollection.Capacity}");
        fromCollection.Capacity = 10;
        Console.WriteLine(fromCollection.Capacity);

        var students = new ArrayList
        {
            new Student("Svetlana", "Omelchenko", new[] { 98, 92, 81, 60 }),
            new Student("Claire", "O'Donnell", new[] { 75, 84, 91, 39 }),
            new Student("Sven", "Mortensen", new[] { 88, 94, 65, 91 }),
        };
        var query = from Student s in students where s.ExamScores[0] > 80 select s;
        foreach (Student s in query) Console.WriteLine(s.LastName + ": " + s.ExamScores[0]);
        foreach (var o in students.OfType<Student>().Take(1)) Console.WriteLine(o.FirstName);

        Try("index get", () => _ = list[99]);
        Try("index set", () => list[-1] = 0);
        Try("insert", () => list.Insert(99, 0));
        Try("insert range", () => list.InsertRange(99, new[] { 1 }));
        Try("remove at", () => list.RemoveAt(99));
        Try("remove range", () => list.RemoveRange(1, 99));
        Try("ctor capacity", () => new ArrayList(-1));
        Try("ctor null", () => new ArrayList(null!));
        Try("capacity", () => list.Capacity = 0);
        Try("get range", () => list.GetRange(-1, 1));
        Try("index of", () => list.IndexOf(1, 99));
        Try("sort mixed", () => new ArrayList { 1, "a" }.Sort());
        Try("modified", () => { foreach (var o in numbers) numbers.Add(1); });
        var e = numbers.GetEnumerator();
        Try("current before", () => _ = e.Current);
        Try("repeat", () => ArrayList.Repeat(1, -1));
    }
}

class Desc : IComparer
{
    public int Compare(object? x, object? y) => ((int)y!).CompareTo((int)x!);
}
