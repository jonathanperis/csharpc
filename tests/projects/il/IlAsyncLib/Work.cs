namespace IlAsyncLib;

public static class Work
{
    public static async Task<int> DoubleAsync(int x)
    {
        await Task.Yield();
        return x * 2;
    }

    // Not called by IlAsyncApp: csharpc never lowers it (only reachable methods are compiled from IL).
    public static async Task<string> UnusedAsync()
    {
        await Task.Delay(1);
        return "unused";
    }
}
