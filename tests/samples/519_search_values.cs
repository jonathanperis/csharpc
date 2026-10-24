// System.Buffers.SearchValues for chars, bytes and strings, and the span searches that take them.
using System;
using System.Buffers;

class Program
{
    private static readonly SearchValues<string> Commands =
        SearchValues.Create(["start", "run", "go", "begin", "commence"], StringComparison.OrdinalIgnoreCase);
    private static readonly SearchValues<char> Vowels = SearchValues.Create("aeiouAEIOU");
    private static readonly SearchValues<byte> Digits = SearchValues.Create("0123456789"u8);

    static void Main()
    {
        foreach (var c in new[] { "start", "RUN", "Go", "stop", "", "begins" })
            Console.Write($"{c}:{Commands.Contains(c)} ");
        Console.WriteLine();
        Console.WriteLine($"{Vowels.Contains('e')} {Vowels.Contains('z')} {Digits.Contains((byte)'7')} {Digits.Contains((byte)'x')}");
        ReadOnlySpan<char> text = "rhythm and blues";
        Console.WriteLine($"{text.IndexOfAny(Vowels)} {text.LastIndexOfAny(Vowels)} {text.IndexOfAnyExcept(Vowels)} {text.LastIndexOfAnyExcept(Vowels)}");
        Console.WriteLine($"{"xyz".AsSpan().ContainsAny(Vowels)} {"aaa".AsSpan().ContainsAnyExcept(Vowels)} {"crwth".AsSpan().IndexOfAny(Vowels)}");
        Span<byte> bytes = [(byte)'a', (byte)'b', (byte)'3', (byte)'c'];
        Console.WriteLine($"{bytes.IndexOfAny(Digits)} {bytes.ContainsAny(Digits)}");
        var words = SearchValues.Create(["needle", "hay"], StringComparison.Ordinal);
        Console.WriteLine($"{"a haystack with a needle".AsSpan().IndexOfAny(words)} {"no match here".AsSpan().ContainsAny(words)} {"a NEEDLE".AsSpan().IndexOfAny(words)}");
        Console.WriteLine($"{"a NEEDLE".AsSpan().IndexOfAny(Commands)} {"let's BEGIN".AsSpan().IndexOfAny(Commands)}");
        try { SearchValues.Create(["x"], StringComparison.CurrentCulture); }
        catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    }
}
