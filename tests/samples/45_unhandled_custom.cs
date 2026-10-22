// An unhandled exception terminates the process (exit status 134) after printing to stderr.
// Like .NET, the finally blocks on the way out still run first.
Console.WriteLine("start");
try
{
    Process(3);
}
finally
{
    Console.WriteLine("finally runs even though the exception is unhandled");
}

static void Process(int n)
{
    if (n > 2) throw new ConfigurationError("bad config value " + n);
}

class ConfigurationError(string message) : Exception(message) { }
