using Csharpc.Driver;

try
{
    var options = CompilerOptions.Parse(args, out var exitEarly);
    if (exitEarly || options is null) return 0;
    return new Compiler(options).Run();
}
catch (ArgumentException e)
{
    Console.Error.WriteLine($"csharpc: {e.Message}\n\n{CompilerOptions.Usage}");
    return 2;
}
catch (Exception e) when (e is InvalidOperationException or FileNotFoundException or DllNotFoundException or IOException)
{
    Console.Error.WriteLine($"csharpc: {e.Message}");
    return 1;
}
