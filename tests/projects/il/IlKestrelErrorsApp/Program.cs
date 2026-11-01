using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting.Server.Features;

// Kestrel when the application throws: a request handler that throws before responding gets a 500 and
// the error is logged (Kestrel's ApplicationError, event 13); one that throws after starting the response
// has its connection aborted; a connection middleware that throws (as TechEmpower's Platform entry is one)
// closes the connection and Kestrel logs it. The logger records what was logged, so the output is
// deterministic. Compiled from the shared framework's IL.
var logged = new BlockingCollection<string>();
var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new RecordingProvider(logged));
builder.WebHost.UseKestrel(o =>
{
    o.Listen(IPAddress.Loopback, 0);
    o.Listen(IPAddress.Loopback, 0, l => l.Use(next => Connection.ExecuteAsync));
});
var app = builder.Build();
app.Run(async context =>
{
    switch (context.Request.Path.Value)
    {
        case "/sync": throw new InvalidOperationException("sync failure");
        case "/async": await Task.Yield(); throw new InvalidOperationException("async failure");
        case "/late":
            await context.Response.WriteAsync("partial");
            await context.Response.Body.FlushAsync();
            throw new InvalidOperationException("late failure");
        default: await context.Response.WriteAsync("ok"); break;
    }
});
await app.StartAsync();
var ports = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<IServerAddressesFeature>()!
    .Addresses.Select(a => new Uri(a).Port).ToArray();

foreach (var path in new[] { "/ok", "/sync", "/async", "/late" })
{
    var response = await Request(ports[0], path);
    var firstLine = response.Split("\r\n")[0];
    Console.WriteLine($"{path}: {(firstLine.Length == 0 ? "(closed)" : firstLine)}{(response.Contains("partial") ? " + partial body" : "")}");
    if (path != "/ok") Console.WriteLine($"  logged: {Next(logged)}");
}
foreach (var path in new[] { "/ok", "/throw", "/caught" })
{
    var response = await Request(ports[1], path);
    Console.WriteLine($"middleware {path}: {(response.Length == 0 ? "(closed)" : response.Split("\r\n")[0])}");
    if (path == "/throw") Console.WriteLine($"  logged: {Next(logged)}");
}
await app.StopAsync();
Console.WriteLine($"more logged: {logged.Count}");

static string Next(BlockingCollection<string> logged) => logged.TryTake(out var entry, TimeSpan.FromSeconds(30)) ? entry : "(nothing)";

static async Task<string> Request(int port, string path)
{
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
    await socket.SendAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"), SocketFlags.None);
    var received = new MemoryStream();
    var buffer = new byte[4096];
    try
    {
        int n;
        while ((n = await socket.ReceiveAsync(buffer, SocketFlags.None)) > 0) received.Write(buffer, 0, n);
    }
    catch (SocketException) { } // an aborted connection may end with a reset
    return Encoding.ASCII.GetString(received.ToArray());
}

static class Connection
{
    public static async Task ExecuteAsync(ConnectionContext connection)
    {
        var input = connection.Transport.Input;
        var output = connection.Transport.Output;
        var result = await input.ReadAsync();
        var request = Encoding.ASCII.GetString(result.Buffer.FirstSpan);
        input.AdvanceTo(result.Buffer.End);
        var path = request.Split(' ')[1];
        if (path == "/throw") { await Task.Yield(); throw new InvalidOperationException("middleware failure"); }
        if (path == "/caught")
        {
            // TechEmpower's Platform entry: the exception completes the pipes, which closes the connection.
            try { await Task.Yield(); throw new InvalidOperationException("caught failure"); }
            catch (Exception e) { input.Complete(e); }
            finally { output.Complete(); }
            return;
        }
        await output.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
        output.Complete();
    }
}

sealed class RecordingProvider(BlockingCollection<string> logged) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, logged);
    public void Dispose() { }

    sealed class Recorder(string category, BlockingCollection<string> logged) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) logged.Add($"{logLevel} {category}[{eventId.Id}] {exception?.GetType().Name}: {exception?.Message}");
        }
    }
}
