var q = new Queue<int>(new[] { 1, 2, 3 });
q.Enqueue(4); q.Dequeue();
Console.WriteLine(string.Join(",", q) + " peek=" + q.Peek());
var s = new Stack<int>(new[] { 1, 2, 3 });
s.Push(4);
Console.WriteLine(string.Join(",", s) + " peek=" + s.Peek() + " arr=" + string.Join(",", s.ToArray()));
try { new Stack<int>().Pop(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
