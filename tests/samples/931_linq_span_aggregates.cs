// Enumerable over arrays and List<T> (the spans .NET's Enumerable reads directly): Sum with checked
// overflow (short and long spans, overflow that every lane width detects), floating sums in sequence order,
// Min/Max with NaN, -0.0 and infinities, Average, empty sources, Contains and SequenceEqual over
// bitwise and non-bitwise element types, ToArray, and the IList paths of First/Last/ElementAtOrDefault
// and SequenceEqual (indexer calls, Count first, a predicate run backwards).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

static class Program
{
    static void T(string label, Func<object> f)
    {
        try { Console.WriteLine(label + ": " + Format(f())); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static string Format(object o) => o switch
    {
        double d => d.ToString("R", CultureInfo.InvariantCulture) + (d == 0 && 1 / d < 0 ? " (negative zero)" : ""),
        float f => f.ToString("R", CultureInfo.InvariantCulture) + (f == 0 && 1 / f < 0 ? " (negative zero)" : ""),
        int[] a => "[" + string.Join(",", a) + "]",
        null => "null",
        _ => Convert.ToString(o, CultureInfo.InvariantCulture),
    };

    sealed class LoggingList : IList<int>
    {
        private readonly List<int> _items;
        public LoggingList(params int[] items) { _items = new List<int>(items); }
        public int this[int index] { get { Console.WriteLine("  indexer " + index); return _items[index]; } set => _items[index] = value; }
        public int Count { get { Console.WriteLine("  Count"); return _items.Count; } }
        public bool IsReadOnly => false;
        public void Add(int item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(int item) { Console.WriteLine("  Contains"); return _items.Contains(item); }
        public void CopyTo(int[] array, int arrayIndex) { Console.WriteLine("  CopyTo"); _items.CopyTo(array, arrayIndex); }
        public IEnumerator<int> GetEnumerator() { Console.WriteLine("  GetEnumerator"); return _items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int IndexOf(int item) => _items.IndexOf(item);
        public void Insert(int index, int item) => _items.Insert(index, item);
        public bool Remove(int item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);
    }

    static void Main()
    {
        var ramp = Enumerable.Range(-500, 1003).ToArray();
        T("int[] Sum", () => ramp.Sum());
        T("List<int> Sum", () => new List<int>(ramp).Sum());
        T("short int[] Sum", () => new[] { 1, 2, 3 }.Sum());
        T("int[17] Sum", () => Enumerable.Range(1, 17).ToArray().Sum());
        T("int[] Sum overflow (all lanes)", () => Enumerable.Repeat(int.MaxValue, 64).ToArray().Sum());
        T("int[] Sum overflow (short)", () => new[] { int.MaxValue, 1 }.Sum());
        T("int[] Sum negative overflow", () => Enumerable.Repeat(int.MinValue, 40).ToArray().Sum());
        T("int[] Sum no overflow at the limit", () => new[] { int.MaxValue, -1, 1, 0 }.Sum());
        T("long[] Sum", () => ramp.Select(i => (long)i * 1_000_000_007).ToArray().Sum());
        T("long[] Sum overflow", () => Enumerable.Repeat(long.MaxValue / 2, 64).ToArray().Sum());
        T("empty int[] Sum", () => new int[0].Sum());
        T("empty List<long> Sum", () => new List<long>().Sum());

        var thirds = Enumerable.Range(1, 1000).Select(i => 1.0 / i).ToArray();
        T("double[] Sum", () => thirds.Sum());
        T("List<double> Sum", () => new List<double>(thirds).Sum());
        T("enumerated double Sum", () => thirds.Where(_ => true).Sum());
        T("double[] Sum cancellation", () => new[] { 1e16, 1.0, -1e16, 1.0 }.Sum());
        T("float[] Sum", () => thirds.Select(d => (float)d).ToArray().Sum());
        T("float[] Sum beyond float", () => new[] { float.MaxValue, float.MaxValue, -float.MaxValue }.Sum());
        T("decimal[] Sum", () => new[] { 0.1m, 0.2m, 0.3m }.Sum());
        T("double[] Sum NaN", () => new[] { 1.0, double.NaN, 2.0 }.Sum());
        T("double[] Sum infinities", () => new[] { double.PositiveInfinity, double.NegativeInfinity }.Sum());

        T("int[] Average", () => ramp.Average());
        T("List<int> Average", () => new List<int>(ramp).Average());
        T("long[] Average", () => new[] { long.MaxValue / 3, long.MaxValue / 3, 7L }.Average());
        T("double[] Average", () => thirds.Average());
        T("float[] Average", () => new[] { 1f, 2f, 4f }.Average());
        T("empty int[] Average", () => new int[0].Average());
        T("empty List<double> Average", () => new List<double>().Average());
        T("empty float[] Average", () => new float[0].Average());

        T("int[] Min", () => ramp.Min());
        T("int[] Max", () => ramp.Max());
        T("List<long> Min", () => new List<long> { 5, long.MinValue, 7 }.Min());
        T("List<long> Max", () => new List<long> { 5, long.MaxValue, 7 }.Max());
        T("empty int[] Min", () => new int[0].Min());
        T("empty List<long> Max", () => new List<long>().Max());
        T("double[] Min NaN", () => new[] { 3.0, double.NaN, -1.0 }.Min());
        T("double[] Max NaN first", () => new[] { double.NaN, 3.0, -1.0 }.Max());
        T("double[] Max all NaN", () => new[] { double.NaN, double.NaN }.Max());
        T("double[] Max NaN later", () => new[] { 1.0, double.NaN, 3.0 }.Max());
        T("double[] Min -0.0 first", () => new[] { -0.0, 0.0 }.Min());
        T("double[] Min 0.0 first", () => new[] { 0.0, -0.0 }.Min());
        T("double[] Max 0.0 first", () => new[] { 0.0, -0.0 }.Max());
        T("double[] Max -0.0 first", () => new[] { -0.0, 0.0 }.Max());
        T("double[] Min infinities", () => new[] { double.PositiveInfinity, double.NegativeInfinity }.Min());
        T("float[] Min NaN", () => new[] { 1f, float.NaN }.Min());
        T("float[] Max", () => new[] { 1f, -2f, 3.5f }.Max());
        T("float[] Min -0", () => new[] { -0f, 0f }.Min());
        T("List<double> Max", () => new List<double>(thirds).Max());
        T("empty double[] Min", () => new double[0].Min());
        T("empty float[] Max", () => new float[0].Max());

        T("int[] Contains", () => ramp.Contains(502));
        T("int[] Contains missing", () => ramp.Contains(503));
        T("long[] Contains", () => new[] { 1L, 2L, long.MinValue }.Contains(long.MinValue));
        T("List<int> Contains", () => new List<int>(ramp).Contains(-500));
        T("byte[] Contains", () => new byte[] { 1, 2, 255 }.Contains((byte)255));
        T("double[] Contains NaN", () => new[] { 1.0, double.NaN }.Contains(double.NaN));
        T("double[] Contains -0.0", () => new[] { 0.0 }.Contains(-0.0));
        T("List<string> Contains null", () => new List<string> { "a", null }.Contains(null));
        T("string[] Contains", () => new[] { "a", "b" }.Contains("b"));

        T("int[] SequenceEqual", () => ramp.SequenceEqual((int[])ramp.Clone()));
        T("int[] SequenceEqual differs", () => ramp.SequenceEqual(ramp.Select(i => i == 0 ? 1 : i).ToArray()));
        T("int[] SequenceEqual List<int>", () => ramp.SequenceEqual(new List<int>(ramp)));
        T("int[] SequenceEqual length", () => new[] { 1, 2 }.SequenceEqual(new[] { 1, 2, 3 }));
        T("double[] SequenceEqual NaN", () => new[] { double.NaN, 0.0 }.SequenceEqual(new[] { double.NaN, -0.0 }));
        T("char[] SequenceEqual", () => "hello".ToCharArray().SequenceEqual("hello".ToCharArray()));
        T("int[] SequenceEqual comparer", () => new[] { 1, 2 }.SequenceEqual(new[] { -1, -2 }, new AbsComparer()));
        T("empty SequenceEqual", () => new int[0].SequenceEqual(new List<int>()));

        T("int[] ToArray", () => new[] { 3, 1, 2 }.ToArray());
        T("List<int> ToArray", () => new List<int> { 4, 5 }.ToArray());
        T("empty List ToArray is Array.Empty", () => ReferenceEquals(new List<int>().ToArray(), Array.Empty<int>()));
        T("empty HashSet ToArray is Array.Empty", () => ReferenceEquals(new HashSet<int>().ToArray(), Array.Empty<int>()));
        T("HashSet ToArray", () => new HashSet<int> { 9 }.ToArray());
        T("int[] Count", () => ramp.Count());

        Console.WriteLine("custom IList:");
        var logging = new LoggingList(1, 2, 3, 4);
        T("First", () => logging.First());
        T("Last", () => logging.Last());
        T("LastOrDefault", () => logging.LastOrDefault());
        T("Last(predicate)", () => logging.Last(x => { Console.WriteLine("  predicate " + x); return x < 3; }));
        T("ElementAtOrDefault", () => logging.ElementAtOrDefault(2));
        T("ElementAtOrDefault out of range", () => logging.ElementAtOrDefault(9));
        T("ToArray", () => logging.ToArray());
        T("Contains", () => logging.Contains(3));
        T("SequenceEqual other count", () => logging.SequenceEqual(new LoggingList(1, 2)));
        T("SequenceEqual same", () => logging.SequenceEqual(new LoggingList(1, 2, 3, 4)));
        T("SequenceEqual array", () => logging.SequenceEqual(new[] { 1, 2, 3, 4 }));
        T("empty First", () => new LoggingList().First());
        T("empty Last", () => new LoggingList().Last());
    }

    sealed class AbsComparer : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => Math.Abs(x) == Math.Abs(y);
        public int GetHashCode(int x) => Math.Abs(x);
    }
}
