// Invalid patterns throw RegexParseException (an ArgumentException) with .NET's error code, offset
// and message.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        string[] patterns =
        {
            "(", ")", "a)", "[a", "[z-a]", "a{3,1}", "*", "a**", "+a", "a{1}{2}x**",
            @"\", @"\q", @"\p", @"\p{", @"\p{Foo}", @"\P{Lu", @"\k", @"\k<x>", @"\k<1", @"\2",
            @"(?<1a>x)", @"(?<a-b>x)", @"(?<0>x)", @"(?'x)", @"(?", @"(?z)", @"(?#comment", @"(?(1)a)",
            @"(?(x)a|b|c)", @"(?(?#c)a)", @"[\d-z]", @"[a-\d]", @"\x1", @"\u12", @"\cZ\c", @"\c!",
            @"(?<x>a)(?(x", @"[a-[b]c]", @"a{2147483648}", @"(?<n>a)\k<m>", @"(?(?<n>a)b)",
        };
        foreach (string p in patterns)
        {
            try
            {
                var r = new Regex(p);
                Console.WriteLine($"ok: {p} -> {r.Match("aaa").Success}");
            }
            catch (RegexParseException e)
            {
                Console.WriteLine($"{e.Error} @{e.Offset}: {e.Message}");
            }
            catch (ArgumentException e)
            {
                Console.WriteLine($"{e.GetType().Name}: {e.Message}");
            }
        }

        try { Regex.Match("x", "(a"); }
        catch (ArgumentException e)
        {
            Console.WriteLine(e is RegexParseException);
            Console.WriteLine(e.GetType().FullName + " | " + e.ParamName + " | " + e.Message);
        }
    }
}
