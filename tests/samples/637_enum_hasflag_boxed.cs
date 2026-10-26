// Enum.HasFlag when the flag is boxed after a branch merge (Unboxed metadata lost across blocks).
bool local = true;
Enum scopes = Scope.Local | Scope.Global;
Enum flag = local ? Scope.Local : Scope.Global;
Console.WriteLine(scopes.HasFlag(flag));
Console.WriteLine(((Enum)(object)Scope.Local).HasFlag((Enum)(object)Scope.Global));
try { ((Enum)(object)Scope.Local).HasFlag(null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
try { ((Enum)(object)Scope.Local).HasFlag((Enum)(object)Other.A); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }

[Flags] enum Scope { None = 0, Local = 1, Global = 2, All = Local | Global }
enum Other { A = 1 }
