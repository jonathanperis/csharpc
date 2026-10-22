// Spectral norm (benchmarks-game), matrix-vector products split across cores with Parallel.For.
using System.Threading.Tasks;

const int N = 3000;
var u = new double[N];
var v = new double[N];
var tmp = new double[N];
for (int i = 0; i < N; i++) u[i] = 1.0;
for (int i = 0; i < 10; i++)
{
    MultiplyAtAv(u, v, tmp);
    MultiplyAtAv(v, u, tmp);
}
double vBv = 0, vv = 0;
for (int i = 0; i < N; i++) { vBv += u[i] * v[i]; vv += v[i] * v[i]; }
Console.WriteLine(Math.Sqrt(vBv / vv).ToString("F9"));

static double A(int i, int j) => 1.0 / ((i + j) * (i + j + 1) / 2 + i + 1);

static void MultiplyAv(double[] v, double[] av)
{
    Parallel.For(0, av.Length, i =>
    {
        double sum = 0;
        for (int j = 0; j < v.Length; j++) sum += A(i, j) * v[j];
        av[i] = sum;
    });
}

static void MultiplyAtv(double[] v, double[] atv)
{
    Parallel.For(0, atv.Length, i =>
    {
        double sum = 0;
        for (int j = 0; j < v.Length; j++) sum += A(j, i) * v[j];
        atv[i] = sum;
    });
}

static void MultiplyAtAv(double[] v, double[] atav, double[] tmp)
{
    MultiplyAv(v, tmp);
    MultiplyAtv(tmp, atav);
}
