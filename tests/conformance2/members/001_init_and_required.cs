var c = new Config { Name = "svc", Port = 8080 };
var d = c with { Port = 9090 };
Console.WriteLine($"{c.Name}:{c.Port} {d.Name}:{d.Port} {c.Debug}");
record Config { public required string Name { get; init; } public int Port { get; init; } = 80; public bool Debug { get; init; } }
