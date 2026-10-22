// Console key input with redirected stdin (the test runner feeds /dev/null): ReadKey and KeyAvailable
// throw InvalidOperationException like .NET; ConsoleKeyInfo value semantics; console encodings.
using System;
using System.Text;

class Program
{
    static void Main()
    {
        Console.WriteLine(Console.IsInputRedirected);
        Console.WriteLine(Console.OutputEncoding.WebName);
        Console.WriteLine(Console.OutputEncoding.GetPreamble().Length);
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("héllo ✓");
        Console.WriteLine(Console.InputEncoding.WebName);

        try { Console.WriteLine(Console.KeyAvailable); }
        catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
        try { Console.ReadKey(true); }
        catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

        var ki = new ConsoleKeyInfo('a', ConsoleKey.A, true, false, true);
        Console.WriteLine($"{ki.KeyChar} {ki.Key} {ki.Modifiers} {(int)ki.Modifiers}");
        Console.WriteLine(ki == new ConsoleKeyInfo('a', ConsoleKey.A, true, false, true));
        Console.WriteLine(ki != default);
        Console.WriteLine(ki.Equals((object)ki));
        Console.WriteLine(default(ConsoleKeyInfo).Key);
        Console.WriteLine((int)default(ConsoleKeyInfo).KeyChar);
        Console.WriteLine(ConsoleKey.Enter + " " + (int)ConsoleKey.Escape + " " + ConsoleKey.UpArrow);
        try { new ConsoleKeyInfo('x', (ConsoleKey)300, false, false, false); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

        Console.Clear();   // output is redirected: no escape sequence
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey(); // unhandled: exit 134 like .NET
    }
}
