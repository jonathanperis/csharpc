// BCL members .NET's macOS TLS and X.509 code (the Apple backend of System.Net.Security and
// System.Security.Cryptography) relies on, beyond those of 1250: the typed GCHandle<T> (SslStream passes
// one to SecureTransport's read/write callbacks as the connection pointer), the destination overloads of
// SHA1/SHA384/SHA512.HashData, and a generic static abstract interface member called through a type
// parameter (SslStream's IReadWriteAdapter.WaitAsync<T>).
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

interface IAdapter
{
    static abstract ValueTask<T> WaitAsync<T>(ValueTask<T> task);
    static abstract string Name { get; }
}

struct SyncAdapter : IAdapter
{
    public static ValueTask<T> WaitAsync<T>(ValueTask<T> task) => task;
    public static string Name => "sync";
}

struct AsyncAdapter : IAdapter
{
    public static async ValueTask<T> WaitAsync<T>(ValueTask<T> task) => await task.ConfigureAwait(false);
    public static string Name => "async";
}

class Context
{
    public string Label = "ctx";
}

static class Program
{
    static async Task<int> Read<TAdapter>(int value) where TAdapter : IAdapter
    {
        int result = await TAdapter.WaitAsync(new ValueTask<int>(value));
        string text = await TAdapter.WaitAsync(new ValueTask<string>(TAdapter.Name));
        Console.WriteLine($"{text}: {result}");
        return result + 1;
    }

    static void Main()
    {
        var context = new Context();
        var handle = new GCHandle<Context>(context);
        IntPtr pointer = GCHandle<Context>.ToIntPtr(handle);
        Console.WriteLine(handle.IsAllocated);
        var again = GCHandle<Context>.FromIntPtr(pointer);
        Console.WriteLine(again.Target.Label);
        again.Target = new Context { Label = "replaced" };
        Console.WriteLine(handle.Target.Label);
        Console.WriteLine(handle.Equals(again));
        handle.Dispose();

        byte[] data = { 1, 2, 3, 4, 5 };
        Span<byte> sha1 = stackalloc byte[SHA1.HashSizeInBytes];
        Console.WriteLine(SHA1.HashData(data, sha1));
        Console.WriteLine(Convert.ToHexString(sha1));
        Span<byte> sha384 = stackalloc byte[SHA384.HashSizeInBytes];
        Console.WriteLine(SHA384.HashData(data, sha384));
        Console.WriteLine(Convert.ToHexString(sha384));
        Span<byte> sha512 = stackalloc byte[SHA512.HashSizeInBytes];
        Console.WriteLine(SHA512.HashData(data, sha512));
        Console.WriteLine(Convert.ToHexString(sha512));
        Console.WriteLine(SHA1.TryHashData(data, new byte[3], out int written) + " " + written);
        Console.WriteLine(SHA384.TryHashData(data, new byte[48], out written) + " " + written);
        Console.WriteLine(SHA512.TryHashData(data, new byte[10], out written) + " " + written);
        try { SHA1.HashData(data, new byte[3]); }
        catch (ArgumentException e) { Console.WriteLine(e.ParamName + ": " + e.Message.Split('(')[0].Trim()); }

        Console.WriteLine(Read<SyncAdapter>(41).Result);
        Console.WriteLine(Read<AsyncAdapter>(9).Result);
    }
}
