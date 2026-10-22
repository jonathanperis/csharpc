// try/catch/finally with typed handlers, propagation across calls, nested trys.
try
{
    Console.WriteLine("in try");
    Fail(1);
    Console.WriteLine("not reached");
}
catch (ArgumentException e)
{
    Console.WriteLine("caught ArgumentException: " + e.Message);
}
catch (InvalidOperationException e)
{
    Console.WriteLine("caught InvalidOperationException: " + e.Message);
}
finally
{
    Console.WriteLine("outer finally");
}

try { Fail(2); }
catch (Exception e) { Console.WriteLine("caught: " + e.Message); }

try { Fail(3); }
catch { Console.WriteLine("caught with a catch-all"); }

Console.WriteLine(Parse("42") + Parse("x") + Parse(""));
Console.WriteLine(Depth(0));

try
{
    try { throw new NotSupportedException(); }
    catch (ArgumentException) { Console.WriteLine("wrong handler"); }
    finally { Console.WriteLine("inner finally"); }
}
catch (NotSupportedException e) { Console.WriteLine("outer caught: " + e.Message); }

int attempts = 0;
while (true)
{
    try
    {
        attempts++;
        if (attempts < 3) throw new InvalidOperationException("retry " + attempts);
        Console.WriteLine("succeeded after " + attempts);
        break;
    }
    catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
}
Console.WriteLine("done");

static void Fail(int kind)
{
    try
    {
        if (kind == 1) throw new InvalidOperationException("boom");
        if (kind == 2) throw new ArgumentNullException("value");
        throw new NotImplementedException();
    }
    finally { Console.WriteLine("Fail finally " + kind); }
}

static int Parse(string text)
{
    try
    {
        if (text.Length == 0) throw new ArgumentException("empty input", nameof(text));
        int result = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < '0' || c > '9') throw new FormatException("bad digit '" + c + "'");
            result = result * 10 + (c - '0');
        }
        return result;
    }
    catch (FormatException e) { Console.WriteLine("format: " + e.Message); return -1; }
    catch (ArgumentException e) { Console.WriteLine("argument: " + e.Message + " / " + e.ParamName); return -100; }
}

static int Depth(int n)
{
    if (n == 50) throw new OverflowException("too deep at " + n);
    try { return Depth(n + 1); }
    catch (OverflowException e) when (n == 10) { Console.WriteLine("caught at depth 10: " + e.Message); return n; }
}
