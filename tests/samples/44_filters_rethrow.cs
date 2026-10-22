// Exception filters (`when`), rethrow (`throw;`), and throwing stored exception objects.
foreach (var code in new[] { 404, 500, 418 })
{
    try { Request(code); }
    catch (HttpError e) when (e.Status == 404) { Console.WriteLine("not found"); }
    catch (HttpError e) when (e.Status >= 500) { Console.WriteLine("server error " + e.Status); }
    catch (HttpError e) { Console.WriteLine("other: " + e.Message); }
}

try
{
    try { Request(503); }
    catch (HttpError e)
    {
        Console.WriteLine("logging " + e.Status + ", rethrowing");
        throw;
    }
}
catch (HttpError e) { Console.WriteLine("outer got " + e.Status); }

Exception saved = new InvalidOperationException("saved for later");
try { throw saved; }
catch (InvalidOperationException e) { Console.WriteLine(ReferenceEquals(e, saved) + " " + e.Message); }

try
{
    try { throw new FormatException("low level"); }
    catch (FormatException e) { throw new InvalidOperationException("high level", e); }
}
catch (InvalidOperationException e) { Console.WriteLine(e.Message + " <- " + e.InnerException!.Message); }

int filterCalls = 0;
try { throw new Exception("x"); }
catch (Exception) when (++filterCalls > 5) { Console.WriteLine("never"); }
catch (Exception) { Console.WriteLine("filter ran " + filterCalls + " time(s)"); }

static void Request(int status)
{
    if (status != 200) throw new HttpError(status);
}

class HttpError(int status) : Exception("HTTP " + status)
{
    public int Status => status;
}
