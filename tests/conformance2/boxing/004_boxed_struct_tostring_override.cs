object o = new S(3);
Console.WriteLine(o);
Console.WriteLine(o.ToString());
Console.WriteLine($"{o}");
Console.WriteLine(o.GetHashCode() == new S(3).GetHashCode());
readonly struct S(int v) { public override string ToString() => $"S<{v}>"; public override int GetHashCode() => v * 31; }
