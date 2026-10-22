var ll = new LinkedList<string>();
var b = ll.AddLast("b");
ll.AddFirst("a"); ll.AddAfter(b, "c"); ll.AddBefore(b, "ab");
Console.WriteLine(string.Join(",", ll) + " " + ll.First!.Value + ll.Last!.Value + " " + ll.Count);
ll.Remove("ab"); ll.RemoveFirst();
Console.WriteLine(string.Join(",", ll) + " " + (ll.Find("c")?.Previous?.Value));
