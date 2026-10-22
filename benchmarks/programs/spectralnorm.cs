// spectral-norm — Computer Language Benchmarks Game. Tight double loops over arrays.
int n = 1500;
double[] u = new double[n];
double[] v = new double[n];
double[] tmp = new double[n];
for (int i = 0; i < n; i++) u[i] = 1.0;
for (int i = 0; i < 10; i++)
{
    MultiplyAtAv(n, u, v, tmp);
    MultiplyAtAv(n, v, u, tmp);
}
double vBv = 0, vv = 0;
for (int i = 0; i < n; i++)
{
    vBv += u[i] * v[i];
    vv += v[i] * v[i];
}
Console.WriteLine(Math.Sqrt(vBv / vv));

static double A(int i, int j) => 1.0 / ((i + j) * (i + j + 1) / 2 + i + 1);

static void MultiplyAv(int n, double[] v, double[] av)
{
    for (int i = 0; i < n; i++)
    {
        double sum = 0;
        for (int j = 0; j < n; j++) sum += A(i, j) * v[j];
        av[i] = sum;
    }
}

static void MultiplyAtv(int n, double[] v, double[] atv)
{
    for (int i = 0; i < n; i++)
    {
        double sum = 0;
        for (int j = 0; j < n; j++) sum += A(j, i) * v[j];
        atv[i] = sum;
    }
}

static void MultiplyAtAv(int n, double[] v, double[] atav, double[] tmp)
{
    MultiplyAv(n, v, tmp);
    MultiplyAtv(n, tmp, atav);
}
