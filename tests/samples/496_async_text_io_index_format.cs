// Async TextReader/TextWriter members, File's token/encoding async overloads, String.StartsWith/EndsWith
// with a culture, and Index/Range formatting.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var reader = new StringReader("first\nsecond\r\nthird");
        Console.WriteLine(await reader.ReadLineAsync());
        Console.WriteLine(await reader.ReadLineAsync(CancellationToken.None));
        Console.WriteLine(await reader.ReadToEndAsync());
        Console.WriteLine((await reader.ReadLineAsync()) ?? "<eof>");
        var buffer = new char[4];
        var r2 = new StringReader("abcdef");
        Console.WriteLine($"{await r2.ReadAsync(buffer, 0, 4)} {new string(buffer)} {await r2.ReadBlockAsync(buffer, 0, 4)}");

        var writer = new StringWriter();
        await writer.WriteAsync("x=");
        await writer.WriteAsync('1');
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("line");
        await writer.WriteAsync(new[] { 'a', 'b', 'c' }, 1, 2);
        await writer.WriteLineAsync('!');
        await writer.FlushAsync();
        Console.Write(writer.ToString());

        string path = Path.Combine(Path.GetTempPath(), "csharpc_495_" + Environment.ProcessId + ".txt");
        await File.WriteAllTextAsync(path, "héllo\nworld", CancellationToken.None);
        Console.WriteLine(await File.ReadAllTextAsync(path, CancellationToken.None));
        await File.AppendAllTextAsync(path, "\n!", Encoding.UTF8, CancellationToken.None);
        Console.WriteLine((await File.ReadAllLinesAsync(path, CancellationToken.None)).Length);
        Console.WriteLine((await File.ReadAllBytesAsync(path, CancellationToken.None)).Length);
        await File.WriteAllLinesAsync(path, new[] { "1", "2" }, CancellationToken.None);
        using (var sr = new StreamReader(path)) Console.WriteLine((await sr.ReadToEndAsync()).Replace("\n", "|"));
        using (var sw = new StreamWriter(path)) { await sw.WriteLineAsync("via writer"); }
        Console.WriteLine(File.ReadAllText(path).Trim());
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = File.WriteAllTextAsync(path, "x", cts.Token);
        Console.WriteLine(canceled.Status);
        try { await File.ReadAllTextAsync(path + ".missing", CancellationToken.None); }
        catch (FileNotFoundException e) { Console.WriteLine(e.GetType().Name); }
        try { await File.ReadAllTextAsync("", CancellationToken.None); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }
        File.Delete(path);

        Console.WriteLine("Hello".StartsWith("he", true, CultureInfo.InvariantCulture));
        Console.WriteLine("Hello".StartsWith("he", false, CultureInfo.InvariantCulture));
        Console.WriteLine("file".EndsWith("LE", true, null));
        Console.WriteLine("file".EndsWith("LE", false, null));
        try { "x".StartsWith(null!, true, null); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

        Index ix = ^3;
        Range r = 1..^2;
        Console.WriteLine($"{ix} {r} {(Index)4} {..} {r.Start} {..5} {2..} {ix.Value} {ix.IsFromEnd}");
        Console.WriteLine("[" + ix + "] " + r.End);
    }
}
