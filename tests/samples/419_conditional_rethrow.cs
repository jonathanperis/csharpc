// `throw;` reached through a conditional branch of a catch block (Roslyn's CFG makes the conditional edge
// itself the rethrow), including nested catches, where `throw;` rethrows the innermost caught exception.
static int Parse(string s, bool rethrow)
{
    try
    {
        return int.Parse(s);
    }
    catch (FormatException)
    {
        if (rethrow) throw;
        return -1;
    }
}

static string Nested(string outer, string inner, int mode)
{
    try
    {
        throw new InvalidOperationException(outer);
    }
    catch (InvalidOperationException)
    {
        try
        {
            throw new ArgumentException(inner);
        }
        catch (ArgumentException)
        {
            if (mode == 1) throw;
        }
        if (mode == 2) throw;
        return "handled";
    }
}

static int Loop(int[] values)
{
    int sum = 0;
    foreach (var v in values)
    {
        try
        {
            if (v < 0) throw new ValueException(v);
            sum += v;
        }
        catch (ValueException e) when (e.Value is int x)
        {
            if (x < -10) throw;
            sum -= 1;
        }
    }
    return sum;
}

Console.WriteLine(Parse("12", true));
Console.WriteLine(Parse("x", false));
try { Parse("x", true); } catch (FormatException e) { Console.WriteLine("rethrown: " + e.GetType().Name); }
for (int mode = 0; mode < 3; mode++)
{
    try { Console.WriteLine(Nested("outer", "inner", mode)); }
    catch (Exception e) { Console.WriteLine($"mode {mode}: {e.GetType().Name} {e.Message}"); }
}
Console.WriteLine(Loop([1, 2, -3, 4]));
try { Loop([1, -20]); } catch (ValueException e) { Console.WriteLine(e.Message); }

class ValueException(int value) : Exception($"bad value {value}") { public object Value => value; }
