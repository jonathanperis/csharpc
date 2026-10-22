var o = new Order(1, new Customer("Ada", null), new[] { 1, 2 });
Console.WriteLine(o);
record Customer(string Name, string? Email);
record Order(int Id, Customer Who, int[] Items);
