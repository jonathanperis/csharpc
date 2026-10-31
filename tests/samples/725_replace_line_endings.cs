// string.ReplaceLineEndings (csharpc's own driver uses it): CR, LF, CRLF, NEL, LS, FF and PS are each one
// line ending; LF CR is two. Without an argument the replacement is Environment.NewLine.
using System;

string[] inputs = { "", "plain", "a\r\nb\nc\rd", "\n\r", "x\u0085y\u2028z\u2029w\fv", "\r\n\r\n", "tab\tvertical\vkept" };
foreach (var s in inputs)
{
    Console.WriteLine($"[{s.ReplaceLineEndings("|")}] [{s.ReplaceLineEndings("")}] {s.ReplaceLineEndings().Replace(Environment.NewLine, "<NL>")}");
    Console.WriteLine(ReferenceEquals(s, s.ReplaceLineEndings("|")));
}
try { "a".ReplaceLineEndings(null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
