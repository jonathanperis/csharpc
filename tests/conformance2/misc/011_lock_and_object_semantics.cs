var o = new object();
int n = 0;
lock (o) { lock (o) { n++; } }
Console.WriteLine(n);
Console.WriteLine(o.GetType().FullName);
Console.WriteLine(new object().Equals(new object()));
