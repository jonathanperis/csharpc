Console.WriteLine(FindPair(new[] { 2, 7, 11, 15 }, 18));
static string FindPair(int[] a, int target)
{
    for (int i = 0; i < a.Length; i++)
        for (int j = i + 1; j < a.Length; j++)
            if (a[i] + a[j] == target) return $"{i},{j}";
    return "none";
}
