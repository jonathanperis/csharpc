// A negative value that rounds to zero in a custom format keeps its '-' only when the format has no
// explicit negative section: (-0.4).ToString("0") is "-0" but (-0.4).ToString("0;(0)") is "0".
double[] doubles = { -0.0, -1e-10, -0.4, -0.6, -1.5, 0.0, 2.5 };
decimal[] decimals = { -0.0m, -0.00001m, -0.4m, 0m, -2m };
long[] longs = { 0, -1, 7 };
string[] formats =
{
    "0.0", "#", "0", "C;%;", "x;y", "x;y;z", "0;-0", "#;", ";#", "#.#", "0e0", "0.##", "%", "' '",
    "+;#e", "22;-", "#,#", "0;", "0;;z", "0;;", "x;;z", "0;'a;b'", "0\\;x", "0';'x", "#;y", "0;y;", "0;(0)",
};
foreach (string f in formats)
{
    var line = new System.Text.StringBuilder(f.PadRight(8) + ":");
    foreach (double v in doubles) line.Append(" [").Append(v.ToString(f)).Append(']');
    foreach (decimal v in decimals) line.Append(" {").Append(v.ToString(f)).Append('}');
    foreach (long v in longs) line.Append(" <").Append(v.ToString(f)).Append('>');
    Console.WriteLine(line);
}
