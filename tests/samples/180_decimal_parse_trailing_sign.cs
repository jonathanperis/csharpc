// decimal.Parse uses NumberStyles.Number: a trailing sign is allowed (once, among trailing white
// space), the FormatException message quotes the input, and a format error beats overflow.
string[] inputs =
{
    "12-", "12 -", "12- ", " 3+", "1,000.5-", "-1-", "1--", "+-1", "- 1", "5-x",
    "", " ", "-", "(1)", "1e3", "12abc", ".5-", "79228162514264337593543950335",
    "79228162514264337593543950336", "79228162514264337593543950336-", "792281625142643375935439503360x",
};
foreach (string s in inputs)
{
    try { Console.WriteLine("[" + s + "] -> " + decimal.Parse(s)); }
    catch (Exception e) { Console.WriteLine("[" + s + "] -> " + e.GetType().Name + ": " + e.Message); }
    Console.WriteLine("  TryParse: " + (decimal.TryParse(s, out decimal d) ? d.ToString() : "no"));
}
