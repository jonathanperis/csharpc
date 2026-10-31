// Trees built with the System.Linq.Expressions API (blocks, variables, loops, labels, switch, try/catch,
// throw, member access and calls by name, new, conditionals), compiled through the interpreter, printed,
// and rewritten by an ExpressionVisitor subclass.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

class Item
{
    public int Count;
    public string Name { get; set; }
    public Item() { }
    public Item(string name, int count) { Name = name; Count = count; }
    public string Describe(string prefix) => prefix + Name + ":" + Count;
    public static int Square(int x) => x * x;
}

// Rewrites a + b into a - b and multiplies constants by 10.
class Rewriter : ExpressionVisitor
{
    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.Add)
            return Expression.Subtract(Visit(node.Left), Visit(node.Right));
        return base.VisitBinary(node);
    }

    protected override Expression VisitConstant(ConstantExpression node) =>
        node.Value is int i ? Expression.Constant(i * 10) : base.VisitConstant(node);
}

// Counts the nodes of each kind.
class Counter : ExpressionVisitor
{
    public readonly SortedDictionary<string, int> Kinds = new();
    public override Expression Visit(Expression node)
    {
        if (node != null) Kinds[node.NodeType.ToString()] = Kinds.TryGetValue(node.NodeType.ToString(), out var n) ? n + 1 : 1;
        return base.Visit(node);
    }
}

class Program
{
    static void Main()
    {
        // Factorial with a loop, a variable and a break label.
        var n = Expression.Parameter(typeof(int), "n");
        var result = Expression.Variable(typeof(int), "result");
        var done = Expression.Label(typeof(int), "done");
        var factorial = Expression.Lambda<Func<int, int>>(
            Expression.Block(new[] { result },
                Expression.Assign(result, Expression.Constant(1)),
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.GreaterThan(n, Expression.Constant(1)),
                        Expression.MultiplyAssign(result, Expression.PostDecrementAssign(n)),
                        Expression.Break(done, result)),
                    done)),
            n);
        Console.WriteLine(factorial);
        var fact = factorial.Compile();
        Console.WriteLine($"{fact(1)} {fact(5)} {fact(10)}");

        // Switch, with a default.
        var x = Expression.Parameter(typeof(int), "x");
        var sw = Expression.Lambda<Func<int, string>>(
            Expression.Switch(x, Expression.Constant("many"),
                Expression.SwitchCase(Expression.Constant("zero"), Expression.Constant(0)),
                Expression.SwitchCase(Expression.Constant("one"), Expression.Constant(1)),
                Expression.SwitchCase(Expression.Constant("few"), Expression.Constant(2), Expression.Constant(3))),
            x);
        var name = sw.Compile();
        Console.WriteLine(string.Join(",", Enumerable.Range(0, 5).Select(name)));

        // try/catch/finally and throw.
        var s = Expression.Parameter(typeof(string), "s");
        var e = Expression.Variable(typeof(FormatException), "e");
        var log = typeof(Console).GetMethod("WriteLine", new[] { typeof(string) });
        var parse = Expression.Lambda<Func<string, int>>(
            Expression.TryCatchFinally(
                Expression.Call(typeof(int).GetMethod("Parse", new[] { typeof(string) }), s),
                Expression.Call(log, Expression.Constant("finally")),
                Expression.Catch(e, Expression.Block(
                    Expression.Call(log, Expression.Property(e, typeof(Exception).GetProperty("Message"))),
                    Expression.Constant(-1)))),
            s);
        var tryParse = parse.Compile();
        Console.WriteLine(tryParse("42"));
        Console.WriteLine(tryParse("x"));
        var thrower = Expression.Lambda<Action>(Expression.Throw(Expression.New(typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) }), Expression.Constant("boom")))).Compile();
        try { thrower(); } catch (InvalidOperationException ex) { Console.WriteLine("caught " + ex.Message); }

        // Members and calls by name, object creation, conditionals, coalesce, type tests.
        var item = Expression.Parameter(typeof(Item), "item");
        var describe = Expression.Lambda<Func<Item, string>>(
            Expression.Condition(
                Expression.GreaterThan(Expression.Field(item, typeof(Item).GetField("Count")), Expression.Constant(0)),
                Expression.Call(item, typeof(Item).GetMethod("Describe"), Expression.Constant("> ")),
                Expression.Coalesce(Expression.Property(item, typeof(Item).GetProperty("Name")), Expression.Constant("(none)"))),
            item);
        Console.WriteLine(describe);
        var d = describe.Compile();
        Console.WriteLine(d(new Item("apples", 3)) + " | " + d(new Item(null, 0)) + " | " + d(new Item("pears", 0)));
        var create = Expression.Lambda<Func<Item>>(
            Expression.MemberInit(Expression.New(typeof(Item)),
                Expression.Bind(typeof(Item).GetField("Count"), Expression.Constant(7)),
                Expression.Bind(typeof(Item).GetProperty("Name"), Expression.Constant("plums")))).Compile();
        Console.WriteLine(create().Describe(""));
        var square = Expression.Lambda<Func<int, int>>(Expression.Call(typeof(Item).GetMethod("Square"), x), x).Compile();
        Console.WriteLine(square(9));
        var o = Expression.Parameter(typeof(object), "o");
        var isString = Expression.Lambda<Func<object, bool>>(Expression.TypeIs(o, typeof(string)), o).Compile();
        Console.WriteLine($"{isString("a")} {isString(1)}");

        // Lists and arrays.
        var add = typeof(List<int>).GetMethod("Add");
        var list = Expression.Lambda<Func<List<int>>>(Expression.ListInit(Expression.New(typeof(List<int>)),
            Expression.ElementInit(add, Expression.Constant(3)), Expression.ElementInit(add, Expression.Constant(1)),
            Expression.ElementInit(add, Expression.Constant(2)))).Compile()();
        Console.WriteLine(string.Join(",", list));
        var arr = Expression.Lambda<Func<string[]>>(Expression.NewArrayInit(typeof(string), Expression.Constant("a"), Expression.Constant("b"))).Compile()();
        Console.WriteLine(string.Join("+", arr));
        var len = Expression.Lambda<Func<string[], int>>(Expression.ArrayLength(Expression.Parameter(typeof(string[]), "a")), Expression.Parameter(typeof(string[]), "a"));
        Console.WriteLine(len);

        // Lambda without a delegate type, Invoke, DynamicInvoke, nested lambdas.
        var a = Expression.Parameter(typeof(int), "a");
        var b = Expression.Parameter(typeof(int), "b");
        var untyped = Expression.Lambda(Expression.Add(a, b), a, b);
        Console.WriteLine(untyped + " : " + untyped.Type);
        Console.WriteLine(untyped.Compile().DynamicInvoke(2, 3));
        var adder = Expression.Lambda<Func<int, int, int>>(Expression.Add(a, b), a, b);
        var invoke = Expression.Lambda<Func<int>>(Expression.Invoke(adder, Expression.Constant(4), Expression.Constant(5)));
        Console.WriteLine(invoke + " = " + invoke.Compile()());

        // Visitors.
        Expression<Func<int, int>> tree = v => v + 2 + 3;
        var rewritten = (Expression<Func<int, int>>)new Rewriter().Visit(tree);
        Console.WriteLine(rewritten + " -> " + rewritten.Compile()(100));
        var counter = new Counter();
        counter.Visit(factorial);
        Console.WriteLine(string.Join(" ", counter.Kinds.Select(kv => kv.Key + "=" + kv.Value)));

        // Errors from the factories surface as .NET's.
        try { Expression.Add(Expression.Constant(1), Expression.Constant("x")); }
        catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        try { Expression.Lambda<Func<int>>(Expression.Constant("x")); }
        catch (ArgumentException ex) { Console.WriteLine(ex.Message); }
    }
}
