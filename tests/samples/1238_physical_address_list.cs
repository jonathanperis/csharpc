using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;

// System.Net.NetworkInformation mixes csharpc's own NetworkInterface (a corelib shadow) with plain IL types:
// once the assembly had a shadow, PhysicalAddress was no longer compiled from IL and any generic code over it
// ("type test against 'System.Net.NetworkInformation.PhysicalAddress'") could not be built.
class Program
{
    static void Main()
    {
        PhysicalAddress mac = PhysicalAddress.Parse("00-11-22-AA-BB-CC");
        Console.WriteLine(mac);
        Console.WriteLine(BitConverter.ToString(mac.GetAddressBytes()));

        var list = new List<PhysicalAddress> { mac, PhysicalAddress.None };
        IList untyped = list;
        untyped[1] = PhysicalAddress.Parse("AABBCCDDEEFF");
        Console.WriteLine(list[1]);
        Console.WriteLine(untyped.Contains(mac) + " " + untyped.IndexOf(PhysicalAddress.Parse("AABBCCDDEEFF")));
        try { untyped[0] = "not an address"; }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }

        object boxed = mac;
        Console.WriteLine(boxed is PhysicalAddress p && p.Equals(mac));
        Console.WriteLine(mac.Equals(PhysicalAddress.Parse("001122AABBCC")) + " " + (mac.GetHashCode() == PhysicalAddress.Parse("001122AABBCC").GetHashCode()));
        Console.WriteLine(PhysicalAddress.None.GetAddressBytes().Length);
    }
}
