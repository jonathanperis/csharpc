// Exception filters that throw (the exception is swallowed and the filter declines), rethrow inside
// a catch (`throw;` runs the outer filters again, for the new dispatch), `throw e;`, and filters
// that read and write the method's locals, parameters, `this`, captured variables and pattern
// variables the handler then uses.
using System;
using System.Collections.Generic;

class Counter
{
    public int Hits;
    public string Name = "counter";

    public bool Check(Exception e)
    {
        Hits++;
        Console.WriteLine($"{Name}.Check #{Hits}: {e.Message}");
        return Hits >= 2;
    }

    public void Run()
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                try { throw new Exception("run " + i); }
                finally { Console.WriteLine("run finally " + i + " hits " + Hits); }
            }
            catch (Exception e) when (Check(e) && e.Message.EndsWith(i.ToString())) { Console.WriteLine("run caught " + i); }
            catch (Exception) { Console.WriteLine("run fallback " + i); }
        }
    }
}

struct Box
{
    public int Value;
    public bool Bump(int limit) { Value++; return Value > limit; }

    public void Probe()
    {
        try { throw new Exception("box"); }
        catch when (Bump(0)) { Console.WriteLine("box caught, value " + Value); }
    }
}

class Program
{
    static bool Throwing(string what)
    {
        Console.WriteLine("filter " + what + " throws");
        throw new InvalidOperationException("from filter " + what);
    }

    static bool Log(string what, bool result)
    {
        Console.WriteLine("filter " + what + " -> " + result);
        return result;
    }

    static T Generic<T>(T value, Func<T, bool> accept)
    {
        try { throw new ArgumentException(value!.ToString()); }
        catch (ArgumentException e) when (accept(value) && e.Message.Length > 0) { Console.WriteLine("generic caught " + value); return value; }
        catch (ArgumentException) { Console.WriteLine("generic declined " + value); return default!; }
    }

    static void Main()
    {
        Console.WriteLine("-- a filter that throws declines");
        try
        {
            try { throw new FormatException("first"); }
            catch (FormatException) when (Throwing("inner")) { Console.WriteLine("never"); }
            finally { Console.WriteLine("finally inner"); }
        }
        catch (FormatException e) when (Log("outer " + e.Message, true)) { Console.WriteLine("outer caught " + e.Message); }

        Console.WriteLine("-- a filter whose own try/catch handles its exception");
        try { throw new Exception("second"); }
        catch (Exception e) when (SafeLength(e.Message) > 3) { Console.WriteLine("caught second"); }

        Console.WriteLine("-- rethrow runs the outer filters again");
        var passes = 0;
        try
        {
            try
            {
                try { throw new TimeoutException("t"); }
                catch (TimeoutException) when (Log("first pass " + ++passes, true)) { Console.WriteLine("inner handler, rethrowing"); throw; }
                finally { Console.WriteLine("finally after rethrow"); }
            }
            catch (TimeoutException) when (Log("middle " + passes, false)) { Console.WriteLine("never"); }
        }
        catch (TimeoutException e) when (Log("outer " + passes, true)) { Console.WriteLine("outer caught " + e.Message + " passes " + passes); }

        Console.WriteLine("-- the same exception thrown again");
        Exception? saved = null;
        try { throw new KeyNotFoundException("k"); }
        catch (KeyNotFoundException e) when (Log("first throw", true)) { Console.WriteLine("inner caught"); saved = e; }
        try { throw saved; }
        catch (KeyNotFoundException) when (Log("second throw", true)) { Console.WriteLine("outer caught k"); }

        Console.WriteLine("-- locals, parameters, captured variables, pattern variables");
        var local = 10;
        var captured = 0;
        Func<int> read = () => captured;
        try { throw new ArgumentOutOfRangeException("arg", "range"); }
        catch (ArgumentOutOfRangeException e) when (e.ParamName is string name && (local += 5) > 0 && ++captured > 0)
        {
            Console.WriteLine($"name {name}, local {local}, captured {captured}, via lambda {read()}");
        }
        Probe(7);

        Console.WriteLine("-- this and struct this");
        new Counter().Run();
        var box = new Box();
        box.Probe();
        Console.WriteLine("box after " + box.Value);

        Console.WriteLine("-- generics and lambdas");
        Console.WriteLine(Generic(5, v => v > 3));
        Console.WriteLine(Generic("no", v => v.Length > 3) ?? "null");
        Action nested = () =>
        {
            var inLambda = "lambda";
            try { throw new Exception("in lambda"); }
            catch (Exception e) when (Log(inLambda + ": " + e.Message, true)) { Console.WriteLine("lambda caught"); }
        };
        nested();
        LocalFunction(3);

        void LocalFunction(int n)
        {
            try { throw new Exception("local " + n); }
            catch (Exception e) when (Log(e.Message + " " + local, n == 3)) { Console.WriteLine("local function caught"); }
        }
    }

    static int SafeLength(string s)
    {
        try { return s.Length + int.Parse("x"); }
        catch (FormatException) { Console.WriteLine("filter helper caught its own exception"); return s.Length; }
    }

    static void Probe(int parameter)
    {
        try { throw new Exception("probe"); }
        catch (Exception) when (parameter++ > 5 && Log("parameter " + parameter, true)) { Console.WriteLine("parameter now " + parameter); }
    }
}
