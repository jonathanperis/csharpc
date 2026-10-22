// DateTime custom formats: unterminated quotes, a trailing '%' or '\', "%%" and more than seven
// fraction digits are errors; quoted text honours '\' escapes; 'g' is the era; '%x' formats one
// specifier; 'F' drops a dangling '.'; unknown single-character standard formats are errors.
var date = new DateTime(2024, 3, 7, 14, 5, 9).AddTicks(1234500);
var whole = new DateTime(1999, 12, 31, 23, 59, 59);
string[] formats =
{
    "'abc", "\"abc", "yyyy'-", "'a\\'b'", "\"a\\\"b\"", "'a\\", "%", "%%", "d%", "%d", "%M", "%'", "%\\", "\\", "a\\",
    "\\d\\M", "fffffff", "ffffffff", "FFFFFFFF", "ss.FFF", "ss.FFFF'x'", "g gg ggg", "%g", "K", "KK", "%K",
    "yyyyy y yy yyy", "MMMMM", "ddddd ddd", "hhh HHH mmm sss ttt t", "''", "\"\"", "d", "D", "m", "Y", "s", "o",
    "r", "u", "t", "T", "x", "e", "F", "f", "g", "G", "M", ":/.- ",
};
foreach (string f in formats)
{
    try { Console.WriteLine("[" + f + "] -> [" + date.ToString(f) + "] [" + whole.ToString(f) + "]"); }
    catch (Exception e) { Console.WriteLine("[" + f + "] -> " + e.GetType().Name + ": " + e.Message); }
}
