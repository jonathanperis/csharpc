using System.Runtime.CompilerServices;
using IlFeatureSwitchLib;

Console.WriteLine($"{RuntimeFeature.IsDynamicCodeSupported} {RuntimeFeature.IsDynamicCodeCompiled}");
Console.WriteLine(Probe.Answer());
Console.WriteLine(Probe.Engine());
Console.WriteLine(Probe.Loop(10));
