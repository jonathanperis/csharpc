using System.Net;
using System.Net.NetworkInformation;

// NetworkInterface.GetAllNetworkInterfaces().GetIPProperties().UnicastAddresses: the local addresses
// (System.Net.Http's Windows system proxy lists them for a "<local>" bypass). Machines differ in their
// adapters, but all of them have a loopback interface with 127.0.0.1 and no sane list is empty.
var interfaces = NetworkInterface.GetAllNetworkInterfaces();
bool anyAddress = false, loopbackV4 = false;
foreach (var networkInterface in interfaces)
    foreach (UnicastIPAddressInformation unicast in networkInterface.GetIPProperties().UnicastAddresses)
    {
        anyAddress = true;
        if (unicast.Address.Equals(IPAddress.Loopback)) loopbackV4 = true;
    }
Console.WriteLine($"interfaces: {interfaces.Length > 0}, addresses: {anyAddress}, 127.0.0.1: {loopbackV4}");
