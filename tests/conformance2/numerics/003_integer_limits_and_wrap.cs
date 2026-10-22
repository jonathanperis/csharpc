int imax = int.MaxValue, imin = int.MinValue; uint umax = uint.MaxValue; long lmin = long.MinValue; byte bmax = byte.MaxValue; short smin = short.MinValue;
Console.WriteLine($"{imax + 1} {umax + 1} {lmin - 1} {(byte)(bmax + 1)} {smin - 1}");
Console.WriteLine($"{-imin} {imin * -1} {Math.Abs((long)imin)}");
ulong u = 0; u--;
Console.WriteLine(u);
