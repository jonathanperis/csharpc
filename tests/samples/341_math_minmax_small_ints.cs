// Math.Max/Min on the small integer types and native ints (System.Collections.Immutable calls Math.Max(byte, byte)).
byte a = 200, b = 7; sbyte c = -5, d = 3; short e = -300; ushort f = 60000; nint g = -9; nuint h = 9;
Console.WriteLine($"{Math.Max(a, b)} {Math.Min(a, b)} {Math.Max(c, d)} {Math.Min(c, d)} {Math.Min(e, (short)1)} {Math.Max(f, (ushort)1)} {(long)Math.Max(g, 0)} {(ulong)Math.Min(h, 3)}");
