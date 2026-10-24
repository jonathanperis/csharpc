// Microsoft.Extensions.* abstractions shipped in the shared framework (Primitives, Options,
// DependencyInjection.Abstractions, Logging.Abstractions), compiled from their IL.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

// StringValues: zero, one or many strings without allocating for the common cases.
StringValues none = StringValues.Empty;
StringValues one = "alpha";
StringValues many = new(["beta", "gamma", "delta"]);
Console.WriteLine($"{none.Count} {one.Count} {many.Count} '{many}' '{StringValues.Concat(one, many)}'");
Console.WriteLine($"{one == "alpha"} {many[1]} {string.Join("|", (string?[])many!)} {StringValues.IsNullOrEmpty(none)}");
foreach (var s in many) Console.Write(s + ";");
Console.WriteLine();

// StringSegment and StringTokenizer: slicing without substrings.
var segment = new StringSegment("  key = value  ", 1, 12);
Console.WriteLine($"[{segment}] [{segment.Trim()}] {segment.Length} {segment.IndexOf('=')} {segment.StartsWith(" key", StringComparison.Ordinal)}");
var tokens = new StringTokenizer("a,b;;c,d", [',', ';']);
Console.WriteLine(string.Join(" ", tokens.Select(t => $"<{t}>")));
Console.WriteLine(StringSegment.Equals("Hello", "hello", StringComparison.OrdinalIgnoreCase));

// Options.
var options = Options.Create(new GreeterOptions { Greeting = "Hi", Times = 2 });
Console.WriteLine($"{options.Value.Greeting} x{options.Value.Times}");
Console.WriteLine(Options.DefaultName.Length);

// Dependency injection abstractions: descriptors and the collection.
var services = new ServiceCollection();
services.AddSingleton<IClock, FixedClock>();
services.AddTransient(typeof(GreeterOptions));
services.AddScoped<IClock>(_ => new FixedClock());
services.TryAddSingletonClock();
foreach (var d in services)
    Console.WriteLine($"{d.ServiceType.Name} {d.Lifetime} impl={d.ImplementationType?.Name ?? "-"} factory={d.ImplementationFactory is not null}");
Console.WriteLine(services.Count);

// Logging abstractions: a custom logger, the extension methods and message templates.
var logger = new ConsoleLogger();
logger.LogInformation("Processed {Count} items in {Elapsed} ms", 42, 17);
logger.LogWarning(new EventId(7, "Seven"), "Disk {Name} is {Percent}% full", "C", 93.5);
logger.Log(LogLevel.Debug, "below the threshold");
NullLogger.Instance.LogError("never printed");
Console.WriteLine(NullLogger.Instance.IsEnabled(LogLevel.Critical));

sealed class GreeterOptions
{
    public string Greeting { get; set; } = "";
    public int Times { get; set; }
}

interface IClock { DateTime Now { get; } }

sealed class FixedClock : IClock { public DateTime Now => new(2026, 1, 2); }

static class Registrations
{
    public static void TryAddSingletonClock(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IClock) && d.Lifetime == ServiceLifetime.Singleton))
            services.AddSingleton<IClock, FixedClock>();
    }
}

sealed class ConsoleLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        Console.WriteLine($"{logLevel} [{eventId.Id}:{eventId.Name}] {formatter(state, exception)}");
        if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            Console.WriteLine("  " + string.Join(", ", values.Select(v => $"{v.Key}={v.Value}")));
    }
}
