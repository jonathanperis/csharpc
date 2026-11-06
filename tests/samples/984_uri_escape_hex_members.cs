// The static helpers of System.Uri that were missing: EscapeUriString (obsolete; keeps the reserved characters),
// TryEscapeDataString, HexEscape, HexUnescape, FromHex, the remaining UriScheme* constants, and
// UriCreationOptions with the Uri constructor and Uri.TryCreate taking it.
using System;

#pragma warning disable SYSLIB0013

static void Try(string label, Func<object> f)
{
    try { Console.WriteLine(label + " => " + f()); }
    catch (Exception e) { Console.WriteLine(label + " !! " + e.GetType().Name + ": " + e.Message); }
}

foreach (string s in new[] { "a b", "a%b", "http://h/a b?c d#e f", "é", "~!*'();:@&=+$,/?#[]", "%41", "\ud800", "xéy", "" })
{
    Try("escape uri [" + s + "]", () => Uri.EscapeUriString(s));
    Try("escape data [" + s + "]", () => Uri.EscapeDataString(s));
    Try("try escape [" + s + "]", () => { char[] d = new char[40]; bool ok = Uri.TryEscapeDataString(s, d, out int n); return ok + " " + n + " " + new string(d, 0, n); });
    Try("try escape small [" + s + "]", () => { char[] d = new char[2]; bool ok = Uri.TryEscapeDataString(s, d, out int n); return ok + " " + n; });
}
Try("escape uri null", () => Uri.EscapeUriString(null!));

foreach (char c in new[] { 'a', ' ', 'ÿ', 'Ā', '%', '\0' })
    Try("hex escape " + (int)c, () => Uri.HexEscape(c));
foreach ((string s, int i) in new[] { ("%41", 0), ("a%41", 1), ("%4", 0), ("%zz", 0), ("abc", 1), ("abc", 3), ("abc", -1), ("%e9x", 0), ("%41", 1), ("", 0), ("%2f%2F", 3) })
    Try($"hex unescape [{s}] {i}", () => { int index = i; char c = Uri.HexUnescape(s, ref index); return (int)c + " " + index; });
foreach (char c in new[] { '0', '9', 'a', 'F', 'g', ' ', '٠' })
    Try("from hex " + (int)c, () => Uri.FromHex(c));
Console.WriteLine(Uri.IsHexDigit('f') + " " + Uri.IsHexDigit('g') + " " + Uri.IsHexEncoding("a%2Fb", 1) + " " + Uri.IsHexEncoding("a%2", 1));

Console.WriteLine(string.Join(" ", Uri.UriSchemeGopher, Uri.UriSchemeNews, Uri.UriSchemeNntp, Uri.UriSchemeSsh, Uri.UriSchemeSftp,
    Uri.UriSchemeTelnet, Uri.UriSchemeNetTcp, Uri.UriSchemeNetPipe, Uri.UriSchemeData, Uri.UriSchemeFtps, Uri.UriSchemeMailto));

UriCreationOptions defaults = new();
Console.WriteLine(defaults.DangerousDisablePathAndQueryCanonicalization);
Try("ctor options", () => new Uri("http://h/a/../b", in defaults));
Try("try options", () => Uri.TryCreate("http://h/a/../b", in defaults, out Uri? u) + " " + u);
Try("try options bad", () => Uri.TryCreate("nope", in defaults, out Uri? u) + " " + u);
Try("ctor options bad", () => new Uri("nope", in defaults));
