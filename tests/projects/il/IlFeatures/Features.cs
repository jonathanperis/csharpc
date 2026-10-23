using System.Numerics;
using System.Text;

namespace IlFeatures;

public record Person(string Name, int Age)
{
    public string Greeting => $"Hi, {Name}";
}

public record Employee(string Name, int Age, string Role) : Person(Name, Age);

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money operator +(Money a, Money b) =>
        a.Currency == b.Currency ? new(a.Amount + b.Amount, a.Currency) : throw new InvalidOperationException("currency mismatch");
}

public struct Counter
{
    private int _value;
    public Counter(int start) => _value = start;
    public int Next() => ++_value;
    public readonly int Peek => _value;
}

public interface IGreeter
{
    string Name { get; }
    string Greet() => $"Hello from {Name}";   // default interface method
}

public sealed class Robot(string name) : IGreeter
{
    public string Name => name;
}

public interface IHasZero<TSelf> where TSelf : IHasZero<TSelf>
{
    static abstract TSelf Zero { get; }
    static abstract TSelf operator +(TSelf a, TSelf b);
}

public readonly struct Meters(double value) : IHasZero<Meters>
{
    public double Value { get; } = value;
    public static Meters Zero => new(0);
    public static Meters operator +(Meters a, Meters b) => new(a.Value + b.Value);
    public override string ToString() => $"{Value}m";
}

public sealed class Thermostat : IDisposable
{
    private readonly List<string> _log;
    public event EventHandler<int>? Changed;
    public Thermostat(List<string> log) { _log = log; _log.Add("open"); }
    private int _temperature;
    public int Temperature
    {
        get => _temperature;
        set
        {
            if (value == _temperature) return;
            _temperature = value;
            Changed?.Invoke(this, value);
        }
    }
    public void Dispose() => _log.Add("disposed");
}

public class Node<T>
{
    public T Value { get; }
    public Node<T>? Next { get; set; }
    public Node(T value) => Value = value;
}

public class Stack<T> : IEnumerable<T>
{
    private Node<T>? _top;
    public int Count { get; private set; }
    public void Push(T value) { _top = new Node<T>(value) { Next = _top }; Count++; }
    public T Pop()
    {
        var top = _top ?? throw new InvalidOperationException("empty stack");
        _top = top.Next;
        Count--;
        return top.Value;
    }
    public IEnumerator<T> GetEnumerator()
    {
        for (var n = _top; n is not null; n = n.Next) yield return n.Value;
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public abstract class Visitor
{
    public virtual string Visit<T>(T value) => $"value {value}";
}

public sealed class LoudVisitor : Visitor
{
    public override string Visit<T>(T value) => base.Visit(value).ToUpperInvariant() + "!";
}

public static class Features
{
    private static readonly Dictionary<string, int> s_cache = new();
    private static string? s_lazy;

    public static string Lazy => s_lazy ??= "computed once";

    public static T Sum<T>(IEnumerable<T> values) where T : IHasZero<T>
    {
        var total = T.Zero;
        foreach (var v in values) total += v;
        return total;
    }

    public static TNum Square<TNum>(TNum x) where TNum : INumber<TNum> => x * x;

    public static int Memo(string key)
    {
        if (s_cache.TryGetValue(key, out var hit)) return hit;
        var value = key.Length * 7;
        s_cache[key] = value;
        return value;
    }

    public static string Describe(int? maybe, (int X, string Y) pair) =>
        $"{(maybe.HasValue ? maybe.Value.ToString() : "none")} {maybe ?? -1} {pair.X}:{pair.Y} {pair}";

    public static string Join(string separator, params int[] values) => string.Join(separator, values);

    public static string Optional(int a, int b = 10, string label = "sum") => $"{label}={a + b}";

    public static int Primes(int limit)
    {
        Span<bool> composite = stackalloc bool[limit + 1];
        var count = 0;
        for (var i = 2; i <= limit; i++)
        {
            if (composite[i]) continue;
            count++;
            for (var j = i * i; j <= limit; j += i) composite[j] = true;
        }
        return count;
    }

    public static ReadOnlySpan<byte> Magic => [0x43, 0x53, 0x48, 0x41, 0x52, 0x50];

    public static int MagicSum()
    {
        var sum = 0;
        foreach (var b in Magic) sum += b;
        return sum;
    }

    public static unsafe long PointerSum(int[] values)
    {
        long sum = 0;
        fixed (int* p = values)
            for (var i = 0; i < values.Length; i++) sum += *(p + i);
        return sum;
    }

    public static string Flow(int n)
    {
        var sb = new StringBuilder();
        try
        {
            try
            {
                if (n < 0) throw new ArgumentException("negative", nameof(n));
                sb.Append("try;");
                if (n == 0) return sb.Append("early;").ToString();
            }
            finally
            {
                sb.Append("inner-finally;");
            }
            sb.Append("after;");
        }
        catch (ArgumentException e) when (e.ParamName == "n")
        {
            sb.Append("caught ").Append(e.Message).Append(';');
        }
        finally
        {
            sb.Append("outer-finally;");
        }
        return sb.ToString();
    }

    public static string Rethrow(string text)
    {
        try
        {
            try { return int.Parse(text).ToString(); }
            catch (FormatException) { throw; }
        }
        catch (FormatException e) { return "rethrown: " + e.GetType().Name; }
    }

    public static string FilterThrows(string s)
    {
        try { throw new InvalidOperationException(s); }
        // For "abc" the filter itself throws (division by zero): that counts as false, not as an escape.
        catch (InvalidOperationException e) when (e.Message.Length / (s.Length - 3) > 0) { return "filtered"; }
        catch (InvalidOperationException) { return "fallback"; }
    }

    public static string Grade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 and < 90 => "B",
        >= 70 => "C",
        _ => "F",
    };

    public static decimal Interest(decimal principal, decimal rate, int years)
    {
        var amount = principal;
        for (var i = 0; i < years; i++) amount += decimal.Round(amount * rate, 2);
        return amount;
    }

    public static string Chars(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsDigit(c)) sb.Append('#');
            else if (char.IsUpper(c)) sb.Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Words(string text) =>
        string.Join(" ", text.Split(' ').Where(w => w.Length > 0).GroupBy(w => w.Length).OrderBy(g => g.Key)
            .Select(g => $"{g.Key}:{string.Join("/", g)}"));

    public static string Query(int[] numbers) =>
        string.Join(",", from n in numbers where n % 2 == 1 let sq = n * n orderby sq descending select sq);

    public static string Locked(object gate, Func<string> action)
    {
        lock (gate) return action();
    }

    public static string Dates()
    {
        var start = new DateTime(2024, 2, 28);
        var span = TimeSpan.FromHours(36);
        return $"{start.AddDays(2):yyyy-MM-dd} {(start + span).DayOfWeek} {span.TotalMinutes}";
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "strlen")]
    private static extern nuint CStrLen(string s);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "abs")]
    private static extern int CAbs(int x);

    public static string Native(string text) => $"{(int)CStrLen(text)} {CAbs(-42)}";

    public static string Bits(uint x) => $"{BitOperations.PopCount(x)} {x >> 3} {x << 4} {unchecked((int)x * -3)} {(byte)x} {(long)x * x}";
}
