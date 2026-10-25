// Library members Kestrel's request path and start-up reach: PeriodicTimer, ExecutionContext.SuppressFlow,
// MemoryMarshal.TryGetArray over Memory, enum underlying types through reflection, non-generic array
// enumeration, HashCode.Combine of eight values, span trims and range searches, Uri.TryUnescapeDataString,
// Environment.Version / FrameworkDescription, Thread.Sleep(TimeSpan) and SslApplicationProtocol.
#nullable disable
using System;
using System.Collections;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

enum Small : short { A = 1 }

class Program
{
    static readonly AsyncLocal<int> Local = new AsyncLocal<int>();

    static async Task Main()
    {
        using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20)))
            Console.WriteLine($"{await timer.WaitForNextTickAsync()} {timer.Period.TotalMilliseconds}");
        var slow = new PeriodicTimer(TimeSpan.FromHours(1));
        var pending = slow.WaitForNextTickAsync();
        try { _ = slow.WaitForNextTickAsync(); } catch (InvalidOperationException) { Console.WriteLine("one waiter"); }
        slow.Dispose();
        Console.WriteLine($"{await pending} {await slow.WaitForNextTickAsync()}");
        try { new PeriodicTimer(TimeSpan.Zero); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

        Local.Value = 5;
        int seen = -1;
        using (ExecutionContext.SuppressFlow())
        {
            Console.WriteLine($"{ExecutionContext.IsFlowSuppressed()} {ExecutionContext.Capture() == null} {Local.Value}");
            var t = new Thread(() => seen = Local.Value);
            t.Start();
            t.Join();
        }
        Console.WriteLine($"{seen} {ExecutionContext.IsFlowSuppressed()}");
        try { ExecutionContext.RestoreFlow(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

        var array = new byte[10];
        Memory<byte> slice = array.AsMemory(3, 4);
        Console.WriteLine($"{MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)slice, out ArraySegment<byte> segment)} {ReferenceEquals(segment.Array, array)} {segment.Offset} {segment.Count}");
        Console.WriteLine(MemoryMarshal.TryGetArray("text".AsMemory(), out ArraySegment<char> _));

        Console.WriteLine($"{typeof(Small).GetEnumUnderlyingType()} {typeof(DayOfWeek).GetEnumUnderlyingType()} {typeof(Small).GetElementType() == null}");
        try { typeof(int).GetEnumUnderlyingType(); } catch (ArgumentException e) { Console.WriteLine(e.Message); }

        Array numbers = new[] { 1, 2, 3 };
        IEnumerator e1 = numbers.GetEnumerator();
        int sum = 0;
        while (e1.MoveNext()) sum += (int)e1.Current;
        Console.WriteLine(sum);

        Console.WriteLine(HashCode.Combine(1, 2, 3, 4, 5, 6, 7, 8) == HashCode.Combine(1, 2, 3, 4, 5, 6, 7, 8));
        Console.WriteLine($"[{"--ab--".AsSpan().Trim("-".AsSpan()).ToString()}] [{"xxaxx".AsSpan().TrimStart("x").ToString()}] [{"ab..".AsSpan().TrimEnd(".").ToString()}]");
        Console.WriteLine($"{"abc".AsSpan().ContainsAnyExceptInRange('a', 'z')} {"ab1".AsSpan().ContainsAnyExceptInRange('a', 'z')} {"ABC".AsSpan().ContainsAnyInRange('a', 'z')}");

        Span<char> unescaped = stackalloc char[16];
        Console.WriteLine($"{Uri.TryUnescapeDataString("a%20b%2Fc", unescaped, out int written)} {new string(unescaped.Slice(0, written))} {Uri.TryUnescapeDataString("abcdef", unescaped.Slice(0, 3), out written)} {written}");

        Console.WriteLine($"{Environment.Version.Major} {RuntimeInformation.FrameworkDescription.StartsWith(".NET " + Environment.Version.Major + ".")}");
        Thread.Sleep(TimeSpan.FromMilliseconds(1));
        try { Thread.Sleep(TimeSpan.FromMilliseconds(-2)); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

        Console.WriteLine($"{SslApplicationProtocol.Http11} {SslApplicationProtocol.Http2} {new SslApplicationProtocol("custom")} {SslApplicationProtocol.Http2 == new SslApplicationProtocol("h2")} {new SslApplicationProtocol(new byte[] { 0xff, 0x01 })}");

    }
}
