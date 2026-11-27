// System.Net.Primitives throughput, the program behind the NativeAOT column of docs/il-first/System.Net.Primitives.md
// (tools/il-first/measure-aot.sh): IPAddress parsing and formatting (v4 and v6), IPEndPoint, IPNetwork.Contains. The output is checksums only.
using System;
using System.Net;

long check = 0;
var network = IPNetwork.Parse("10.0.0.0/8");
for (int i = 0; i < 2_000_000; i++)
{
    var v4 = IPAddress.Parse($"10.{i & 255}.{(i >> 8) & 255}.{(i >> 16) & 255}");
    check += v4.ToString().Length + (network.Contains(v4) ? 1 : 0);
    var v6 = IPAddress.Parse($"2001:db8::{i & 0xffff:x}:{(i >> 16) & 0xffff:x}");
    check += v6.ToString().Length;
    check += new IPEndPoint(v4, i & 0xffff).ToString().Length;
}
Console.WriteLine(check);
