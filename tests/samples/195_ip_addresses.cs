// IPAddress/IPEndPoint parsing and formatting as .NET: inet_aton-style IPv4 input (127.1, hex,
// octal), RFC 5952 IPv6 output (zero compression, embedded IPv4), scope ids, mapping.
using System.Net;
using System.Net.Sockets;
using System.Text;

foreach (var s in new[] { "127.0.0.1", "127.1", "0x7f.1", "010.1.2.3", "1", "::1", "::", "fe80::1%3", "2001:db8:0:0:1:0:0:1", "2001:0db8:0000:0000:0000:ff00:0042:8329", "::ffff:192.168.1.1", "::192.168.1.1", "1::", "1:2:3:4:5:6:7:8", "256.1.1.1", "1.2.3.4.5", "1::2::3", "[::1]", "abc", "" })
{
    if (IPAddress.TryParse(s, out IPAddress? a)) Console.WriteLine($"{s,-42} -> {a} {a.AddressFamily}");
    else Console.WriteLine($"{s,-42} -> invalid");
}
Console.WriteLine(IPAddress.Loopback.MapToIPv6());
Console.WriteLine(IPAddress.Parse("::ffff:10.0.0.1").MapToIPv4());
Console.WriteLine(new IPEndPoint(IPAddress.Parse("::1"), 80));
Console.WriteLine(IPEndPoint.Parse("10.1.2.3:443"));
Console.WriteLine(IPAddress.IsLoopback(IPAddress.Parse("127.5.5.5")) + " " + IPAddress.IsLoopback(IPAddress.IPv6Loopback));
Console.WriteLine(string.Join(",", IPAddress.Parse("10.20.30.40").GetAddressBytes()));
Console.WriteLine(IPAddress.HostToNetworkOrder(0x01020304).ToString("X"));

