// A Regex built the way .NET's regex source generator emits one (ASP.NET Core's route constraints ship
// such code compiled): a Regex subclass with its own RegexRunnerFactory and a RegexRunner whose Scan
// matches by hand through the protected runner API (runtextpos, Capture, CheckTimeout).
#nullable disable
using System;
using System.Text.RegularExpressions;

// Matches runs of ASCII letters, like the generated `^[a-z]*$`-style constraints, but unanchored.
sealed class LettersRegex : Regex
{
    public LettersRegex()
    {
        pattern = "[A-Za-z]+";
        roptions = RegexOptions.None;
        internalMatchTimeout = System.Threading.Timeout.InfiniteTimeSpan;
        factory = new RunnerFactory();
        capsize = 1;
    }

    private sealed class RunnerFactory : RegexRunnerFactory
    {
        protected override RegexRunner CreateInstance() => new Runner();

        private sealed class Runner : RegexRunner
        {
            protected override void Scan(ReadOnlySpan<char> inputSpan)
            {
                // runtextpos: where this scan starts; report one match (or none) and leave runtextpos past it.
                int start = runtextpos;
                while (start < inputSpan.Length && !char.IsAsciiLetter(inputSpan[start])) start++;
                if (start >= inputSpan.Length) { runtextpos = inputSpan.Length; return; }
                int end = start;
                while (end < inputSpan.Length && char.IsAsciiLetter(inputSpan[end])) end++;
                CheckTimeout();
                Capture(0, start, end);
                runtextpos = end;
            }
        }
    }
}

class Program
{
    static void Main()
    {
        var regex = new LettersRegex();
        Console.WriteLine(regex.IsMatch("123 abc"));
        Console.WriteLine(regex.IsMatch("123 456"));
        var m = regex.Match("42 hello, world 7");
        Console.WriteLine($"{m.Success} {m.Index} {m.Length} {m.Value}");
        m = m.NextMatch();
        Console.WriteLine($"{m.Success} {m.Index} {m.Length} {m.Value}");
        m = m.NextMatch();
        Console.WriteLine($"{m.Success} '{m.Value}'");
        foreach (Match each in regex.Matches("a1bb22ccc333"))
            Console.Write($"[{each.Value}@{each.Index}]");
        Console.WriteLine();
        Console.WriteLine(regex.Replace("x1y22z", "_"));
        Console.WriteLine(string.Join("|", regex.Split("ab12cd34")));
        Console.WriteLine(regex.Count("one two 3 four"));
        Console.WriteLine(regex.ToString());
        Console.WriteLine(regex.Options);
        Console.WriteLine(regex.MatchTimeout == System.Threading.Timeout.InfiniteTimeSpan);
        Console.WriteLine(string.Join(",", regex.GetGroupNumbers()));
    }
}
