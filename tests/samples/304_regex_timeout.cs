// Match timeouts: catastrophic backtracking under a short timeout throws RegexMatchTimeoutException
// (input, pattern and timeout attached); the same Regex keeps working afterwards.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        string input = new string('a', 40) + "!";
        var slow = new Regex(@"^(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(50));
        try
        {
            Console.WriteLine(slow.IsMatch(input));
        }
        catch (RegexMatchTimeoutException e)
        {
            Console.WriteLine(e.GetType().FullName);
            Console.WriteLine(e.Message);
            Console.WriteLine(e.Pattern + " " + e.Input.Length + " " + e.MatchTimeout);
            Console.WriteLine(e is TimeoutException);
        }
        Console.WriteLine(slow.IsMatch("aaaa"));

        try
        {
            Regex.Match(input, @"(x+x+)+y|(a|aa)+$!", RegexOptions.None, TimeSpan.FromMilliseconds(20));
            Console.WriteLine("finished");
        }
        catch (RegexMatchTimeoutException e)
        {
            Console.WriteLine("timeout " + e.MatchTimeout.TotalMilliseconds);
        }

        var custom = new RegexMatchTimeoutException("in", "pat", TimeSpan.FromSeconds(1));
        Console.WriteLine(custom.Input + " " + custom.Pattern + " " + custom.MatchTimeout);
        var plain = new RegexMatchTimeoutException();
        Console.WriteLine(plain.Message + " [" + plain.Input + "] [" + plain.Pattern + "] " + plain.MatchTimeout.Ticks);
        Console.WriteLine(new RegexMatchTimeoutException("custom message").Message);
    }
}
