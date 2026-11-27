// System.Net.Primitives without sockets: IP addresses, end points and networks parsed from text and printed.
using System.Net;

foreach (var text in new[] { "192.168.1.10", "10.1", "::1", "2001:db8::8a2e:370:7334", "::ffff:10.0.0.1", "not an address" })
    Console.WriteLine(IPAddress.TryParse(text, out var address) ? $"{address} {address.AddressFamily}" : $"invalid: {text}");
var endPoint = IPEndPoint.Parse("[2001:db8::1]:8080");
Console.WriteLine($"{endPoint} {endPoint.Port}");
var network = IPNetwork.Parse("10.0.0.0/8");
Console.WriteLine($"{network} {network.Contains(IPAddress.Parse("10.20.30.40"))}");
