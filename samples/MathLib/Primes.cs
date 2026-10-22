namespace MathLib;

/// <summary>Library code compiled into the same closed-world native image as the app.</summary>
public static class Primes
{
    public static bool IsPrime(int n)
    {
        if (n < 2) return false;
        for (int d = 2; d * d <= n; d++)
            if (n % d == 0) return false;
        return true;
    }

    public static int[] FirstN(int count)
    {
        var result = new int[count];
        int found = 0;
        for (int candidate = 2; found < count; candidate++)
            if (IsPrime(candidate)) result[found++] = candidate;
        return result;
    }
}
