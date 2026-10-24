// Stream's async members (ReadAsync/WriteAsync over arrays and memory, FlushAsync, CopyToAsync,
// DisposeAsync) and FileStream's useAsync constructor.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "csharpc_502_" + Environment.ProcessId + ".bin");
        byte[] data = Encoding.UTF8.GetBytes("stream async ✓");
        await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await fs.WriteAsync(data, 0, data.Length);
            await fs.WriteAsync(new ReadOnlyMemory<byte>(data, 0, 6));
            await fs.FlushAsync();
        }
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
        {
            var buffer = new byte[64];
            int n = await fs.ReadAsync(buffer, 0, buffer.Length);
            Console.WriteLine($"{n}: {Encoding.UTF8.GetString(buffer, 0, n)}");
            fs.Position = 0;
            int m = await fs.ReadAsync(buffer.AsMemory(0, 6));
            Console.WriteLine($"{m}: {Encoding.UTF8.GetString(buffer, 0, m)}");
            fs.Position = 0;
            var copy = new MemoryStream();
            await fs.CopyToAsync(copy);
            Console.WriteLine(copy.Length);
        }
        var ms = new MemoryStream();
        await ms.WriteAsync(data, 0, 6, CancellationToken.None);
        Console.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Console.WriteLine(ms.ReadAsync(new byte[4], 0, 4, cts.Token).Status);
        try { await ms.WriteAsync(null!, 0, 1); }
        catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name); }
        await ms.DisposeAsync();
        File.Delete(path);
    }
}
