string? s = null;
Console.WriteLine(s?.Length ?? -1);
s = "abc";
Console.WriteLine(s?.Length);
int[]? arr = null;
Console.WriteLine(arr?[0] ?? 99);
Node? n = new Node { Next = new Node { V = 7 } };
Console.WriteLine(n?.Next?.V);
Console.WriteLine(n?.Next?.Next?.V ?? 0);
class Node { public int V; public Node? Next; }
