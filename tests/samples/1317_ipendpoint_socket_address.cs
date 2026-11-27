// IPEndPoint.Serialize/Create and SocketAddress over csharpc's struct sockaddr layout (System.Net.Primitives' SocketAddressPal
// contract): sizes, ToString (which shows the bytes after the family, the same on every OS), round trips, equality and the errors
// of a wrong family or a short buffer.
using System.Net;
using System.Net.Sockets;

var v4 = new IPEndPoint(IPAddress.Parse("10.1.2.3"), 8080);
var a4 = v4.Serialize();
Console.WriteLine($"{a4} {a4.Family} {a4.Size} {a4[2]} {a4[3]} {a4[4]} {a4[7]}");
Console.WriteLine($"{v4.Create(a4)} {v4.Create(a4).Equals(v4)}");

var v6 = new IPEndPoint(IPAddress.Parse("fe80::1%3"), 443);
var a6 = v6.Serialize();
Console.WriteLine($"{a6} {a6.Family} {a6.Size}");
var back = (IPEndPoint)v6.Create(a6);
Console.WriteLine($"{back} {back.Address.ScopeId} {back.Equals(v6)}");

var built = new SocketAddress(AddressFamily.InterNetwork);
built[2] = 0x01; built[3] = 0xBB; built[4] = 127; built[7] = 1;
Console.WriteLine($"{built} {new IPEndPoint(IPAddress.Any, 0).Create(built)}");
Console.WriteLine($"{a4.Equals(v4.Serialize())} {a4.GetHashCode() == v4.Serialize().GetHashCode()} {a4.Equals(built)}");
Console.WriteLine($"{new SocketAddress(AddressFamily.InterNetwork).Size} {new SocketAddress(AddressFamily.InterNetworkV6).Size} {new SocketAddress(AddressFamily.InterNetwork, 64).Size}");

var mapped = new IPEndPoint(IPAddress.Parse("::ffff:192.168.1.1"), 1);
Console.WriteLine($"{mapped.Serialize()} {mapped.Create(mapped.Serialize())}");

try { v4.Create(a6); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { v6.Create(a4); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { new SocketAddress(AddressFamily.InterNetwork, 1); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { _ = a4[16]; } catch (Exception e) { Console.WriteLine(e.GetType().Name); }
try { new EndPointProbe().Serialize(); } catch (Exception e) { Console.WriteLine(e.GetType().Name); }

sealed class EndPointProbe : EndPoint { }
