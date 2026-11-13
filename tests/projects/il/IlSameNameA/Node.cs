namespace Shared;

// The same full name as the other library's Shared.Node: two different types.
public class Node
{
    public override string ToString() => "A node";
}

public static class FactoryA
{
    public static object Array() => new Node[2];
    public static object Node() => new Node();
    public static Type ArrayType() => typeof(Node[]);
    public static bool IsArray(object o) => o is Node[];
    public static bool IsNode(object o) => o is Node;
    public static object Task() => System.Threading.Tasks.Task.FromResult(new Node());
    public static bool IsTask(object o) => o is Task<Node>;
    public static object List() => new List<Node> { new Node() };
}
