// csharpc runs static initializers before Main; only test a type whose initializer succeeds.
Console.WriteLine(Cfg.Value);
static class Cfg { public static string Value = string.Concat("a", "b"); }
