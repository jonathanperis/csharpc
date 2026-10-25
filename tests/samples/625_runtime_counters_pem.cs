// Members ASP.NET Core's metrics and certificate code reach: the GC and thread-pool counters (only their
// invariants, the values differ between runtimes), Environment.OSVersion / OSDescription, PemEncoding.Write,
// TaskCompletionSource.TrySetCanceled(token) and ProcessStartInfo.
#nullable disable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var keep = new byte[100_000];
        Console.WriteLine(keep.Length);
        Console.WriteLine($"{GC.CollectionCount(0) >= 0} {GC.CollectionCount(0) >= GC.CollectionCount(1)} {GC.MaxGeneration >= 0}");
        Console.WriteLine($"{GC.GetTotalAllocatedBytes() >= 100_000} {GC.GetTotalPauseDuration() >= TimeSpan.Zero}");
        Console.WriteLine($"{ThreadPool.ThreadCount >= 0} {ThreadPool.PendingWorkItemCount >= 0} {ThreadPool.CompletedWorkItemCount >= 0} {Timer.ActiveCount >= 0}");
        Console.WriteLine($"{Environment.WorkingSet > 0} {Environment.CpuUsage.TotalTime >= TimeSpan.Zero}");

        var os = Environment.OSVersion;
        Console.WriteLine(os.Platform == (OperatingSystem.IsWindows() ? PlatformID.Win32NT : PlatformID.Unix));
        Console.WriteLine($"{os.Version.Major >= 0} {os.VersionString.Length > 0} {RuntimeInformation.OSDescription.Length > 0}");

        byte[] der = new byte[70];
        for (int i = 0; i < der.Length; i++) der[i] = (byte)(i * 7);
        Console.WriteLine(new string(PemEncoding.Write("CERTIFICATE", der)));
        Console.WriteLine(PemEncoding.WriteString("PRIVATE KEY", new byte[] { 1, 2, 3 }));
        Console.WriteLine(PemEncoding.GetEncodedSize(11, 70));

        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<int>();
        Console.WriteLine(tcs.TrySetCanceled(cts.Token));
        Console.WriteLine(tcs.TrySetCanceled(cts.Token));
        try { await tcs.Task; }
        catch (TaskCanceledException e) { Console.WriteLine($"{e.CancellationToken == cts.Token} {tcs.Task.Status}"); }
        var plain = new TaskCompletionSource();
        plain.SetCanceled(cts.Token);
        try { await plain.Task; }
        catch (OperationCanceledException e) { Console.WriteLine($"{e.GetType().Name} {e.CancellationToken == cts.Token}"); }

        var psi = new ProcessStartInfo("tool", "--flag value");
        psi.ArgumentList.Add("x");
        psi.Environment["KEY"] = "v";
        Console.WriteLine($"{psi.FileName} '{psi.Arguments}' {psi.ArgumentList.Count} {psi.UseShellExecute} {psi.RedirectStandardOutput} {psi.CreateNoWindow} '{psi.WorkingDirectory}' {psi.Environment["KEY"]}");
    }
}
