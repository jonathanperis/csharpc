object o = new object();
Console.WriteLine(o.ToString());
Console.WriteLine(o.Equals(o));
Console.WriteLine(ReferenceEquals(o, new object()));
Console.WriteLine(o.GetType().Name);
Console.WriteLine(42.GetType().FullName);
