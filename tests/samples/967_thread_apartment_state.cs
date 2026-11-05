// Thread's apartment state off Windows (COM Interop is a Windows feature): the getter reports Unknown, setting STA
// or MTA is a PlatformNotSupportedException, Unknown is accepted, out-of-range values are rejected, and
// TrySetApartmentState answers instead of throwing. Real .NET on Windows really sets the state, and WebAssembly
// has no threads, so the sample is recorded for Linux and macOS only.
using System;
using System.Threading;

class Program
{
    static void Show(string name, Func<object> f)
    {
        try { Console.WriteLine(name + ": " + f()); }
        catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static void Main()
    {
        var t = new Thread(() => { });
        Show("apartment get", () => t.GetApartmentState());
        Show("apartment STA", () => { t.SetApartmentState(ApartmentState.STA); return "ok"; });
        Show("apartment MTA", () => { t.SetApartmentState(ApartmentState.MTA); return "ok"; });
        Show("apartment Unknown", () => { t.SetApartmentState(ApartmentState.Unknown); return "ok"; });
        Show("apartment bad", () => { t.SetApartmentState((ApartmentState)5); return "ok"; });
        Show("try STA", () => t.TrySetApartmentState(ApartmentState.STA));
        Show("try Unknown", () => t.TrySetApartmentState(ApartmentState.Unknown));
        Show("try bad", () => t.TrySetApartmentState((ApartmentState)7));
        t.Start();
        t.Join();
        Show("apartment after start", () => { t.SetApartmentState(ApartmentState.MTA); return "ok"; });
        Show("apartment current", () => Thread.CurrentThread.GetApartmentState());
    }
}
