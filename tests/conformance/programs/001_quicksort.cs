int[] a = { 9, 4, 7, 1, 8, 2, 6, 3, 5, 0 };
QuickSort(a, 0, a.Length - 1);
Console.WriteLine(string.Join(" ", a));
static void QuickSort(int[] a, int lo, int hi)
{
    if (lo >= hi) return;
    int p = a[(lo + hi) / 2], i = lo, j = hi;
    while (i <= j)
    {
        while (a[i] < p) i++;
        while (a[j] > p) j--;
        if (i <= j) { (a[i], a[j]) = (a[j], a[i]); i++; j--; }
    }
    QuickSort(a, lo, j); QuickSort(a, i, hi);
}
