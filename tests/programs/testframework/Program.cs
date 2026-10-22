// A miniature unit-test framework: test registry with delegates, fluent assertions, expected exceptions,
// setup/teardown, parameterized cases, timing-free deterministic reporting, and a summary with failures.
var runner = new TestRunner();

runner.Suite("Math", s =>
{
    s.Test("addition", () => Assert.That(2 + 2).IsEqualTo(4));
    s.Test("integer division truncates", () => Assert.That(-7 / 2).IsEqualTo(-3));
    s.Theory("squares", [(2, 4), (3, 9), (-4, 16), (5, 26)], c => Assert.That(c.Item1 * c.Item1).IsEqualTo(c.Item2));
    s.Test("division by zero throws", () => Assert.Throws<DivideByZeroException>(() => { int zero = 0; _ = 1 / zero; }));
    s.Test("floating point", () => Assert.That(0.1 + 0.2).IsCloseTo(0.3, 1e-9));
    s.Test("deliberately failing", () => Assert.That(Math.Max(3, 7)).IsEqualTo(3));
});

runner.Suite("Strings", s =>
{
    string? subject = null;
    s.Setup(() => subject = "  Hello, World  ");
    s.Teardown(() => subject = null);
    s.Test("trim", () => Assert.That(subject!.Trim()).IsEqualTo("Hello, World"));
    s.Test("contains", () => Assert.That(subject!.Contains("World")).IsTrue());
    s.Test("split", () => Assert.That(subject!.Trim().Split(", ")).HasCount(2));
    s.Test("null after teardown is reset", () => Assert.That(subject).IsNotNull());
    s.Test("expected exception not thrown", () => Assert.Throws<FormatException>(() => int.Parse("123")));
    s.Test("wrong exception type", () => Assert.Throws<ArgumentException>(() => throw new InvalidOperationException("boom")));
});

runner.Suite("Collections", s =>
{
    s.Test("list order", () => Assert.That(new List<int> { 3, 1, 2 }.Order().ToList()).SequenceEquals([1, 2, 3]));
    s.Test("dictionary lookup", () =>
    {
        var d = new Dictionary<string, int> { ["a"] = 1 };
        Assert.Throws<KeyNotFoundException>(() => _ = d["b"]);
    });
    s.Test("unexpected exception", () => { object o = "text"; _ = (int)o; });
    s.Theory("palindromes", ["level", "noon", "csharp"], w => Assert.That(w.SequenceEqual(w.Reverse())).IsTrue());
});

var summary = runner.Run();
Console.WriteLine();
Console.WriteLine($"{summary.Passed} passed, {summary.Failed} failed, {summary.Total} total");
foreach (var f in summary.Failures) Console.WriteLine($"  FAIL {f.Name}: {f.Message}");
return summary.Failed == 0 ? 0 : 1;

record TestResult(string Name, bool Passed, string? Message);
record Summary(int Passed, int Failed, int Total, List<TestResult> Failures);

class AssertionException(string message) : Exception(message);

static class Assert
{
    public static Subject<T> That<T>(T actual) => new(actual);

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        catch (Exception e) { throw new AssertionException($"expected {typeof(TException).Name} but got {e.GetType().Name}: {e.Message}"); }
        throw new AssertionException($"expected {typeof(TException).Name} but nothing was thrown");
    }
}

readonly struct Subject<T>(T actual)
{
    public T Value => actual;
    public void IsEqualTo(T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new AssertionException($"expected {expected} but was {actual}");
    }
    public void IsTrue() { if (actual is not true) throw new AssertionException($"expected true but was {actual}"); }
    public void IsNotNull() { if (actual is null) throw new AssertionException("expected a value but was null"); }
    public void HasCount(int n)
    {
        var count = actual is System.Collections.ICollection c ? c.Count : -1;
        if (count != n) throw new AssertionException($"expected {n} items but found {count}");
    }
    public void SequenceEquals(IEnumerable<int> expected)
    {
        if (actual is not IEnumerable<int> seq || !seq.SequenceEqual(expected))
            throw new AssertionException($"sequences differ");
    }
}

static class DoubleAssertions
{
    public static void IsCloseTo(this Subject<double> subject, double expected, double tolerance)
    {
        if (Math.Abs(subject.Value - expected) > tolerance)
            throw new AssertionException($"expected {expected}±{tolerance} but was {subject.Value}");
    }
}

class Suite(string name)
{
    public string Name { get; } = name;
    public List<(string Name, Action Body)> Tests { get; } = [];
    public Action? SetupAction { get; private set; }
    public Action? TeardownAction { get; private set; }

    public void Setup(Action a) => SetupAction = a;
    public void Teardown(Action a) => TeardownAction = a;
    public void Test(string testName, Action body) => Tests.Add((testName, body));
    public void Theory<T>(string testName, IEnumerable<T> cases, Action<T> body)
    {
        int i = 0;
        foreach (var c in cases) { var captured = c; Tests.Add(($"{testName}[{i++}] {captured}", () => body(captured))); }
    }
}

class TestRunner
{
    private readonly List<Suite> _suites = [];

    public void Suite(string name, Action<Suite> define)
    {
        var suite = new Suite(name);
        define(suite);
        _suites.Add(suite);
    }

    public Summary Run()
    {
        var results = new List<TestResult>();
        foreach (var suite in _suites)
        {
            Console.WriteLine($"{suite.Name} ({suite.Tests.Count} tests)");
            foreach (var (name, body) in suite.Tests)
            {
                var full = $"{suite.Name}.{name}";
                TestResult result;
                try
                {
                    suite.SetupAction?.Invoke();
                    body();
                    result = new TestResult(full, true, null);
                }
                catch (AssertionException e) { result = new TestResult(full, false, e.Message); }
                catch (Exception e) { result = new TestResult(full, false, $"unexpected {e.GetType().Name}: {e.Message}"); }
                finally { suite.TeardownAction?.Invoke(); }
                results.Add(result);
                Console.WriteLine($"  {(result.Passed ? "ok  " : "FAIL")} {name}");
            }
        }
        var failures = results.Where(r => !r.Passed).ToList();
        return new Summary(results.Count - failures.Count, failures.Count, results.Count, failures);
    }
}
