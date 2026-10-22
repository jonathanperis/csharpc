// "R" on decimal means "G" (with its precision); "G0"/"R0" count the decimal's trailing zeros as
// digits but do not print them. Double "r" writes a lowercase exponent.
decimal[] values = { 1.2300m, -12345.678m, 0.000012m, 0m, 0.00m, 1e20m, -12.50m, 123456789.987654321m };
foreach (string f in new[] { "R", "r", "R0", "r3", "R2", "R30", "G", "G0", "g0", "G3" })
{
    var line = new System.Text.StringBuilder(f.PadRight(4));
    foreach (decimal v in values) line.Append(" [").Append(v.ToString(f)).Append(']');
    Console.WriteLine(line);
}
foreach (double v in new[] { 1e-300, -2.6922E-13, 7.69811E-11, 6.713410323444542E+66, 0.5, 1e15, 1e16 })
    Console.WriteLine(v.ToString("r") + " " + v.ToString("R") + " " + v.ToString("r9") + " " + v.ToString("g") + " " + v.ToString("G"));
