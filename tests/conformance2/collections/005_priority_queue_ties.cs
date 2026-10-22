var pq = new PriorityQueue<string, int>();
pq.Enqueue("a", 2); pq.Enqueue("b", 1); pq.Enqueue("c", 2); pq.Enqueue("d", 1); pq.Enqueue("e", 0);
while (pq.TryDequeue(out var item, out var pri)) Console.Write($"{item}{pri} ");
Console.WriteLine();
