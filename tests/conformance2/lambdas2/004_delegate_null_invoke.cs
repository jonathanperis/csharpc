Action? a = null;
a?.Invoke();
Console.WriteLine("no crash");
try { a!(); } catch (NullReferenceException) { Console.WriteLine("NRE"); }
Func<int>? f = null;
Console.WriteLine(f?.Invoke() ?? -1);
