// Min and Max of empty sequences: .NET's Enumerable takes arrays and lists of integers and of other
// value types (not double, float, decimal) through MemoryExtensions, whose message ends with a period;
// other sources keep Enumerable's. Spans of reference and nullable types skip nulls and give null
// when empty, as Enumerable does.
using System;
using System.Collections.Generic;
using System.Linq;

T("int[] Max", () => new int[0].Max());
T("List<int> Max", () => new List<int>().Max());
T("List<int> Min", () => new List<int>().Min());
T("List<long> Max", () => new List<long>().Max());
T("List<short> Max", () => new List<short>().Max());
T("List<DateTime> Max", () => new List<DateTime>().Max());
T("List<int> Max(comparer)", () => new List<int>().Max(Comparer<int>.Default));
T("int[] as IEnumerable<int> Max", () => AsEnumerable(new int[0]).Max());
T("DateTime[] as IEnumerable Max", () => AsEnumerable(new DateTime[0]).Max());
T("iterator Max", () => Iterator<int>().Max());
T("iterator Min", () => Iterator<long>().Min());
T("List<double> Max", () => new List<double>().Max());
T("List<float> Min", () => new List<float>().Min());
T("List<decimal> Max", () => new List<decimal>().Max());
T("double[] as IEnumerable Min", () => AsEnumerable(new double[0]).Min());
T("selector Max", () => new int[0].Max(x => x));
T("Average", () => new List<int>().Average());
T("List<string> Max", () => new List<string>().Max());
T("List<int?> Max", () => new List<int?>().Max());
T("string[] Max", () => new string[0].Max());
T("string[] Max with nulls", () => new string?[] { null, "b", "a" }.Max());
T("string[] Min with nulls", () => new string?[] { null, "b", "a" }.Min());
T("int?[] Max", () => new int?[0].Max());
T("int?[] Min with nulls", () => new int?[] { null, 3, 1 }.Min());
T("int?[] Max with nulls", () => new int?[] { 2, null, 5 }.Max());

static IEnumerable<X> AsEnumerable<X>(X[] items) => items;
static IEnumerable<X> Iterator<X>() { yield break; }

static void T(string name, Func<object?> f)
{
    try { Console.WriteLine(name + ": " + (f() ?? "null")); }
    catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
}
