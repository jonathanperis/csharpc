// IPAddress, IPEndPoint and IPNetwork parsing of untrusted text, as System.Net.Primitives does it: inet_aton forms and their
// limits, IPv6 compression, embedded IPv4 and scope ids (a scope that is no number and no interface is dropped, not an error),
// brackets and ports, whitespace, the span and UTF-8 overloads, and the RFC 5952 / ISATAP formatting of what was parsed.
using System.Net;
using System.Text;

string[] addresses =
[
    "1.2.3.4", " 1.2.3.4", "1.2.3.4 ", "1.2.3.04", "1.2.3.08", "0x100.1", "0xff.0xffffff", "4294967295", "4294967296",
    "1.16777215", "1.16777216", "1.2.65535", "1.2.65536", "1..2", "1.2.3.", ".1.2.3", "0x", "00", "1.2.3.4.5",
    "::", "::1", "1::", "1:2:3:4:5:6:7:8", "1:2:3:4:5:6:7::", "::1:2:3:4:5:6:7", "1:2:3:4:5:6:7:8:9", "1::2::3", ":1::2", "1::2:",
    "12345::1", "g::1", "2001:DB8::ABCD", "0:0:0:0:0:0:0:1", "::0.0.0.1", "::1.2.3.4", "::ffff:1.2.3.4", "::ffff:0:1.2.3.4",
    "1:2:3:4:5:6:1.2.3.4", "1::1.2.3", "1::1.2.3.256", "::5efe:1.2.3.4", "64:ff9b::1.2.3.4", "fe80::1%2", "fe80::1%2%3",
    "fe80::1%", "fe80::1%nosuchif0", "fe80::1%4294967295", "fe80::1%4294967296", "fe80::1%-1", "::ffff:1.2.3.4%7",
    "[::1]", "[::1]:80", "[::1]x", "[::1", "::1]", "[1.2.3.4]", "", " ",
];
foreach (var text in addresses)
{
    bool ok = IPAddress.TryParse(text, out IPAddress? address);
    bool span = IPAddress.TryParse(text.AsSpan(), out IPAddress? fromSpan);
    bool utf8 = IPAddress.TryParse(Encoding.UTF8.GetBytes(text), out IPAddress? fromUtf8);
    string shown = ok ? $"{address} {address!.AddressFamily}{(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? " scope " + address.ScopeId : "")}" : "invalid";
    Console.WriteLine($"[{text}] -> {shown}{(span == ok && Equals(fromSpan, address) ? "" : " (span differs)")}{(utf8 == ok && Equals(fromUtf8, address) ? "" : " (utf8 differs)")}");
}

try { IPAddress.Parse("1.2.3"[..2] + "x"); } catch (FormatException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { IPAddress.Parse((string)null!); } catch (ArgumentNullException e) { Console.WriteLine($"{e.GetType().Name}: {e.ParamName}"); }

string[] endPoints = ["1.2.3.4:80", "1.2.3.4:", "1.2.3.4:65535", "1.2.3.4:65536", "1.2.3.4:+80", "1.2.3.4: 80", "1.2.3.4:080", "[::1]:443", "[::1]", "::1", "::1:80", "[::1]:", "1.2.3.4:-1", "host:80"];
foreach (var text in endPoints)
    Console.WriteLine($"[{text}] -> {(IPEndPoint.TryParse(text, out var endPoint) ? endPoint.ToString() : "invalid")}");
try { IPEndPoint.Parse("1.2.3.4:x"); } catch (FormatException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

string[] networks = ["10.0.0.0/8", "10.1.2.3/8", "10.0.0.0/08", "10.0.0.0/ 8", "10.0.0.0/+8", "10.0.0.0/33", "10.0.0.0/-1", "::/0", "2001:db8::/129", "2001:db8::1/64", "10.0.0.0", "/8", "10.0.0.0/8/8"];
foreach (var text in networks)
    Console.WriteLine($"[{text}] -> {(IPNetwork.TryParse(text, out var network) ? network.ToString() : "invalid")}");

// Formatting into spans: exact sizes, one char short, and UTF-8.
var v6 = IPAddress.Parse("2001:db8::abcd%5");
Span<char> chars = stackalloc char[64];
Console.WriteLine($"{v6.TryFormat(chars, out int written)} {chars[..written].ToString()} {v6.TryFormat(chars[..(written - 1)], out int none)} {none}");
Span<byte> bytes = stackalloc byte[64];
Console.WriteLine($"{v6.TryFormat(bytes, out int bytesWritten)} {Encoding.UTF8.GetString(bytes[..bytesWritten])}");
var net = IPNetwork.Parse("192.168.0.0/16");
Console.WriteLine($"{net.TryFormat(chars, out written)} {chars[..written].ToString()} {net.TryFormat(chars[..5], out written)} {written}");
Console.WriteLine($"{IPAddress.Parse("10.0.0.1").Equals(IPAddress.Parse("10.0.0.1"))} {IPAddress.Parse("10.0.0.1").GetHashCode() == IPAddress.Parse("10.0.0.1").GetHashCode()} {IPAddress.Parse("fe80::1%1").Equals(IPAddress.Parse("fe80::1%2"))}");
