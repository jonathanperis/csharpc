var list = new MyList();
for (int i = 1; i <= 5; i++) list.Add(i);
list.Reverse();
Console.WriteLine(list);
class MyList
{
    class Node { public int V; public Node? Next; }
    Node? head;
    public void Add(int v) { var n = new Node { V = v }; if (head == null) head = n; else { var c = head; while (c.Next != null) c = c.Next; c.Next = n; } }
    public void Reverse() { Node? prev = null, cur = head; while (cur != null) { var nx = cur.Next; cur.Next = prev; prev = cur; cur = nx; } head = prev; }
    public override string ToString() { var s = ""; for (var c = head; c != null; c = c.Next) s += c.V + (c.Next != null ? "->" : ""); return s; }
}
