namespace Csharpc.Driver;

/// <summary>
/// How many threads one compilation may use (docs/adr/0014-compile-time.md): `-j N`, else
/// CSHARPC_THREADS, else the processor count. Parallel phases (corelib parsing, Roslyn binding ahead
/// of lowering, LLVM code generation) never change the output: the work is split the same way and
/// merged in the same order whatever the thread count.
/// </summary>
public static class Parallelism
{
    public static int Threads { get; set; } = Default();

    private static int Default() =>
        Environment.GetEnvironmentVariable("CSHARPC_THREADS") is { Length: > 0 } text && int.TryParse(text, out var n) && n > 0
            ? n
            : Environment.ProcessorCount;

    public static ParallelOptions Options => new() { MaxDegreeOfParallelism = Threads };
}
