var v = new Vec3();
v[0] = 1; v[1] = 2; v[2] = 3;
Console.WriteLine(v[0] + v[1] + v[2]);
var g = new Grid();
Console.WriteLine(g[2, 3]);
class Vec3 { double[] d = new double[3]; public double this[int i] { get => d[i]; set => d[i] = value; } }
class Grid { public int this[int r, int c] => r * 10 + c; }
