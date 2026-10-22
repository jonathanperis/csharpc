// TimeSpan custom formats accept only d (up to 8), h/m/s (up to 2), f/F (up to 7), quotes, '\' and
// '%'; any other character, an unknown single-character standard format or a bad quote is an error.
var spans = new[] { new TimeSpan(1, 2, 3, 4, 567), TimeSpan.FromTicks(-12345678901), TimeSpan.Zero };
string[] formats =
{
    "c", "t", "T", "g", "G", "d", "h", "m", "s", "f", "F", "%d", "%h", "%F", "%%", "%", "\\", "dd\\.hh\\:mm",
    "dddddddd", "ddddddddd", "hhh", "mm':'ss", "ss'.'fffffff", "ffffffff", "FFFFFFF", "FFFFFFFF", "'abc", "\"x",
    "'a\\'b'", "hh:mm", "h x", "d'd' h'h'", "", " ", "\\'", "hh\\", "%'",
};
foreach (string f in formats)
{
    var line = new System.Text.StringBuilder("[" + f + "] ->");
    foreach (var span in spans)
    {
        try { line.Append(" [").Append(span.ToString(f)).Append(']'); }
        catch (Exception e) { line.Append(' ').Append(e.GetType().Name).Append(": ").Append(e.Message); }
    }
    Console.WriteLine(line);
}
