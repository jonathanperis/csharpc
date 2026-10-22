// binary-trees — Computer Language Benchmarks Game. Allocation-heavy.
// NOTE: csrt has no GC yet, so every node is leaked; depth is capped accordingly.
int minDepth = 4;
int maxDepth = 16;
int stretchDepth = maxDepth + 1;
Console.WriteLine("stretch tree of depth " + stretchDepth + "\t check: " + Check(Bottom(stretchDepth)));
TreeNode longLived = Bottom(maxDepth);
for (int depth = minDepth; depth <= maxDepth; depth += 2)
{
    int iterations = 1 << (maxDepth - depth + minDepth);
    int check = 0;
    for (int i = 0; i < iterations; i++) check += Check(Bottom(depth));
    Console.WriteLine(iterations + "\t trees of depth " + depth + "\t check: " + check);
}
Console.WriteLine("long lived tree of depth " + maxDepth + "\t check: " + Check(longLived));

static TreeNode Bottom(int depth) =>
    depth > 0 ? new TreeNode(Bottom(depth - 1), Bottom(depth - 1)) : new TreeNode(null, null);

static int Check(TreeNode node) =>
    node.Left == null ? 1 : 1 + Check(node.Left) + Check(node.Right!);

class TreeNode
{
    public TreeNode? Left;
    public TreeNode? Right;

    public TreeNode(TreeNode? left, TreeNode? right)
    {
        Left = left;
        Right = right;
    }
}
