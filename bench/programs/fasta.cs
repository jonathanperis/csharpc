// fasta-like — the benchmark game's LCG random generator driving a weighted nucleotide choice.
// Prints symbol frequencies and a rolling hash instead of the FASTA text.
var random = new Lcg();
char[] symbols = new char[] { 'a', 'c', 'g', 't' };
double[] probabilities = new double[] { 0.3029549426680, 0.1979883004921, 0.1975473066391, 0.3015094502008 };
double[] cumulative = new double[4];
double acc = 0;
for (int i = 0; i < 4; i++)
{
    acc += probabilities[i];
    cumulative[i] = acc;
}

int n = 10000000;
int[] counts = new int[4];
int hash = 0;
for (int i = 0; i < n; i++)
{
    double r = random.Next(1.0);
    int k = 0;
    while (k < 3 && r >= cumulative[k]) k++;
    counts[k]++;
    hash = hash * 31 + symbols[k];
}
for (int i = 0; i < 4; i++) Console.WriteLine(symbols[i] + " " + counts[i]);
Console.WriteLine("hash " + hash);

class Lcg
{
    const int IM = 139968, IA = 3877, IC = 29573;
    private int _seed = 42;

    public double Next(double max)
    {
        _seed = (_seed * IA + IC) % IM;
        return max * _seed / IM;
    }
}
