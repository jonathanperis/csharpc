Console.WriteLine(Check("ok"));
try { Check(null); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
static string Check(string? s) => s ?? throw new ArgumentNullException(nameof(s));
