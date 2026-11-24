// string.Clone, Intern and IsInterned as NativeAOT runs them (#103).
using System;

static class Program
{
    static string Make(params char[] c) => new string(c);
    static void Main()
    {
        string built = Make('a', 'b');
        Console.WriteLine($"clone is self: {ReferenceEquals(built.Clone(), built)} {ReferenceEquals("lit".Clone(), "lit")}");
        Console.WriteLine($"built before Intern: {string.IsInterned(built) is null}");
        Console.WriteLine($"literal before Intern: {string.IsInterned("ab") is null} {string.IsInterned("never-interned") is null}");
        string first = string.Intern(built);
        Console.WriteLine($"Intern returns the first: {ReferenceEquals(first, built)} same as literal: {ReferenceEquals(first, "ab")}");
        Console.WriteLine($"IsInterned after: {ReferenceEquals(string.IsInterned(Make('a', 'b')), built)}");
        string second = string.Intern(Make('a', 'b'));
        Console.WriteLine($"second Intern: {ReferenceEquals(second, built)}");
        string lit = string.Intern("xyz");
        Console.WriteLine($"literal interned: {ReferenceEquals(lit, "xyz")} {ReferenceEquals(string.Intern(Make('x', 'y', 'z')), "xyz")}");
        Console.WriteLine($"empty: {ReferenceEquals(string.Intern(string.Empty), string.Empty)} {string.IsInterned("") is null}");
        try { string.Intern(null!); } catch (ArgumentNullException e) { Console.WriteLine($"Intern(null): {e.ParamName}"); }
        try { string.IsInterned(null!); } catch (ArgumentNullException e) { Console.WriteLine($"IsInterned(null): {e.ParamName}"); }
    }
}
