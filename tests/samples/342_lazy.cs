// System.Lazy<T> (corelib shadow): one factory run, IsValueCreated, ToString, cached factory exceptions,
// re-entrancy, PublicationOnly, pre-built values.
var calls = 0;
var lazy = new Lazy<string>(() => { calls++; return "built"; });
Console.WriteLine($"{lazy.IsValueCreated} {lazy}");
Console.WriteLine($"{lazy.Value} {lazy.Value} calls={calls} {lazy.IsValueCreated} {lazy}");

var attempts = 0;
var failing = new Lazy<int>(() => { attempts++; throw new InvalidOperationException("boom " + attempts); });
for (var i = 0; i < 2; i++)
{
    try { Console.WriteLine(failing.Value); }
    catch (InvalidOperationException e) { Console.WriteLine($"{e.Message} attempts={attempts} {failing.IsValueCreated}"); }
}

Lazy<int>? self = null;
self = new Lazy<int>(() => self!.Value + 1);
try { Console.WriteLine(self.Value); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

var publication = new Lazy<List<int>>(() => [1, 2, 3], System.Threading.LazyThreadSafetyMode.PublicationOnly);
Console.WriteLine(publication.Value.Count + publication.Value[2]);
var unsafeLazy = new Lazy<double>(() => 2.5, isThreadSafe: false);
Console.WriteLine(unsafeLazy.Value * 2);
var shared = new Lazy<int>(() => 42);
Console.WriteLine(Enumerable.Range(0, 3).Sum(_ => shared.Value));

try { _ = new Lazy<int>((Func<int>)null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
