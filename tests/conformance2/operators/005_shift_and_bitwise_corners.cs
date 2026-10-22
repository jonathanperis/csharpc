int x = -8;
Console.WriteLine($"{x >> 1} {x >>> 1} {x << 31} {1 << 32} {1L << 63} {~0u} {5 & -2} {(uint)x >> 28}");
byte b = 0b1010_1010;
Console.WriteLine($"{(byte)~b} {b ^ 0xFF} {(b & 0x0F) | 0x30}");
