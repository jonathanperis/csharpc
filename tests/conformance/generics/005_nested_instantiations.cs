var list = new Node<Node<int>>(new Node<int>(5, null), null);
Console.WriteLine(list.Value.Value);
class Node<T>(T value, Node<T>? next) { public T Value = value; public Node<T>? Next = next; }
