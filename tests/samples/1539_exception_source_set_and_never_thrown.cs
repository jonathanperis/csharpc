// Exception.Source where csharpc gives .NET's answer (#104): a value the program set, before or after the exception is thrown,
// and null for an exception that was never thrown. (A thrown exception whose Source was never set names the throwing assembly
// in .NET; csharpc reports that as not supported yet instead of returning null.)
using System;

static class Program
{
    static void Main()
    {
        var never = new InvalidOperationException("never thrown");
        Console.WriteLine($"never thrown: {never.Source ?? "null"}");

        var preset = new ArgumentException("preset") { Source = "Preset.Source" };
        try { throw preset; }
        catch (ArgumentException e) { Console.WriteLine($"set before throw: {e.Source}"); }

        try { throw new FormatException("late"); }
        catch (FormatException e)
        {
            e.Source = "Late.Source";
            Console.WriteLine($"set after catch: {e.Source}");
        }

        var cleared = new Exception("cleared") { Source = "x" };
        cleared.Source = null;
        Console.WriteLine($"set to null: {cleared.Source ?? "null"}");
    }
}
