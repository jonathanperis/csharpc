// Dense matrix multiply on jagged double[][] (transposed second operand).
int n = 400;
double[][] a = Generate(n);
double[][] b = Generate(n);
double[][] c = Multiply(n, a, b);
Console.WriteLine(c[n / 2][n / 2]);

static double[][] Generate(int n)
{
    double tmp = 1.0 / n / n;
    double[][] m = new double[n][];
    for (int i = 0; i < n; i++)
    {
        m[i] = new double[n];
        for (int j = 0; j < n; j++) m[i][j] = tmp * (i - j) * (i + j);
    }
    return m;
}

static double[][] Multiply(int n, double[][] a, double[][] b)
{
    double[][] bt = new double[n][];
    for (int i = 0; i < n; i++)
    {
        bt[i] = new double[n];
        for (int j = 0; j < n; j++) bt[i][j] = b[j][i];
    }
    double[][] c = new double[n][];
    for (int i = 0; i < n; i++)
    {
        double[] ai = a[i];
        double[] ci = new double[n];
        for (int j = 0; j < n; j++)
        {
            double[] bj = bt[j];
            double sum = 0;
            for (int k = 0; k < n; k++) sum += ai[k] * bj[k];
            ci[j] = sum;
        }
        c[i] = ci;
    }
    return c;
}
