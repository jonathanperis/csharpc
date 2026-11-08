using System.Text.RegularExpressions;

var date = new Regex(@"(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})");
var m = date.Match("released on 2026-11-08");
Console.WriteLine($"{m.Groups["d"].Value}/{m.Groups["m"].Value}/{m.Groups["y"].Value}");
Console.WriteLine(Regex.Replace("a1b22c333", @"\d+", "#"));
