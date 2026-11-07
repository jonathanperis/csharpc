namespace Csharpc.Driver;

/// <summary>
/// How many partitions ParallelCodegen and ParallelOptimize may work on at once without running the
/// machine out of memory (docs/adr/0014-compile-time.md, section 6). Each worker parses the program's
/// whole bitcode image into a module of its own before keeping its part of it, and for a closed world
/// the size of Roslyn that module is gigabytes. The partitioning never depends on the thread count, so
/// fewer workers change the compile time, not the executable.
///
/// Measured on csharpc compiling itself (9 M instructions), peak RSS with two workers and with one:
/// -O0 8.0 and 5.3 GB (backend 122 and 219 s), -O2 7.6 and 5.5 GB.
/// </summary>
internal static class MemoryBudget
{
    /// <summary>
    /// What a worker adds to the process, as a multiple of the bitcode image: the module it parses (some 30
    /// bytes of bitcode per instruction are some 300 in memory) and the code generator's or optimizer's own data.
    /// A second worker added 2.0 GB at -O2 (330 MB image) and 2.7 GB at -O0 (274 MB image).
    /// </summary>
    internal const double BytesPerImageByte = 10.0;

    /// <summary>
    /// Workers to run: <paramref name="wanted"/>, or fewer (at least one) when the rest of the memory budget
    /// does not hold that many parsed copies of the image. The first worker is free: it parses into the memory
    /// the program's own module has just given back (freed, but still part of the working set). The budget is 90 %
    /// of the memory the process may use (the machine's, or its container's), or CSHARPC_MEMORY_MB.
    /// </summary>
    public static int Workers(int wanted, long imageBytes, Action<string>? log)
    {
        if (wanted <= 1) return 1;
        var budget = Environment.GetEnvironmentVariable("CSHARPC_MEMORY_MB") is { Length: > 0 } text && long.TryParse(text, out var mb) && mb > 0
            ? mb << 20
            : (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes * 0.9);
        var used = Environment.WorkingSet;
        var each = (long)(imageBytes * BytesPerImageByte);
        var fit = each <= 0 ? wanted : (int)Math.Clamp(1 + (budget - used) / each, 1, wanted);
        if (fit < wanted)
            log?.Invoke($"memory: {fit} of {wanted} worker(s) ({budget >> 20} MB budget, {used >> 20} MB in use, ~{each >> 20} MB per extra worker)");
        return fit;
    }
}
