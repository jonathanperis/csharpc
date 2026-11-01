using System;
using System.Threading.Tasks;

namespace IlStackTracesNoPdbLib;

public sealed class Driver
{
    public string Name { get; }

    public Driver(string name) => Name = name;

    public async Task OpenAsync(bool fail)
    {
        await Task.Yield();
        await ConnectAsync(fail);
    }

    private async ValueTask ConnectAsync(bool fail)
    {
        try { await HandshakeAsync(fail); }
        catch (InvalidOperationException e) { throw new ApplicationException("could not open " + Name, e); }
    }

    private static async Task HandshakeAsync(bool fail)
    {
        await Task.Delay(1);
        if (fail) Fail();
    }

    private static void Fail() => throw new InvalidOperationException("handshake failed");

    public T Convert<T>(Func<string, T> convert) => convert(Name);
}
