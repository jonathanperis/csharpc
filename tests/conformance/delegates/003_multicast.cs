Action a = () => Console.WriteLine("one");
a += () => Console.WriteLine("two");
a();
