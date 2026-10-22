// Allocation churn: ~10M short-lived strings and ~200k large arrays, while long-lived data held
// from a static field, a local and an array of arrays must survive every collection intact.
Registry.Head = BuildList(100_000);
int[][] keep = new int[64][];
for (int k = 0; k < keep.Length; k++) keep[k] = Filled(1000 + k, k);

long totalLength = 0;
string last = "";
for (int i = 0; i < 10_000_000; i++)
{
    last = "item-" + i;
    totalLength += last.Length;
}
Console.WriteLine($"strings: total length {totalLength}, last = {last}");

long arraySum = 0;
for (int i = 0; i < 200_000; i++)
{
    int[] scratch = Filled(2048, i);          // 8 KiB+: large-object path
    arraySum += scratch[i % scratch.Length];
    if (i % 3125 == 0) keep[i / 3125 % keep.Length] = Filled(1000, i);
}
Console.WriteLine($"arrays: sum {arraySum}");

long keptSum = 0;
for (int k = 0; k < keep.Length; k++)
    for (int j = 0; j < keep[k].Length; j++) keptSum += keep[k][j];
Console.WriteLine($"kept arrays: sum {keptSum}");

long listSum = 0;
int count = 0;
for (Node? n = Registry.Head; n != null; n = n.Next) { listSum += n.Value; count++; }
Console.WriteLine($"list from static field: {count} nodes, sum {listSum}");

static Node BuildList(int length)
{
    Node head = new Node(0, null);
    for (int i = 1; i < length; i++) head = new Node(i, head);
    return head;
}

static int[] Filled(int length, int seed)
{
    int[] a = new int[length];
    for (int i = 0; i < a.Length; i++) a[i] = seed + i;
    return a;
}

static class Registry
{
    public static Node? Head;
}

class Node
{
    public readonly int Value;
    public readonly Node? Next;
    public Node(int value, Node? next)
    {
        Value = value;
        Next = next;
    }
}
