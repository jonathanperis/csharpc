// A framework struct that does not override ToString formats as its full type name (ValueType.ToString), in interpolation,
// string.Concat and ToString alike.
using System;
using System.Threading;

var token = CancellationToken.None;
Console.WriteLine($"token: {token}");
Console.WriteLine("concat: " + token);
Console.WriteLine($"ToString: {token.ToString()}");
var registration = default(CancellationTokenRegistration);
Console.WriteLine($"registration: {registration}");
var handle = default(RuntimeTypeHandle);
Console.WriteLine($"handle: {handle}");
