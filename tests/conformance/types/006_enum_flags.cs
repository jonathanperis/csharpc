var p = Perm.Read | Perm.Write;
Console.WriteLine(p);
Console.WriteLine(p.HasFlag(Perm.Write));
Console.WriteLine((p & Perm.Exec) == 0);
Console.WriteLine((int)p);
[Flags] enum Perm { None = 0, Read = 1, Write = 2, Exec = 4 }
