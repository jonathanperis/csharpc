#:property AllowUnsafeBlocks=true
// Vector128.Shuffle (constant and run-time indices, in range and outside it) for every element type, and the Vector128 members
// that XxHash-style accumulation uses: Load/Store through pointers, Create(ulong), the 4 x uint Create, AsUInt64, the unsigned right shift,
// and the lane-wise ulong multiply, add and xor (Roslyn's copy of System.IO.Hashing is compiled from IL by csharpc, issue #252). The
// accumulation is also computed by a scalar loop and the results must agree.
using System;
using System.Runtime.Intrinsics;
using System.Text;

static string Show<T>(Vector128<T> v)
{
    var sb = new StringBuilder("<");
    for (int i = 0; i < Vector128<T>.Count; i++) sb.Append(i == 0 ? "" : ",").Append(v.GetElement(i));
    return sb.Append('>').ToString();
}

// Constant indices: reverse, broadcast, out of range (zero)
Console.WriteLine("byte  " + Show(Vector128.Shuffle(Vector128.Create((byte)10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25), Vector128.Create((byte)15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0))));
Console.WriteLine("byte0 " + Show(Vector128.Shuffle(Vector128.Create((byte)10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25), Vector128.Create((byte)3, 3, 16, 17, 255, 128, 127, 0, 1, 2, 15, 16, 100, 4, 5, 6))));
Console.WriteLine("sbyte " + Show(Vector128.Shuffle(Vector128.Create((sbyte)-1, -2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, -16), Vector128.Create((sbyte)15, 0, -1, -128, 16, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11))));
Console.WriteLine("ushort " + Show(Vector128.Shuffle(Vector128.Create((ushort)1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000), Vector128.Create((ushort)7, 0, 8, 65535, 3, 3, 1, 2))));
Console.WriteLine("short " + Show(Vector128.Shuffle(Vector128.Create((short)-1000, 2000, -3000, 4000, 5000, 6000, 7000, 8000), Vector128.Create((short)7, 0, -1, 8, 3, 3, 1, 2))));
Console.WriteLine("uint " + Show(Vector128.Shuffle(Vector128.Create(1u, 2u, 3u, 4u), Vector128.Create(3u, 3u, 4u, 0u))));
Console.WriteLine("int " + Show(Vector128.Shuffle(Vector128.Create(-1, 2, -3, 4), Vector128.Create(2, -1, 0, 3))));
Console.WriteLine("ulong " + Show(Vector128.Shuffle(Vector128.Create(0xAAAAAAAABBBBBBBBUL, 7UL), Vector128.Create(1UL, 2UL))));
Console.WriteLine("long " + Show(Vector128.Shuffle(Vector128.Create(-5L, 7L), Vector128.Create(1L, 0L))));
Console.WriteLine("float " + Show(Vector128.Shuffle(Vector128.Create(1.5f, 2.5f, 3.5f, 4.5f), Vector128.Create(3, 2, 7, 0))));
Console.WriteLine("double " + Show(Vector128.Shuffle(Vector128.Create(1.5, 2.5), Vector128.Create(1L, -1L))));

// Run-time indices (from memory), every rotation of a byte vector and of an int vector
var bytes = new byte[16];
for (int i = 0; i < 16; i++) bytes[i] = (byte)(i * 3 + 1);
var table = Vector128.LoadUnsafe(ref bytes[0]);
int checksum = 0;
for (int shift = 0; shift < 20; shift++)
{
    var idx = new byte[16];
    for (int i = 0; i < 16; i++) idx[i] = (byte)((i + shift) & 31);
    var shuffled = Vector128.Shuffle(table, Vector128.LoadUnsafe(ref idx[0]));
    for (int i = 0; i < 16; i++) checksum = checksum * 31 + shuffled.GetElement(i);
}
Console.WriteLine("byte rotations checksum " + checksum);
var ints = Vector128.Create(10, 20, 30, 40);
int sum = 0;
for (int a = -1; a < 5; a++) for (int b = -1; b < 5; b++)
    sum = unchecked(sum * 7 + (int)Vector128.Shuffle(ints, Vector128.Create(a, b, 3 - a, b ^ 1)).ExtractMostSignificantBits()) + Vector128.Shuffle(ints, Vector128.Create(a, b, 3 - a, b ^ 1)).GetElement(1);
Console.WriteLine("int shuffles " + sum);

// XxHash-style accumulation: 64-byte stripes, 2 x ulong lanes per vector
unsafe
{
    var data = new byte[256];
    for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 131 + 7);
    var secret = new byte[192];
    for (int i = 0; i < secret.Length; i++) secret[i] = (byte)(i * 17 + 3);
    var acc = new ulong[8];
    var reference = new ulong[8];
    for (int i = 0; i < 8; i++) acc[i] = reference[i] = (ulong)(i + 1) * 0x9E3779B185EBCA87UL;

    fixed (byte* d = data) fixed (byte* s = secret) fixed (ulong* a = acc)
    {
        for (int stripe = 0; stripe < 4; stripe++)
        {
            for (int lane = 0; lane < 8; lane += 2)
            {
                var input = Vector128.Load(d + stripe * 64 + lane * 8).AsUInt64();
                var key = Vector128.Load(s + stripe * 8 + lane * 8).AsUInt64();
                var mixed = input ^ key;
                var swapped = Vector128.Shuffle(input.AsUInt32(), Vector128.Create(2u, 3u, 0u, 1u)).AsUInt64();
                var product = (mixed & Vector128.Create(0xFFFFFFFFUL)) * Vector128.ShiftRightLogical(mixed, 32);
                var current = Vector128.Load(a + lane);
                Vector128.Store(current + swapped + product, a + lane);
            }
        }
    }
    for (int stripe = 0; stripe < 4; stripe++)
        for (int lane = 0; lane < 8; lane++)
        {
            ulong input = BitConverter.ToUInt64(data, stripe * 64 + lane * 8);
            ulong key = BitConverter.ToUInt64(secret, stripe * 8 + lane * 8);
            ulong mixed = input ^ key;
            ulong swapped = BitConverter.ToUInt64(data, stripe * 64 + (lane ^ 1) * 8);
            reference[lane] += swapped + (mixed & 0xFFFFFFFFUL) * (mixed >> 32);
        }
    bool same = true;
    for (int i = 0; i < 8; i++) same &= acc[i] == reference[i];
    Console.WriteLine("accumulate " + (same ? "same" : "DIFFERENT") + " " + string.Join(",", acc));
}
Console.WriteLine(Show(Vector128.Create(1u, 2u, 3u, 4u).AsUInt64()) + " " + Show(Vector128.Create(0x0102030405060708UL).AsByte()));
