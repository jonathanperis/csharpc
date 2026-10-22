static int L(string s, int v) { Console.Write(s); return v; }
static void M(int x, int y, int z) => Console.WriteLine($" {x}{y}{z}");
M(z: L("z", 3), x: L("x", 1), y: L("y", 2));
