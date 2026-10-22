var orders = new[] { new Order("A", 50, true), new Order("B", 500, false), new Order("C", 1500, true) };
foreach (var o in orders)
    Console.WriteLine(o switch
    {
        { Total: > 1000, Paid: true } => "big paid",
        { Paid: false } => "unpaid",
        { Customer: "A" } => "customer A",
        _ => "other",
    });
record Order(string Customer, decimal Total, bool Paid);
