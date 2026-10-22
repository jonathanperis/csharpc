int ok = 0;
foreach (var s in new[] { "1", "x", "3", "", "5" }) {
    try { if (s == "") continue; ok += int.Parse(s); }
    catch (FormatException) { Console.WriteLine("bad: " + s); continue; }
    finally { Console.WriteLine("checked " + (s == "" ? "<empty>" : s)); }
}
Console.WriteLine(ok);
