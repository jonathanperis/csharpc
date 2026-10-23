using System.Text;

namespace IlShapes;

public enum Color { Red, Green, Blue }

public interface IShape
{
    string Name { get; }
    double Area();
    Color Color { get; set; }
}

/// <summary>Fields, auto-properties, virtual/abstract members, a static counter with a static constructor.</summary>
public abstract class Shape : IShape, IComparable<Shape>
{
    private static int s_created;
    private static readonly string[] s_prefixes;

    static Shape()
    {
        s_prefixes = ["tiny", "small", "large"];
    }

    protected Shape(string name)
    {
        Name = name;
        Id = ++s_created;
    }

    public string Name { get; }
    public int Id { get; }
    public Color Color { get; set; } = Color.Green;
    public static int Created => s_created;

    public abstract double Area();

    public virtual string Describe() => $"{Size()} {Color.ToString().ToLowerInvariant()} {Name} #{Id}, area {Area():F2}";

    protected string Size() => Area() switch
    {
        < 1 => s_prefixes[0],
        < 20 => s_prefixes[1],
        _ => s_prefixes[2],
    };

    public int CompareTo(Shape? other) => other is null ? 1 : Area().CompareTo(other.Area());

    public override string ToString() => Describe();
}

public sealed class Circle(double radius) : Shape("circle")
{
    public double Radius { get; } = radius;
    public override double Area() => Math.PI * Radius * Radius;
}

public class Rect : Shape
{
    public Rect(double width, double height) : this("rect", width, height) { }

    protected Rect(string name, double width, double height) : base(name)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "width must be positive");
        Width = width;
        Height = height;
    }

    public double Width { get; }
    public double Height { get; }
    public override double Area() => Width * Height;
    public override string Describe() => base.Describe() + $" ({Width}x{Height})";
}

public sealed class Square(double side) : Rect("square", side, side);

/// <summary>A struct with an interface, operators and value equality.</summary>
public readonly struct Point(int x, int y) : IEquatable<Point>
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    public int ManhattanLength => Math.Abs(X) + Math.Abs(Y);
    public bool Equals(Point other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Point p && Equals(p);
    public override int GetHashCode() => X * 31 + Y;
    public override string ToString() => $"({X}, {Y})";
}

public class ShapeException(string message, string shape) : Exception(message)
{
    public string Shape { get; } = shape;
}

/// <summary>A generic container over an interface constraint, using List/Dictionary/LINQ/lambdas.</summary>
public class Registry<T> where T : IShape
{
    private readonly List<T> _items = [];
    private readonly Dictionary<string, int> _counts = new();

    public int Count => _items.Count;
    public T this[int index] => _items[index];

    public void Add(T item)
    {
        _items.Add(item);
        _counts[item.Name] = _counts.TryGetValue(item.Name, out var n) ? n + 1 : 1;
    }

    public double TotalArea()
    {
        double total = 0;
        foreach (var item in _items) total += item.Area();
        return total;
    }

    public T Largest()
    {
        if (_items.Count == 0) throw new InvalidOperationException("the registry is empty");
        var best = _items[0];
        for (var i = 1; i < _items.Count; i++)
            if (_items[i].Area() > best.Area()) best = _items[i];
        return best;
    }

    public IEnumerable<string> NamesByArea(double minimum) =>
        _items.Where(s => s.Area() >= minimum).OrderBy(s => s.Area()).Select(s => s.Name.ToUpperInvariant());

    public string Summary()
    {
        var sb = new StringBuilder();
        foreach (var name in _counts.Keys.Order())
            sb.Append(name).Append('=').Append(_counts[name]).Append(';');
        return sb.ToString();
    }

    public int CountWhere(Func<T, bool> predicate)
    {
        var n = 0;
        foreach (var item in _items)
            if (predicate(item)) n++;
        return n;
    }

    public void Paint(Color color, double threshold)
    {
        // A closure over parameters and a captured counter.
        var painted = 0;
        _items.ForEach(s =>
        {
            if (s.Area() > threshold) { s.Color = color; painted++; }
        });
        LastPainted = painted;
    }

    public int LastPainted { get; private set; }
}

public static class Geometry
{
    private static readonly int[] s_primes = [2, 3, 5, 7, 11, 13];

    public static T Max<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b) >= 0 ? a : b;

    public static int SumPrimes(int count)
    {
        var sum = 0;
        for (var i = 0; i < count && i < s_primes.Length; i++) sum += s_primes[i];
        return sum;
    }

    public static Shape Parse(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            return parts[0] switch
            {
                "circle" => new Circle(double.Parse(parts[1])),
                "rect" => new Rect(double.Parse(parts[1]), double.Parse(parts[2])),
                "square" => new Square(double.Parse(parts[1])),
                _ => throw new ShapeException($"unknown shape '{parts[0]}'", parts[0]),
            };
        }
        catch (FormatException e)
        {
            throw new ShapeException("bad number in '" + text + "': " + e.Message, parts[0]);
        }
        catch (IndexOutOfRangeException)
        {
            throw new ShapeException($"missing size in '{text}'", parts.Length > 0 ? parts[0] : "?");
        }
    }

    public static string TryAll(string[] inputs, List<string> log)
    {
        var ok = 0;
        foreach (var input in inputs)
        {
            try
            {
                var shape = Parse(input);
                log.Add("ok: " + shape.Describe());
                ok++;
            }
            catch (ShapeException e) when (e.Shape != "?")
            {
                log.Add($"error ({e.Shape}): {e.Message}");
            }
            catch (Exception e)
            {
                log.Add($"{e.GetType().Name}: {e.Message}");
            }
            finally
            {
                log.Add("done " + input.Length);
            }
        }
        return $"{ok}/{inputs.Length}";
    }

    public static long Checked(int a, int b)
    {
        try
        {
            return checked(a * b);
        }
        catch (OverflowException)
        {
            return -1;
        }
    }

    public static string Classify(object? value) => value switch
    {
        null => "null",
        int i when i < 0 => "negative int",
        int i => $"int {i}",
        string { Length: 0 } => "empty string",
        string s => $"string of {s.Length}",
        Point p => $"point {p}",
        IShape shape => "shape " + shape.Name,
        _ => value.GetType().Name,
    };

    public static int Weekday(string day) => day switch
    {
        "mon" => 1, "tue" => 2, "wed" => 3, "thu" => 4, "fri" => 5, "sat" => 6, "sun" => 7,
        _ => 0,
    };

    public static IEnumerable<int> Fibonacci(int count)
    {
        int a = 0, b = 1;
        for (var i = 0; i < count; i++)
        {
            yield return a;
            (a, b) = (b, a + b);
        }
    }
}
