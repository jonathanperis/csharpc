using System.Collections.Immutable;
using IlResourceKeysLib;

Console.WriteLine(AppContext.TryGetSwitch("System.Resources.UseSystemResourceKeys", out var on) ? on.ToString() : "unset");
Console.WriteLine(Messages.UsingKeys());
Console.WriteLine(Messages.Empty());
Console.WriteLine(Messages.TooMany(3));
try { Messages.Throw(); }
catch (InvalidOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
// A runtime library's own SR message (System.Collections.Immutable, compiled from IL).
try { ImmutableQueue<int>.Empty.Dequeue(); }
catch (InvalidOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
