using System.Linq.Expressions;

namespace IlExpressionTreesLib;

public enum Size { Small, Large = 10 }

public class Box
{
    public int Value;
    public string Name { get; set; }
    public Size Size { get; set; }
    public List<string> Tags { get; } = new();
    public Box() { }
    public Box(string name, int value) { Name = name; Value = value; }
    public int Scaled(int factor) => Value * factor;
    public static Box Make(int value) => new Box("made", value);
    public override string ToString() => $"Box({Name},{Value},{Size},[{string.Join(",", Tags)}])";
}

public static class Trees
{
    public static Expression<Func<int, int>> AddCaptured(int y) => x => x + y;

    public static Expression<Func<Box, bool>> Filter(string name, int min) => b => b.Name == name && b.Value > min;

    public static Expression<Func<Box, int>> Arithmetic() => b => checked(b.Value * 2) + b.Scaled(3) - (int)b.Size;

    public static Expression<Func<int, Box>> Create() => v => new Box("new", v) { Size = Size.Large, Tags = { "a", "b" } };

    public static Expression<Func<IEnumerable<Box>, string>> Query() =>
        boxes => string.Join("|", boxes.Where(b => b.Value % 2 == 0).OrderBy(b => b.Name).Select(b => b.Name + "=" + b.Value));

    public static Expression<Func<int?, string, string>> Nullable() => (n, s) => (n ?? -1) + ":" + (s ?? "null");

    public static Expression<Func<object, string>> TypeTests() => o => o is Box ? ((Box)o).Name : o as string ?? "other";

    public static Expression<Func<int, Expression<Func<int, int>>>> Quoted() => x => y => x * y;

    public static Expression<Func<Func<int, int>, int[]>> Arrays() => f => new[] { f(1), f(2), Box.Make(3).Value };

    public static List<LambdaExpression> InLoop()
    {
        var trees = new List<LambdaExpression>();
        int outer = 100;
        for (int i = 0; i < 2; i++)
        {
            int inner = i * 10;
            Expression<Func<int>> tree = () => outer + inner + i;
            trees.Add(tree);
        }
        outer = 200;
        return trees;
    }
}
