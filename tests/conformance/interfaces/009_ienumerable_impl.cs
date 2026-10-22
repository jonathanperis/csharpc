foreach (var x in new Countdown(3)) Console.WriteLine(x);
class Countdown(int from) : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator() { for (int i = from; i > 0; i--) yield return i; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
