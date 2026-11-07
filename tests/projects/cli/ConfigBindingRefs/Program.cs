using ConfigBindingRefsLib;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Name"] = "world", ["Count"] = "3" })
    .Build();

var settings = config.Get<Settings>()!;
Console.WriteLine(Greeting.For(settings.Name));
Console.WriteLine(settings.Count * 2);

public sealed class Settings
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}
