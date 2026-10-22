var m = new Matrix(2, 2);
m[0, 1] = 5; m[1, 0] = 7;
Console.WriteLine($"{m[0, 1]} {m[1, 0]} {m["0,1"]} {m[1]}");
class Matrix(int r, int c) {
    double[] d = new double[r * c];
    public double this[int i, int j] { get => d[i * c + j]; set => d[i * c + j] = value; }
    public double this[string key] { get { var p = key.Split(','); return this[int.Parse(p[0]), int.Parse(p[1])]; } }
    public double this[int row] => Enumerable.Range(0, c).Sum(j => this[row, j]);
}
