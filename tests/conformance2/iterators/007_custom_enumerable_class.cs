var r = new Range3(2, 5);
foreach (var x in r) Console.Write(x + " ");
Console.WriteLine(r.Sum());
class Range3(int from, int to) : IEnumerable<int> {
    public IEnumerator<int> GetEnumerator() { for (int i = from; i < to; i++) yield return i; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
