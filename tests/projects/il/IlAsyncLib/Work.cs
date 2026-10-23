namespace IlAsyncLib;

public static class Work
{
    public static async Task<int> DoubleAsync(int x)
    {
        await Task.Yield();
        return x * 2;
    }

    // Never called by IlAsyncApp: only reachable through nothing, so it compiles as a stub (warning).
    public static async Task<string> UnusedAsync()
    {
        await Task.Delay(1);
        return "unused";
    }
}
