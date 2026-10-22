// In-place quicksort of 5M pseudo-random ints (LCG), then a checksum — array indexing + branches.
int n = 5_000_000;
int[] data = new int[n];
uint seed = 42;
for (int i = 0; i < n; i++)
{
    seed = seed * 1664525 + 1013904223;
    data[i] = (int)(seed >> 1);
}
Sort(data, 0, n - 1);
long checksum = 0;
bool sorted = true;
for (int i = 0; i < n; i++)
{
    checksum += (long)data[i] * (i % 7 + 1);
    if (i > 0 && data[i - 1] > data[i]) sorted = false;
}
Console.WriteLine("sorted=" + sorted + " checksum=" + checksum);

static void Sort(int[] a, int lo, int hi)
{
    while (lo < hi)
    {
        int pivot = a[lo + (hi - lo) / 2];
        int i = lo, j = hi;
        while (i <= j)
        {
            while (a[i] < pivot) i++;
            while (a[j] > pivot) j--;
            if (i <= j)
            {
                int t = a[i]; a[i] = a[j]; a[j] = t;
                i++; j--;
            }
        }
        if (j - lo < hi - i) { Sort(a, lo, j); lo = i; }
        else { Sort(a, i, hi); hi = j; }
    }
}
