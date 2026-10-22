// fannkuch-redux — Computer Language Benchmarks Game. Integer array permutations.
int n = 10;
int[] perm = new int[n];
int[] perm1 = new int[n];
int[] count = new int[n];
for (int i = 0; i < n; i++) perm1[i] = i;
int maxFlips = 0, checksum = 0, permCount = 0, r = n;
while (true)
{
    while (r != 1) { count[r - 1] = r; r--; }
    for (int i = 0; i < n; i++) perm[i] = perm1[i];
    int flips = 0;
    int k;
    while ((k = perm[0]) != 0)
    {
        int k2 = (k + 1) >> 1;
        for (int i = 0; i < k2; i++)
        {
            int t = perm[i];
            perm[i] = perm[k - i];
            perm[k - i] = t;
        }
        flips++;
    }
    maxFlips = Math.Max(maxFlips, flips);
    checksum += permCount % 2 == 0 ? flips : -flips;

    bool done = false;
    while (true)
    {
        if (r == n) { done = true; break; }
        int perm0 = perm1[0];
        for (int i = 0; i < r; i++) perm1[i] = perm1[i + 1];
        perm1[r] = perm0;
        count[r] = count[r] - 1;
        if (count[r] > 0) break;
        r++;
    }
    if (done) break;
    permCount++;
}
Console.WriteLine(checksum);
Console.WriteLine("Pfannkuchen(" + n + ") = " + maxFlips);
