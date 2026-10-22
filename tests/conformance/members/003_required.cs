var c = new Config { Host = "localhost", Port = 80 };
Console.WriteLine($"{c.Host}:{c.Port}");
class Config { public required string Host { get; init; } public required int Port { get; init; } }
