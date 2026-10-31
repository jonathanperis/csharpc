// Exception filters run in the first pass (two-pass dispatch, as in .NET): every `when` filter up
// the stack runs before the finally blocks of the frames being unwound, and a filter sees the state
// before those finally blocks ran.
using System;

class Program
{
    static int s_state;

    static bool Log(string what, bool result)
    {
        Console.WriteLine($"filter {what} (state {s_state}) -> {result}");
        return result;
    }

    static void Level3(int depth)
    {
        try
        {
            s_state = 3;
            throw new InvalidOperationException("deep " + depth);
        }
        finally
        {
            Console.WriteLine("finally level3");
            s_state = -3;
        }
    }

    static void Level2(int depth)
    {
        try
        {
            try { Level3(depth); }
            catch (ArgumentException) { Console.WriteLine("wrong handler"); }
            finally { Console.WriteLine("finally level2 inner"); s_state = -2; }
        }
        catch (InvalidOperationException e) when (Log("level2 " + e.Message, depth > 5)) { Console.WriteLine("caught in level2"); }
        finally { Console.WriteLine("finally level2 outer"); }
    }

    static void Level1(int depth)
    {
        try { Level2(depth); }
        catch (Exception e) when (Log("level1 " + e.GetType().Name, false)) { Console.WriteLine("never"); }
        finally { Console.WriteLine("finally level1"); }
    }

    static int Recurse(int n)
    {
        try
        {
            if (n == 0) throw new FormatException("bottom");
            return Recurse(n - 1);
        }
        catch (FormatException) when (Log("recurse " + n, n == 2)) { Console.WriteLine("caught at " + n); return n; }
        finally { Console.WriteLine("finally recurse " + n); }
    }

    static void Main()
    {
        Console.WriteLine("-- outer filter accepts after inner ones decline");
        try { Level1(1); }
        catch (InvalidOperationException e) when (Log("main", true)) { Console.WriteLine("main caught: " + e.Message + ", state " + s_state); }

        Console.WriteLine("-- inner filter accepts");
        s_state = 0;
        try { Level1(9); }
        catch (Exception) { Console.WriteLine("not reached"); }
        Console.WriteLine("state " + s_state);

        Console.WriteLine("-- recursion: the same filter in several frames");
        Console.WriteLine("result " + Recurse(4));

        Console.WriteLine("-- several handlers of one try");
        for (var i = 0; i < 3; i++)
        {
            try
            {
                try { throw i == 1 ? new ArgumentNullException("p") : new ArgumentException("a" + i); }
                finally { Console.WriteLine("finally " + i); }
            }
            catch (ArgumentNullException) when (Log("null " + i, i == 1)) { Console.WriteLine("handler null " + i); }
            catch (ArgumentException) when (Log("arg " + i, i == 0)) { Console.WriteLine("handler arg " + i); }
            catch (ArgumentException e) { Console.WriteLine("plain handler " + i + " " + e.Message); }
        }
    }
}
