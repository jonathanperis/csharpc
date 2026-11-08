using System.Text.RegularExpressions;

// A constructor and a static method that take RegexOptions reach the framework's NonBacktracking engine.
var word = new Regex(@"\bcsharp\w*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
Console.WriteLine(word.Match("I like CSharpc").Value);
Console.WriteLine(Regex.IsMatch("HELLO", "hello", RegexOptions.IgnoreCase));
