// "R" on integer types is "G" with the same precision (found by tools/fuzz/corelib.sh): "r5" gives a
// lowercase exponent, "R0" is the shortest form, and nothing throws FormatException.
foreach (var f in new[] { "R", "r", "R0", "r7", "R05", "r05", "R99", "R1", "r2" })
    foreach (long v in new long[] { 0, 604479, -699760, long.MinValue, 99 })
        Console.WriteLine(f + " " + v.ToString(f) + " " + ((int)v).ToString(f) + " " + ((ulong)v).ToString(f) + " " + ((short)v).ToString(f));
