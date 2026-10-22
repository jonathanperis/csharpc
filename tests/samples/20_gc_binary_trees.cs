// Benchmarks-game "binary-trees": allocates millions of short-lived nodes while one long-lived
// tree stays reachable. Without a GC this needs gigabytes; with csrt's collector it stays small.
const int MinDepth = 4;
const int MaxDepth = 18;

TreeNode stretch = TreeNode.Create(MaxDepth + 1);
Console.WriteLine($"stretch tree of depth {MaxDepth + 1}\t check: {stretch.Check()}");

TreeNode longLived = TreeNode.Create(MaxDepth);

for (int depth = MinDepth; depth <= MaxDepth; depth += 2)
{
    int iterations = 1 << (MaxDepth - depth + MinDepth);
    int check = 0;
    for (int i = 0; i < iterations; i++)
        check += TreeNode.Create(depth).Check();
    Console.WriteLine($"{iterations}\t trees of depth {depth}\t check: {check}");
}

Console.WriteLine($"long lived tree of depth {MaxDepth}\t check: {longLived.Check()}");

class TreeNode
{
    private readonly TreeNode? _left;
    private readonly TreeNode? _right;

    private TreeNode(TreeNode? left, TreeNode? right)
    {
        _left = left;
        _right = right;
    }

    public static TreeNode Create(int depth) =>
        depth > 0 ? new TreeNode(Create(depth - 1), Create(depth - 1)) : new TreeNode(null, null);

    public int Check() => _left == null || _right == null ? 1 : 1 + _left.Check() + _right.Check();
}
