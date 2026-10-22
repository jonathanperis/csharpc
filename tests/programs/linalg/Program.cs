// A generic linear-algebra mini library using .NET generic math (INumber<T>): matrices over int,
// double and decimal, LU decomposition with partial pivoting, determinants, solving and inverses.
using System.Numerics;
using System.Text;

var a = Matrix<double>.From(new double[,] { { 4, -2, 1 }, { -2, 4, -2 }, { 1, -2, 4 } });
var b = new[] { 11.0, -16.0, 17.0 };
Console.WriteLine($"A =\n{a}");
var (lu, perm, sign) = a.Decompose();
Console.WriteLine($"det(A) = {a.Determinant():F6}  (pivot sign {sign}, perm [{string.Join(",", perm)}])");
var x = a.Solve(b);
Console.WriteLine($"x = [{string.Join(", ", x.Select(v => v.ToString("F6")))}]");
Console.WriteLine($"residual = {a.Multiply(x).Zip(b, (p, q) => Math.Abs(p - q)).Max():E2}");
var inv = a.Inverse();
Console.WriteLine($"A^-1 =\n{inv.Map(v => Math.Round(v, 6))}");
Console.WriteLine($"A * A^-1 == I ? {(a * inv).Map(v => Math.Round(v, 9)).Equals(Matrix<double>.Identity(3))}");

var m = Matrix<int>.From(new[,] { { 1, 2 }, { 3, 4 } });
var power = Matrix<int>.Identity(2);
for (int i = 0; i < 10; i++) power *= m;
Console.WriteLine($"[[1,2],[3,4]]^10 =\n{power}");
Console.WriteLine($"trace = {power.Trace()}, sum = {Sum(power.Row(0))}");

var fib = Matrix<long>.From(new long[,] { { 1, 1 }, { 1, 0 } });
var f = Matrix<long>.Identity(2);
for (int i = 0; i < 90; i++) f *= fib;
Console.WriteLine($"fib(90) via matrix power = {f[0, 1]}");

var money = Matrix<decimal>.From(new decimal[,] { { 1.10m, 2.20m }, { 3.30m, 4.40m } });
Console.WriteLine($"decimal matrix * 3 =\n{money.Scale(3m)}");
Console.WriteLine($"decimal det = {money.Determinant()}");

var singular = Matrix<double>.From(new double[,] { { 1, 2 }, { 2, 4 } });
try { singular.Solve([1, 2]); }
catch (InvalidOperationException e) { Console.WriteLine($"singular: {e.Message}"); }
Console.WriteLine($"dot = {Dot<int>([1, 2, 3], [4, 5, 6])}, dot(decimal) = {Dot<decimal>([0.5m, 0.25m], [2m, 4m])}");

static T Sum<T>(IEnumerable<T> values) where T : INumber<T>
{
    var total = T.Zero;
    foreach (var v in values) total += v;
    return total;
}

static T Dot<T>(T[] u, T[] v) where T : INumber<T>
{
    if (u.Length != v.Length) throw new ArgumentException("length mismatch");
    var acc = T.Zero;
    for (int i = 0; i < u.Length; i++) acc += u[i] * v[i];
    return acc;
}

sealed class Matrix<T> : IEquatable<Matrix<T>> where T : INumber<T>
{
    private readonly T[,] _data;
    public int Rows { get; }
    public int Cols { get; }

    private Matrix(int rows, int cols) { Rows = rows; Cols = cols; _data = new T[rows, cols]; }

    public static Matrix<T> From(T[,] values)
    {
        var m = new Matrix<T>(values.GetLength(0), values.GetLength(1));
        for (int i = 0; i < m.Rows; i++)
            for (int j = 0; j < m.Cols; j++) m._data[i, j] = values[i, j];
        return m;
    }

    public static Matrix<T> Identity(int n)
    {
        var m = new Matrix<T>(n, n);
        for (int i = 0; i < n; i++) m._data[i, i] = T.One;
        return m;
    }

    public T this[int r, int c] => _data[r, c];
    public IEnumerable<T> Row(int r) { for (int c = 0; c < Cols; c++) yield return _data[r, c]; }

    public T Trace()
    {
        var t = T.Zero;
        for (int i = 0; i < Math.Min(Rows, Cols); i++) t += _data[i, i];
        return t;
    }

    public Matrix<T> Map(Func<T, T> f)
    {
        var m = new Matrix<T>(Rows, Cols);
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++) m._data[i, j] = f(_data[i, j]);
        return m;
    }

    public Matrix<T> Scale(T k) => Map(v => v * k);

    public static Matrix<T> operator *(Matrix<T> a, Matrix<T> b)
    {
        if (a.Cols != b.Rows) throw new ArgumentException("dimension mismatch");
        var m = new Matrix<T>(a.Rows, b.Cols);
        for (int i = 0; i < a.Rows; i++)
            for (int j = 0; j < b.Cols; j++)
            {
                var s = T.Zero;
                for (int k = 0; k < a.Cols; k++) s += a._data[i, k] * b._data[k, j];
                m._data[i, j] = s;
            }
        return m;
    }

    public T[] Multiply(T[] v)
    {
        var r = new T[Rows];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++) r[i] += _data[i, j] * v[j];
        return r;
    }

    public (Matrix<T> LU, int[] Perm, int Sign) Decompose()
    {
        if (Rows != Cols) throw new InvalidOperationException("matrix must be square");
        int n = Rows;
        var lu = Map(v => v);
        var perm = Enumerable.Range(0, n).ToArray();
        int sign = 1;
        for (int k = 0; k < n; k++)
        {
            int pivot = k;
            for (int i = k + 1; i < n; i++)
                if (T.Abs(lu._data[i, k]) > T.Abs(lu._data[pivot, k])) pivot = i;
            if (T.IsZero(lu._data[pivot, k])) throw new InvalidOperationException("matrix is singular");
            if (pivot != k)
            {
                for (int j = 0; j < n; j++) (lu._data[k, j], lu._data[pivot, j]) = (lu._data[pivot, j], lu._data[k, j]);
                (perm[k], perm[pivot]) = (perm[pivot], perm[k]);
                sign = -sign;
            }
            for (int i = k + 1; i < n; i++)
            {
                lu._data[i, k] /= lu._data[k, k];
                for (int j = k + 1; j < n; j++) lu._data[i, j] -= lu._data[i, k] * lu._data[k, j];
            }
        }
        return (lu, perm, sign);
    }

    public T Determinant()
    {
        try
        {
            var (lu, _, sign) = Decompose();
            var det = sign > 0 ? T.One : -T.One;
            for (int i = 0; i < Rows; i++) det *= lu._data[i, i];
            return det;
        }
        catch (InvalidOperationException) { return T.Zero; }
    }

    public T[] Solve(T[] b)
    {
        var (lu, perm, _) = Decompose();
        int n = Rows;
        var y = new T[n];
        for (int i = 0; i < n; i++)
        {
            var s = b[perm[i]];
            for (int j = 0; j < i; j++) s -= lu._data[i, j] * y[j];
            y[i] = s;
        }
        var x = new T[n];
        for (int i = n - 1; i >= 0; i--)
        {
            var s = y[i];
            for (int j = i + 1; j < n; j++) s -= lu._data[i, j] * x[j];
            x[i] = s / lu._data[i, i];
        }
        return x;
    }

    public Matrix<T> Inverse()
    {
        var inv = new Matrix<T>(Rows, Cols);
        for (int c = 0; c < Cols; c++)
        {
            var e = new T[Rows];
            e[c] = T.One;
            var col = Solve(e);
            for (int r = 0; r < Rows; r++) inv._data[r, c] = col[r];
        }
        return inv;
    }

    public bool Equals(Matrix<T>? other)
    {
        if (other is null || other.Rows != Rows || other.Cols != Cols) return false;
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                if (_data[i, j] != other._data[i, j]) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is Matrix<T> m && Equals(m);
    public override int GetHashCode() => HashCode.Combine(Rows, Cols);

    public override string ToString()
    {
        var cells = new string[Rows, Cols];
        int width = 0;
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                width = Math.Max(width, (cells[i, j] = _data[i, j].ToString()!).Length);
        var sb = new StringBuilder();
        for (int i = 0; i < Rows; i++)
        {
            sb.Append("  [");
            for (int j = 0; j < Cols; j++) sb.Append(j > 0 ? " " : "").Append(cells[i, j].PadLeft(width));
            sb.Append(']');
            if (i < Rows - 1) sb.Append('\n');
        }
        return sb.ToString();
    }
}
