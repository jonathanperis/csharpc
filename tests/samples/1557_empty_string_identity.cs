// Empty strings made at run time are string.Empty where .NET's are (#492): constructors, Concat, span ToString, Substring, StringBuilder
// (Replace makes a new one); Concat and Join return an operand itself instead of a copy.
using System;
using System.Text;
static class Program
{
    static void Main()
    {
        string empty = string.Empty;
        Console.WriteLine($"literal: {ReferenceEquals("", empty)}");
        Console.WriteLine($"new string(char[0]): {ReferenceEquals(new string(new char[0]), empty)}");
        Console.WriteLine($"new string(char[], 0, 0): {ReferenceEquals(new string(new[] { 'a' }, 0, 0), empty)}");
        Console.WriteLine($"new string('a', 0): {ReferenceEquals(new string('a', 0), empty)}");
        Console.WriteLine($"new string(span): {ReferenceEquals(new string(ReadOnlySpan<char>.Empty), empty)}");
        Console.WriteLine($"Substring: {ReferenceEquals("abc".Substring(3), empty)} {ReferenceEquals("abc".Substring(1, 0), empty)}");
        Console.WriteLine($"Concat: {ReferenceEquals(string.Concat("", ""), empty)} {ReferenceEquals(string.Concat(new string[0]), empty)}");
        Console.WriteLine($"StringBuilder: {ReferenceEquals(new StringBuilder().ToString(), empty)}");
        Console.WriteLine($"Join: {ReferenceEquals(string.Join(",", new string[0]), empty)}");
        Console.WriteLine($"Trim: {ReferenceEquals("   ".Trim(), empty)} Replace: {ReferenceEquals("a".Replace("a", ""), empty)}");
        Console.WriteLine($"ToString: {ReferenceEquals("".ToUpperInvariant(), empty)} {ReferenceEquals(new char[0].AsSpan().ToString(), empty)}");
        // Concat and Join hand back an operand rather than copying it, as .NET's do; Replace makes a new empty string.
        string s = new string('x', 2);
        Console.WriteLine($"identity: {ReferenceEquals(s + "", s)} {ReferenceEquals("" + s, s)} {ReferenceEquals(string.Concat(s, null), s)} {ReferenceEquals(string.Concat(new[] { s }), s)} {ReferenceEquals(string.Join(",", new[] { s }), s)} {ReferenceEquals(s + s, s)}");
        string? none = null;
        Console.WriteLine($"null operands: {ReferenceEquals(none + none, empty)} {ReferenceEquals(string.Join(",", new string?[] { null }), empty)}");
    }
}
