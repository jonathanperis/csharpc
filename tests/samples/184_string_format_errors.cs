// string.Format reports malformed composite formats like .NET's AppendFormatHelper: the reason and
// the offset where parsing stopped.
string[] formats =
{
    "{0}", "{0,5}|{1,-6}|{2:F1}", "{{0}}", "a}}b{{c", "}", "a}b", "x}", "{", "{{", "{0", "{0,", "{0,-", "{0, 5",
    "{0:", "{0:N", "{a}", "{ 0}", "{0 }", "{0 ,3}", "{0, -3 }", "{0,x}", "{0,-x}", "{0x}", "{0:{}", "{0:a{b}",
    "{3}", "{1000000}", "{9999999}", "{0,10000000}", "{0,1000}", "{0,99999999}", "{-1}", "{0}{", "ab{0}}", "{0}{1}{2}{",
    "{0:X.}", "{2:E}{", "{1,3:abc}", "{0:}", "{0,0}", "{00}", "{01}",
};
foreach (string f in formats)
{
    try { Console.WriteLine("[" + f + "] -> [" + string.Format(f, 42, "text", -1.5) + "]"); }
    catch (Exception e) { Console.WriteLine("[" + f + "] -> " + e.GetType().Name + ": " + e.Message); }
}
