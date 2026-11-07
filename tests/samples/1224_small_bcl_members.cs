// Small BCL members the IL survey of nuget.org packages ranks: string.ToCharArray(int, int), Math.Log(a, newBase),
// WebUtility.UrlEncode(string), Thread.GetCurrentProcessorId, HashCode.AddBytes, IPAddress from a span and IPAddress.Address,
// Int128/UInt128.ToString(IFormatProvider) and Task.Delay(TimeSpan[, CancellationToken]); exception types and messages as .NET.
#pragma warning disable CS0618
using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

static class Program
{
    static void Try(Action action)
    {
        try { action(); }
        catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    }

    static async Task Main()
    {
        var s = "hello world";
        Console.WriteLine(new string(s.ToCharArray(2, 5)));
        Console.WriteLine(s.ToCharArray(0, 0).Length);
        Console.WriteLine(s.ToCharArray(11, 0).Length);
        Console.WriteLine(new string(s.ToCharArray(6, 5)));
        Try(() => s.ToCharArray(8, 5));
        Try(() => s.ToCharArray(-1, 1));
        Try(() => s.ToCharArray(12, 0));
        Try(() => s.ToCharArray(2, -1));
        Try(() => ((string)null!).ToCharArray(0, 1));

        Console.WriteLine(Math.Log(8, 2));
        Console.WriteLine(Math.Log(100, 10));
        Console.WriteLine(Math.Log(1, 5));
        Console.WriteLine(Math.Log(0, 2));
        Console.WriteLine(Math.Log(8, 1));
        Console.WriteLine(Math.Log(-1, 2));
        Console.WriteLine(Math.Log(double.NaN, 2));
        Console.WriteLine(Math.Log(2, double.NaN));
        Console.WriteLine(Math.Log(2, double.PositiveInfinity));
        Console.WriteLine(Math.Log(1, double.PositiveInfinity));
        Console.WriteLine(Math.Log(2, 0));
        Console.WriteLine(Math.Log(1, 0));
        Console.WriteLine(Math.Log(double.PositiveInfinity, 3));
        Console.WriteLine(Math.Log(0.5, 0.25));
        Console.WriteLine(Math.Log(1000, Math.E));

        Console.WriteLine(WebUtility.UrlEncode("a b&c=d/é?~-_.!*'()"));
        Console.WriteLine(WebUtility.UrlEncode("日本語 \U0001F600"));
        Console.WriteLine(WebUtility.UrlEncode("lone \uD800 surrogate"));
        Console.WriteLine(WebUtility.UrlEncode("").Length);
        Console.WriteLine(WebUtility.UrlEncode(null) ?? "<null>");

        Console.WriteLine(Thread.GetCurrentProcessorId() >= 0);

        var first = new HashCode();
        first.AddBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        var second = new HashCode();
        second.AddBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        var third = new HashCode();
        third.AddBytes(new byte[] { 1, 2, 3, 4, 5, 6, 8 });
        var split = new HashCode();
        split.AddBytes(new byte[] { 1, 2, 3, 4 });
        split.AddBytes(new byte[] { 5, 6, 7 });
        Console.WriteLine(first.ToHashCode() == second.ToHashCode());
        Console.WriteLine(first.ToHashCode() == third.ToHashCode());
        // Four bytes are one int, the rest single bytes: the same as adding them that way.
        var manual = new HashCode();
        manual.Add(0x04030201);
        manual.Add(5); manual.Add(6); manual.Add(7);
        Console.WriteLine(split.ToHashCode() == manual.ToHashCode());
        var empty = new HashCode();
        empty.AddBytes(ReadOnlySpan<byte>.Empty);
        Console.WriteLine(empty.ToHashCode() == new HashCode().ToHashCode());

        var ip = new IPAddress(new byte[] { 10, 1, 2, 3 }.AsSpan());
        Console.WriteLine(ip);
        Console.WriteLine(ip.AddressFamily);
        Console.WriteLine(ip.Address);
        var ip6 = new IPAddress(new byte[] { 0x20, 1, 0xd, 0xb8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }.AsSpan(), 3);
        Console.WriteLine(ip6);
        Console.WriteLine(ip6.ScopeId);
        Console.WriteLine(new IPAddress(new byte[16].AsSpan()));
        Try(() => { var _ = ip6.Address; });
        Try(() => new IPAddress(new byte[] { 1, 2, 3 }.AsSpan()));
        Try(() => new IPAddress(new byte[] { 1, 2, 3, 4 }.AsSpan(), 1));
        Try(() => new IPAddress(new byte[16].AsSpan(), -1));
        Try(() => new IPAddress(new byte[16].AsSpan(), 0x1_0000_0000));
        var settable = new IPAddress(0);
        settable.Address = 0x0100007F;
        Console.WriteLine(settable);
        Console.WriteLine(new IPAddress(0x0100007FL).Address);

        Console.WriteLine(Int128.Parse("170141183460469231731687303715884105727").ToString(CultureInfo.InvariantCulture));
        Console.WriteLine((-Int128.One).ToString(CultureInfo.InvariantCulture));
        var tilde = new NumberFormatInfo { NegativeSign = "~" };
        Console.WriteLine(Int128.MinValue.ToString(tilde));
        Console.WriteLine((-Int128.One * 12345).ToString(tilde));
        Console.WriteLine(Int128.Zero.ToString((IFormatProvider?)null));
        Console.WriteLine(UInt128.MaxValue.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine(UInt128.Zero.ToString(tilde));

        await Task.Delay(TimeSpan.FromMilliseconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
        await Task.Delay(TimeSpan.Zero);
        await Task.Delay(TimeSpan.FromMilliseconds(0.9));
        Try(() => Task.Delay(TimeSpan.FromMilliseconds(-5)));
        Try(() => Task.Delay(TimeSpan.FromMilliseconds(4294967295.0)));
        Try(() => Task.Delay(TimeSpan.FromMilliseconds(-1.5), CancellationToken.None));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { await Task.Delay(TimeSpan.FromSeconds(30), cts.Token); } catch (OperationCanceledException) { Console.WriteLine("canceled"); }
        var infinite = Task.Delay(Timeout.InfiniteTimeSpan);
        Console.WriteLine(infinite.IsCompleted);
        Console.WriteLine("done");
    }
}
