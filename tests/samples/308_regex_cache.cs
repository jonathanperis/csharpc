// The static Regex methods share a cache of parsed patterns (Regex.CacheSize); results don't depend
// on it. Many patterns, repeated patterns, cache resizing and options as part of the cache key.
using System;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        Console.WriteLine(Regex.CacheSize);
        var sb = new StringBuilder();
        for (int round = 0; round < 3; round++)
            for (int i = 0; i < 40; i++)
                sb.Append(Regex.IsMatch("value" + i, "^value" + i + "$") ? '+' : '-');
        Console.WriteLine(sb.ToString());
        Console.WriteLine(Regex.IsMatch("ABC", "abc") + " " + Regex.IsMatch("ABC", "abc", RegexOptions.IgnoreCase) + " " + Regex.IsMatch("ABC", "abc"));
        Regex.CacheSize = 2;
        Console.WriteLine(Regex.CacheSize);
        for (int i = 0; i < 5; i++) Console.Write(Regex.Match("n" + i, @"\d").Value);
        Console.WriteLine();
        Regex.CacheSize = 0;
        Console.WriteLine(Regex.Replace("a b c", " ", "_") + " " + Regex.CacheSize);
        Regex.CacheSize = 40;
        for (int i = 0; i < 60; i++) sb.Append(Regex.Split("x" + i + "y", @"\d+").Length);
        Console.WriteLine(sb.Length);
        try { Regex.CacheSize = -1; }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}
