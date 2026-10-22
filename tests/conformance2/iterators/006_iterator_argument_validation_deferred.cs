static IEnumerable<int> Gen(int n) { if (n < 0) throw new ArgumentOutOfRangeException(nameof(n)); yield return n; }
var e = Gen(-1);
Console.WriteLine("not thrown yet");
try { foreach (var x in e) { } } catch (ArgumentOutOfRangeException ex) { Console.WriteLine("thrown on iteration: " + ex.ParamName); }
