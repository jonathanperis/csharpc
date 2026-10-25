// Exception.HResult (the types' defaults, the setter in derived constructors, IOException's errno),
// OperationCanceledException.CancellationToken, MemoryExtensions.Split into ranges,
// Directory.CreateDirectory with a Unix mode, RuntimeInformation.ProcessArchitecture and Oid.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

class CustomError : Exception
{
    public CustomError() : base("custom") { HResult = 0x1234; }
}

class Program
{
    static string Hex(int value) => "0x" + value.ToString("X8");

    static void Main()
    {
        var samples = new List<Exception>
        {
            new Exception(), new ArgumentException(), new ArgumentNullException("x"), new ArgumentOutOfRangeException("y"),
            new InvalidOperationException(), new NotSupportedException(), new FormatException(), new IOException(),
            new FileNotFoundException(), new DirectoryNotFoundException(), new UnauthorizedAccessException(),
            new OperationCanceledException(), new KeyNotFoundException(), new TimeoutException(), new ObjectDisposedException("o"),
            new AggregateException(), new CustomError(), new IOException("with errno", 17),
        };
        foreach (var e in samples) Console.WriteLine($"{e.GetType().Name} {Hex(e.HResult)}");

        try { File.ReadAllText("/definitely/missing/file.txt"); }
        catch (Exception e) { Console.WriteLine($"{e.GetType().Name} {Hex(e.HResult)}"); }

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { cts.Token.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException e) { Console.WriteLine($"{e.CancellationToken == cts.Token} {e.CancellationToken.IsCancellationRequested}"); }
        var explicitToken = new OperationCanceledException("m", cts.Token);
        Console.WriteLine($"{explicitToken.CancellationToken == cts.Token} {new OperationCanceledException().CancellationToken == CancellationToken.None}");

        Span<Range> ranges = stackalloc Range[4];
        ReadOnlySpan<char> text = "a,b,,c,d,e";
        int n = text.Split(ranges, ',');
        for (int i = 0; i < n; i++) Console.Write("[" + text[ranges[i]].ToString() + "]");
        Console.WriteLine();
        n = " x , ,y ".AsSpan().Split(ranges, ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < n; i++) Console.Write("[" + " x , ,y "[ranges[i]] + "]");
        Console.WriteLine(n);

        if (!OperatingSystem.IsWindows())
        {
            string dir = Path.Combine(Path.GetTempPath(), "csharpc-mode-" + Environment.ProcessId, "child");
            Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Console.WriteLine($"{Directory.Exists(dir)} {File.GetUnixFileMode(dir)}");
            Directory.Delete(Path.GetDirectoryName(dir), true);
        }

        Console.WriteLine(RuntimeInformation.ProcessArchitecture == Architecture.X64 || RuntimeInformation.ProcessArchitecture == Architecture.Arm64 || RuntimeInformation.ProcessArchitecture == Architecture.Wasm);
        var oid = new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication");
        var oids = new OidCollection { oid };
        foreach (var o in oids) Console.WriteLine($"{o.Value} {o.FriendlyName} {oids.Count}");
    }
}
