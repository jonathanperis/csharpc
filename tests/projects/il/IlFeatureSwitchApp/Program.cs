using System.Runtime.CompilerServices;
using IlFeatureSwitchLib;

Console.WriteLine($"{RuntimeFeature.IsDynamicCodeSupported} {RuntimeFeature.IsDynamicCodeCompiled}");
Console.WriteLine(Probe.Answer());
Console.WriteLine(Probe.Engine());
Console.WriteLine(Probe.Loop(10));
var bytes = new byte[] { 1, 2, 3, 4, 5 };
Console.WriteLine(Segments.Describe(new ArraySegment<byte>(bytes, 1, 3)) + " " + bytes[1]);
Console.WriteLine(Segments.Describe(default));
