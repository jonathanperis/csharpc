// IPNetwork (parsing, host bits cleared, Contains with IPv4-mapped addresses, equality), atomics on
// bool fields, and an abstract override of ToString between two classes.
#nullable disable
using System;
using System.Net;
using System.Threading;

abstract class Shape
{
    public abstract override string ToString();
}

sealed class Square : Shape
{
    public override string ToString() => "square";
}

class Program
{
    static bool s_flag;

    static void Main()
    {
        var network = IPNetwork.Parse("192.168.17.42/16");
        Console.WriteLine($"{network} {network.BaseAddress} {network.PrefixLength}");
        Console.WriteLine($"{network.Contains(IPAddress.Parse("192.168.200.1"))} {network.Contains(IPAddress.Parse("192.169.0.1"))} {network.Contains(IPAddress.Parse("::ffff:192.168.1.1"))} {network.Contains(IPAddress.IPv6Loopback)}");
        var v6 = new IPNetwork(IPAddress.Parse("2001:db8::1"), 32);
        Console.WriteLine($"{v6} {v6.Contains(IPAddress.Parse("2001:db8:ffff::"))} {v6.Contains(IPAddress.Parse("2001:db9::"))}");
        Console.WriteLine($"{IPNetwork.TryParse("10.0.0.0/33", out _)} {IPNetwork.TryParse("10.0.0.0", out _)} {IPNetwork.TryParse("10.0.0.0/8", out var ten)} {ten}");
        Console.WriteLine($"{network == IPNetwork.Parse("192.168.0.0/16")} {network.Equals(ten)} {new IPNetwork(IPAddress.Any, 0)} {default(IPNetwork)}");
        try { IPNetwork.Parse("bad"); } catch (FormatException e) { Console.WriteLine(e.Message); }
        try { new IPNetwork(IPAddress.Loopback, 40); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

        Console.WriteLine($"{Interlocked.Exchange(ref s_flag, true)} {s_flag} {Interlocked.CompareExchange(ref s_flag, false, true)} {s_flag}");

        Shape shape = new Square();
        Console.WriteLine($"{shape} {shape.ToString().Length}");
    }
}
