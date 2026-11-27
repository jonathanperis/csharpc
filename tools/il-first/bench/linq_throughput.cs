// System.Linq throughput, the program behind the numbers of ADR 0023's Linq row:
//   tools/il-first/ab.sh System.Linq tools/il-first/bench/linq_throughput.cs
// Deferred pipelines over arrays, Lists and ranges, the span aggregates (Sum/Min/Max/Average/Contains/SequenceEqual), sorting,
// grouping, joins, set operations and materializing. The output is checksums only (deterministic under real .NET).
using System;
using System.Collections.Generic;
using System.Linq;

class Program
{
    record Person(int Id, string Name, int Age, string City);

    static void Main()
    {
        long check = 0;
        var ints = Enumerable.Range(0, 200_000).Select(i => (i * 7919) % 100_003).ToArray();
        var longs = ints.Select(i => (long)i * 31).ToArray();
        var doubles = ints.Select(i => i / 3.0).ToArray();
        var list = ints.ToList();
        var small = ints.Take(20_000).ToArray(); // int sums stay below int.MaxValue (the sums are checked)
        var smallList = small.ToList();

        // span aggregates over arrays and Lists (the vectorized paths of the framework's Enumerable)
        for (int round = 0; round < 150; round++)
        {
            check += small.Sum() + ints.Min() + ints.Max() + (long)ints.Average();
            check += longs.Sum() + longs.Min() + longs.Max();
            check += (long)(doubles.Sum() + doubles.Min() + doubles.Max());
            check += smallList.Sum() + list.Max();
            check += ints.Contains(-1) ? 1 : 0;
            check += ints.SequenceEqual(list) ? 1 : 0;
            check += ints.Count(i => i % 3 == 0);
        }

        // deferred pipelines
        for (int round = 0; round < 40; round++)
        {
            check += ints.Where(i => (i & 1) == 0).Select(i => (long)i + 1).Where(i => i % 3 != 0).Sum();
            check += list.Where(i => i > 5000).Select(i => (long)i * 2).Sum();
            check += Enumerable.Range(0, 100_000).Select(i => i * 2).Where(i => i % 5 == 0).Count();
            check += ints.Skip(1000).Take(50_000).Select(i => i).Max();
            check += ints.Take(1000).Concat(ints.Skip(5000).Take(1000)).Distinct().Count();
            check += ints.Select((v, i) => v ^ i).Take(100_000).Aggregate(0L, (a, v) => a + v);
            check += ints.Zip(longs, (a, b) => a + b).Take(50_000).Sum();
            check += ints.Any(i => i == 100_002) ? 1 : 0;
            check += ints.All(i => i >= 0) ? 1 : 0;
            check += ints.First(i => i > 1000) + ints.Last(i => i < 50) + ints.ElementAt(12345);
            check += ints.Where(i => i < 20_000).ToList().Count + ints.Where(i => i < 20_000).ToArray().Length;
        }

        // sorting, grouping, joins, sets, dictionaries
        var people = Enumerable.Range(0, 20_000).Select(i => new Person(i, "p" + (i % 977), 18 + (i * 13) % 60, "city" + (i % 41))).ToList();
        for (int round = 0; round < 25; round++)
        {
            check += people.OrderBy(p => p.Age).ThenByDescending(p => p.Name).Select(p => (long)p.Id).Take(100).Sum();
            check += people.OrderByDescending(p => p.Id).First().Id;
            foreach (var g in people.GroupBy(p => p.City)) check += g.Count() + g.Max(p => p.Age);
            check += people.GroupBy(p => p.Age % 10).ToDictionary(g => g.Key, g => g.Sum(p => (long)p.Id)).Values.Sum();
            check += people.ToLookup(p => p.Name)["p5"].Count();
            check += people.Join(people.Take(500), a => a.Id % 500, b => b.Id, (a, b) => (long)a.Age + b.Age).Take(20_000).Sum();
            check += ints.Take(20_000).Distinct().Count() + ints.Take(5000).Union(ints.Skip(3000).Take(5000)).Count()
                + ints.Take(5000).Intersect(ints.Skip(3000).Take(5000)).Count() + ints.Take(5000).Except(ints.Skip(3000).Take(5000)).Count();
            check += ints.Take(30_000).OrderBy(i => i).Skip(100).First();
            check += people.Select(p => p.Name).Distinct().Count();
            check += people.ToDictionary(p => p.Id, p => p.Age).Count;
            check += people.SelectMany(p => new[] { p.Id, p.Age }).Take(10_000).Sum(i => (long)i);
            check += people.Where(p => p.City == "city7").Select(p => p.Name.Length).DefaultIfEmpty(0).Average() > 1 ? 1 : 0;
        }
        Console.WriteLine(check);
    }
}
