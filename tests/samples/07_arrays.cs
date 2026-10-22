// Arrays: allocation, indexing with bounds checks, Length, collection expressions.
int[] primes = Sieve(100);
Console.WriteLine($"{primes.Length} primes below 100");
for (int i = 0; i < primes.Length; i++) Console.Write(primes[i] + (i < primes.Length - 1 ? "," : "\n"));

int[] data = [5, 3, 9, 1, 7, 2, 8, 6, 4, 0];
BubbleSort(data);
Print(data);

double[] weights = new double[4];
for (int i = 0; i < weights.Length; i++) weights[i] = 1.0 / (i + 1);
double sum = 0;
foreach (double w in weights) sum += w;
Console.WriteLine(sum);

string[] words = ["alpha", "beta", "gamma"];
string joined = "";
foreach (var word in words) joined += word.Length + word;
Console.WriteLine(joined);

bool[] flags = new bool[3];
flags[1] = true;
Console.WriteLine($"{flags[0]} {flags[1]} {flags[2]}");

long[][] triangle = new long[10][];
for (int row = 0; row < triangle.Length; row++)
{
    triangle[row] = new long[row + 1];
    triangle[row][0] = triangle[row][row] = 1;
    for (int col = 1; col < row; col++)
        triangle[row][col] = triangle[row - 1][col - 1] + triangle[row - 1][col];
}
foreach (var r in triangle) PrintLongs(r);

int[,]? notYet = null;   // declared but unused: multi-dimensional arrays aren't supported yet
Console.WriteLine(notYet == null);

static int[] Sieve(int limit)
{
    var composite = new bool[limit];
    int count = 0;
    for (int i = 2; i < limit; i++)
    {
        if (composite[i]) continue;
        count++;
        for (int j = i * i; j < limit; j += i) composite[j] = true;
    }
    var result = new int[count];
    int k = 0;
    for (int i = 2; i < limit; i++)
        if (!composite[i]) result[k++] = i;
    return result;
}

static void BubbleSort(int[] a)
{
    for (int i = 0; i < a.Length; i++)
        for (int j = 0; j < a.Length - 1 - i; j++)
            if (a[j] > a[j + 1]) (a[j], a[j + 1]) = (a[j + 1], a[j]);
}

static void Print(int[] a)
{
    string line = "";
    foreach (var x in a) line += x + " ";
    Console.WriteLine(line);
}

static void PrintLongs(long[] a)
{
    string line = "";
    foreach (var x in a) line += x + " ";
    Console.WriteLine(line);
}
