// Member access through null must throw NullReferenceException (LLVM treats null derefs as UB,
// so csharpc checks explicitly).
int[]? arr = null;
string? str = null;
Node? node = null;
Func<int>? fn = null;
IShape? shape = null;
object? boxed = null;
List<int>? list = null;

Try("arr.Length", () => arr!.Length);
Try("arr[0]", () => arr![0]);
Try("str.Length", () => str!.Length);
Try("str[0]", () => str![0]);
Try("node.Value", () => node!.Value);
Try("node.Value = 1", () => { node!.Value = 1; return 0; });
Try("node.Next.Value", () => new Node().Next!.Value);
Try("node.Method()", () => node!.Twice());
Try("node.Virtual()", () => node!.Virtual());
Try("fn()", () => fn!());
Try("shape.Area()", () => shape!.Area());
Try("(int)boxed", () => (int)boxed!);
Try("list.Count", () => list!.Count);
Try("foreach over null", () => { foreach (var x in arr!) { } return 0; });
Try("lock(null)", () => { lock (boxed!) { } return 0; });
Try("null?.Length", () => str?.Length ?? -1);
Try("throw null", () => throw null!);
Try("str.ToUpper()", () => str!.ToUpper().Length);

static void Try(string what, Func<int> f)
{
    try { Console.WriteLine($"{what} = {f()}"); }
    catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message}"); }
}

class Node
{
    public int Value;
    public Node? Next;
    public int Twice() => Value * 2;
    public virtual int Virtual() => Value + 1;
}

interface IShape { int Area(); }
