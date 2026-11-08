// BCL members .NET's Linux TLS and X.509 code (the OpenSSL backend of System.Net.Security and
// System.Security.Cryptography, compiled from the framework's IL over the runtime pack's OpenSSL shim) relies on,
// beyond those of 1250 and 1272: the span overloads of DateTime.ParseExact/TryParseExact (certificate validity
// times), Convert.TryToBase64Chars (URL-safe Base64 of certificate hashes), the typed WeakGCHandle<T> (the
// SslStream context's reference to its options) and GC.GetGeneration (the chain cache's collection watcher).
using System;
using System.Globalization;
using System.Runtime.InteropServices;

class Options
{
    public string Name = "options";
}

static class Program
{
    static void Main()
    {
        var utc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        Console.WriteLine(DateTime.TryParseExact("20301231235959Z".AsSpan(), "yyyyMMddHHmmss'Z'".AsSpan(), CultureInfo.InvariantCulture, utc, out DateTime t1) + " " + t1.ToString("O", CultureInfo.InvariantCulture));
        Console.WriteLine(DateTime.TryParseExact("301231235959Z".AsSpan(), "yyMMddHHmmss'Z'".AsSpan(), CultureInfo.InvariantCulture, utc, out DateTime t2) + " " + t2.ToString("O", CultureInfo.InvariantCulture));
        Console.WriteLine(DateTime.TryParseExact("not a time".AsSpan(), "yyyyMMddHHmmss'Z'".AsSpan(), CultureInfo.InvariantCulture, utc, out DateTime t3) + " " + t3.Ticks);
        Console.WriteLine(DateTime.TryParseExact("2030-12-31".AsSpan(), new[] { "yyyyMMdd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t4) + " " + t4.ToString("O", CultureInfo.InvariantCulture));
        Console.WriteLine(DateTime.ParseExact("20301231".AsSpan(), "yyyyMMdd".AsSpan(), CultureInfo.InvariantCulture).ToString("O", CultureInfo.InvariantCulture));
        Console.WriteLine(DateTime.ParseExact("2030-12-31".AsSpan(), new[] { "yyyyMMdd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture).ToString("O", CultureInfo.InvariantCulture));
        try { DateTime.ParseExact("31/12/2030".AsSpan(), "yyyyMMdd".AsSpan(), CultureInfo.InvariantCulture); }
        catch (FormatException) { Console.WriteLine("FormatException"); }

        byte[] data = { 0xFB, 0xFF, 0xFE, 0x01, 0x02 };
        Span<char> big = stackalloc char[16];
        Console.WriteLine(Convert.TryToBase64Chars(data, big, out int written) + " " + written + " " + new string(big[..written]));
        Span<char> small = stackalloc char[4];
        Console.WriteLine(Convert.TryToBase64Chars(data, small, out written) + " " + written);
        Console.WriteLine(Convert.TryToBase64Chars(ReadOnlySpan<byte>.Empty, small, out written) + " " + written);
        Span<char> lines = stackalloc char[200];
        Console.WriteLine(Convert.TryToBase64Chars(new byte[80], lines, out written, Base64FormattingOptions.InsertLineBreaks) + " " + written);

        var options = new Options();
        var weak = new WeakGCHandle<Options>(options);
        Console.WriteLine(weak.IsAllocated);
        IntPtr pointer = WeakGCHandle<Options>.ToIntPtr(weak);
        var again = WeakGCHandle<Options>.FromIntPtr(pointer);
        Console.WriteLine(again.TryGetTarget(out Options target) + " " + target.Name);
        again.SetTarget(new Options { Name = "replaced" });
        Console.WriteLine(weak.TryGetTarget(out target) + " " + target.Name);
        Console.WriteLine(weak.Equals(again));
        GC.KeepAlive(options);
        weak.Dispose();
        Console.WriteLine(weak.IsAllocated);

        int generation = GC.GetGeneration(options);
        Console.WriteLine(generation >= 0 && generation <= GC.MaxGeneration);
        try { GC.GetGeneration((object)null); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }
    }
}
