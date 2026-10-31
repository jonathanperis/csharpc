using IlResources;

Console.WriteLine(Catalog.Greeting);
Console.WriteLine(Catalog.Unicode);
Console.WriteLine(Catalog.Empty);
Console.WriteLine(Catalog.Missing);
Console.WriteLine(Catalog.CaseInsensitive());
Catalog.Order("apples", 2);
try { Catalog.Order("pears", 7); }
catch (InvalidOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
Console.WriteLine(Catalog.Verbose);
Console.WriteLine(Catalog.Limit);
Console.WriteLine(AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant) ? invariant.ToString() : "unset");

foreach (var name in RawResources.Names()) Console.WriteLine(RawResources.Describe(name));
Console.WriteLine(RawResources.Describe("IlResources.missing.dat"));
Console.WriteLine(RawResources.ByType());
