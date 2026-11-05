using System.Reflection;
using System.Resources;

var resources = new ResourceManager("Repro.Strings", typeof(Marker).Assembly);
Console.WriteLine(resources.GetString("Hello"));
Console.WriteLine(resources.GetString("Multi")!.Replace("\r\n", "\n"));
Console.WriteLine(resources.GetString("Unicode"));
Console.WriteLine(resources.GetString("Missing") is null);

var assembly = typeof(Marker).Assembly;
foreach (var name in assembly.GetManifestResourceNames().Order()) Console.WriteLine(name);
using var stream = assembly.GetManifestResourceStream("Repro.data.bin")!;
using var reader = new StreamReader(stream);
Console.WriteLine(reader.ReadToEnd());

class Marker;
