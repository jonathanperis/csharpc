// An exception no handler takes: the filters on the way all run (and decline) before any finally
// block, and the finally blocks still run before the process ends (exit status 134 on Unix).
using System;

class Program
{
    static bool Log(string what)
    {
        Console.WriteLine("filter " + what);
        return false;
    }

    static void Thrower()
    {
        try { throw new NotSupportedException("nobody catches this"); }
        catch (NotSupportedException) when (Log("thrower")) { Console.WriteLine("never"); }
        finally { Console.WriteLine("finally thrower"); }
    }

    static void Main()
    {
        try
        {
            try { Thrower(); }
            catch (ArgumentException) { Console.WriteLine("wrong type"); }
            finally { Console.WriteLine("finally middle"); }
        }
        catch (Exception e) when (Log("main " + e.Message)) { Console.WriteLine("never"); }
        finally { Console.WriteLine("finally main"); }
    }
}
