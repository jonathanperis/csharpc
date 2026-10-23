using IlFeatures;

var alice = new Person("Alice", 31);
var bob = new Employee("Bob", 45, "engineer");
Console.WriteLine(alice);
Console.WriteLine(bob);
Console.WriteLine($"{alice.Greeting}; equal copy: {alice == new Person("Alice", 31)}; with: {alice with { Age = 32 }}");
Console.WriteLine($"hash stable: {alice.GetHashCode() == new Person("Alice", 31).GetHashCode()}, {bob.Equals(alice)}");

var total = new Money(10.50m, "EUR") + new Money(2.25m, "EUR");
Console.WriteLine(total);
try { _ = total + new Money(1, "USD"); }
catch (InvalidOperationException e) { Console.WriteLine("money: " + e.Message); }

var counter = new Counter(5);
counter.Next();
Console.WriteLine($"counter {counter.Next()} peek {counter.Peek}");

IGreeter robot = new Robot("R2");
Console.WriteLine(robot.Greet());

Console.WriteLine(Features.Sum(new[] { new Meters(1.5), new Meters(2), new Meters(0.25) }));
Console.WriteLine($"{Features.Square(7)} {Features.Square(1.5)} {Features.Square(3L)}");

var log = new List<string>();
using (var t = new Thermostat(log))
{
    t.Changed += (_, v) => log.Add($"changed to {v}");
    t.Temperature = 20;
    t.Temperature = 20;
    t.Temperature = 22;
}
Console.WriteLine(string.Join(" | ", log));

var stack = new IlFeatures.Stack<string>();
foreach (var w in new[] { "a", "b", "c" }) stack.Push(w);
Console.WriteLine($"{string.Join("", stack)} pop {stack.Pop()} count {stack.Count}");
try { new IlFeatures.Stack<int>().Pop(); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

Visitor visitor = new LoudVisitor();
Console.WriteLine(visitor.Visit(42) + " " + visitor.Visit("x"));

Console.WriteLine(Features.Lazy + " / " + Features.Lazy);
Console.WriteLine($"{Features.Memo("abc")} {Features.Memo("abc")} {Features.Memo("hello")}");
Console.WriteLine(Features.Describe(5, (1, "one")) + " | " + Features.Describe(null, (2, "two")));
Console.WriteLine(Features.Join("-", 1, 2, 3) + " " + Features.Join("+") + " " + Features.Optional(1) + " " + Features.Optional(1, 2, "total"));
Console.WriteLine($"primes {Features.Primes(100)} magic {Features.MagicSum()} ptr {Features.PointerSum([1, 2, 3, 4])}");
foreach (var n in new[] { 5, 0, -1 }) Console.WriteLine(Features.Flow(n));
Console.WriteLine(Features.Rethrow("12") + " " + Features.Rethrow("x"));
Console.WriteLine(Features.FilterThrows("abcd") + " " + Features.FilterThrows("abc"));
Console.WriteLine(string.Join(" ", new[] { 95, 85, 72, 10 }.Select(Features.Grade)));
Console.WriteLine(Features.Interest(1000m, 0.05m, 3));
Console.WriteLine(Features.Chars("Hello World 2024"));
Console.WriteLine(Features.Words("the quick brown fox jumps over the lazy dog"));
Console.WriteLine(Features.Query([1, 2, 3, 4, 5, 6, 7]));
Console.WriteLine(Features.Locked(new object(), () => "locked"));
Console.WriteLine(Features.Dates());
Console.WriteLine(Features.Bits(0xF0F0u));
Console.WriteLine(Features.Native("hello, world"));
