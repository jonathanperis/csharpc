using Microsoft.Extensions.Configuration;

// Every call below is intercepted by the configuration-binding generator's code; without the interceptors
// they would bind through reflection, which csharpc (like NativeAOT) doesn't keep for these types.
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionString"] = "Server=db;Database=hello",
        ["Port"] = "8080",
        ["Ratio"] = "0.75",
        ["Mode"] = "Fast",
        ["Tags:0"] = "a",
        ["Tags:1"] = "b",
        ["Limits:Requests"] = "100",
        ["Limits:Burst"] = "7",
        ["Nested:Value"] = "1.5",
    })
    .Build();

var settings = config.Get<AppSettings>()!;
Console.WriteLine($"{settings.ConnectionString} {settings.Port} {settings.Ratio} {settings.Mode} [{string.Join(",", settings.Tags)}] {settings.Missing ?? "(null)"}");
Console.WriteLine(string.Join(", ", settings.Limits.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}")));

var bound = new AppSettings { Port = 1 };
config.Bind(bound);
Console.WriteLine($"{bound.Port} {bound.Tags.Count}");

Console.WriteLine(config.GetValue<int>("Port") + 1);
Console.WriteLine(config.GetValue("Absent", 42));
Console.WriteLine(config.GetSection("Nested").Get<Nested>()!.Value * 2);
Console.WriteLine(config.Get<Nested>() is { Value: 0 });

public enum Mode { Slow, Fast }

public sealed class AppSettings
{
    public string ConnectionString { get; set; } = "";
    public int Port { get; set; }
    public double Ratio { get; set; }
    public Mode Mode { get; set; }
    public List<string> Tags { get; set; } = [];
    public Dictionary<string, int> Limits { get; set; } = [];
    public string? Missing { get; set; }
}

public sealed class Nested
{
    public double Value { get; set; }
}
