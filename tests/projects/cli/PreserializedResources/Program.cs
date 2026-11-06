using System.Resources;

// Strings next to non-string entries of the same .resources file.
var resources = new ResourceManager("Repro.Strings", typeof(Marker).Assembly);
Console.WriteLine(resources.GetString("Hello"));
Console.WriteLine(resources.GetObject("Hello"));
Console.WriteLine(resources.GetString("Last"));
Console.WriteLine(resources.GetString("Missing") is null);
Console.WriteLine(resources.GetObject("Missing") is null);
Console.WriteLine(resources.GetObject("Count"));
// The program never names System.Drawing: the type comes from the .resx (its TypeConverter creates the Point).
var location = resources.GetObject("Location");
Console.WriteLine(location);
Console.WriteLine(location?.GetType());
try { resources.GetString("Location"); }
catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { resources.GetString("Count"); }
catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

class Marker;
