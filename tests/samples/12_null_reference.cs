// Member access on null throws NullReferenceException.
Node? head = new Node(1, new Node(2, new Node(3, null)));
int sum = 0;
while (true)
{
    sum += head!.Value;
    Console.WriteLine(sum);
    head = head.Next;
}

class Node(int value, Node? next)
{
    public int Value = value;
    public Node? Next = next;
}
