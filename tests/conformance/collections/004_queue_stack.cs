var q = new Queue<string>();
q.Enqueue("a"); q.Enqueue("b"); q.Enqueue("c");
Console.WriteLine(q.Dequeue() + q.Peek() + q.Count);
var s = new Stack<int>();
s.Push(1); s.Push(2);
Console.WriteLine(s.Pop() + " " + s.Peek() + " " + s.Count);
