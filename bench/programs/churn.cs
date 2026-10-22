// Allocation churn: millions of short-lived small objects and a few long-lived ones.
long sum = 0;
Node? keep = null;
for (int round = 0; round < 60; round++)
{
    Node? list = null;
    for (int i = 0; i < 200_000; i++) list = new Node(i, list);
    for (Node? p = list; p != null; p = p.Next) sum += p.Value;
    if (round % 10 == 0) keep = new Node(round, keep);
}
int kept = 0;
for (Node? p = keep; p != null; p = p.Next) kept++;
Console.WriteLine(sum);
Console.WriteLine(kept);

class Node
{
    public readonly long Value;
    public readonly Node? Next;
    public Node(long value, Node? next) { Value = value; Next = next; }
}
