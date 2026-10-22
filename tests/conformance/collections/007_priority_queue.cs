var pq = new PriorityQueue<string, int>();
pq.Enqueue("low", 5); pq.Enqueue("high", 1); pq.Enqueue("mid", 3);
while (pq.Count > 0) Console.WriteLine(pq.Dequeue());
