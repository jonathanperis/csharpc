// Vector128<T> operations on every element size (issue #252): arithmetic, bitwise, shifts, comparisons and masks, selects,
// element access, loads and stores, element views, and the vector search the framework's span helpers are built from
// (Equals + ExtractMostSignificantBits + trailing zero count), written once with a Vector128 path and once scalar. The two
// must agree whether or not Vector128.IsHardwareAccelerated: the flag itself is not printed.
using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Text;

static string Show<T>(Vector128<T> v)
{
    var sb = new StringBuilder("<");
    for (int i = 0; i < Vector128<T>.Count; i++) sb.Append(i == 0 ? "" : ",").Append(v.GetElement(i));
    return sb.Append('>').ToString();
}

static void Arithmetic<T>(string label, Vector128<T> a, Vector128<T> b) where T : struct
{
    Console.WriteLine($"{label} a={Show(a)} b={Show(b)}");
    Console.WriteLine($"  add={Show(Vector128.Add(a, b))} sub={Show(Vector128.Subtract(a, b))} and={Show(Vector128.BitwiseAnd(a, b))} or={Show(Vector128.BitwiseOr(a, b))} xor={Show(Vector128.Xor(a, b))}");
    Console.WriteLine($"  andnot={Show(Vector128.AndNot(a, b))} not={Show(Vector128.OnesComplement(a))} eq={Show(Vector128.Equals(a, b))} gt={Show(Vector128.GreaterThan(a, b))} lt={Show(Vector128.LessThan(a, b))} ge={Show(Vector128.GreaterThanOrEqual(a, b))} le={Show(Vector128.LessThanOrEqual(a, b))}");
    Console.WriteLine($"  msb(eq)={Vector128.ExtractMostSignificantBits(Vector128.Equals(a, b))} msb(gt)={Vector128.ExtractMostSignificantBits(Vector128.GreaterThan(a, b))} == {a == b} != {a != b} all={Vector128.EqualsAll(a, a)} any={Vector128.EqualsAny(a, b)}");
    Console.WriteLine($"  select={Show(Vector128.ConditionalSelect(Vector128.GreaterThan(a, b), a, b))} with={Show(a.WithElement(1, b.GetElement(0)))} scalar={a.ToScalar()}");
}

Arithmetic("byte", Vector128.Create((byte)10, 200, 3, 255, 0, 1, 2, 128, 7, 7, 8, 9, 100, 101, 102, 103), Vector128.Create((byte)10, 100, 4, 255, 1, 1, 3, 127, 7, 8, 8, 8, 100, 99, 102, 104));
Arithmetic("sbyte", Vector128.Create((sbyte)-10, 100, -3, 127, 0, -1, 2, -128, 7, 7, 8, 9, -100, 101, -102, 103), Vector128.Create((sbyte)10, -100, 4, -128, 1, 1, 3, 127, 7, 8, 8, 8, -100, 99, 102, 104));
Arithmetic("ushort", Vector128.Create((ushort)1, 65535, 300, 0, 40000, 5, 6, 7), Vector128.Create((ushort)1, 65534, 400, 0, 39999, 5, 7, 6));
Arithmetic("short", Vector128.Create((short)-1, 32767, -300, 0, -32768, 5, 6, 7), Vector128.Create((short)1, -32768, 400, 0, -32767, 5, 7, 6));
Arithmetic("int", Vector128.Create(5, -7, int.MaxValue, int.MinValue), Vector128.Create(5, 7, 1, -1));
Arithmetic("uint", Vector128.Create(5u, 7u, uint.MaxValue, 0u), Vector128.Create(5u, 8u, 1u, 0u));
Arithmetic("long", Vector128.Create(5L, long.MinValue), Vector128.Create(5L, long.MaxValue));
Arithmetic("ulong", Vector128.Create(5UL, ulong.MaxValue), Vector128.Create(6UL, ulong.MaxValue));
Arithmetic("float", Vector128.Create(1.5f, -2.5f, float.NaN, 0f), Vector128.Create(1.5f, 2.5f, float.NaN, -0f));
Arithmetic("double", Vector128.Create(1.5, double.NaN), Vector128.Create(1.5, 0.0));

// Multiply, shifts, min/max, negate, abs
var ia = Vector128.Create(3, -4, 100000, 7);
var ib = Vector128.Create(5, 6, 100000, -7);
Console.WriteLine($"int mul={Show(ia * ib)} neg={Show(-ia)} min={Show(Vector128.Min(ia, ib))} max={Show(Vector128.Max(ia, ib))} shl={Show(ia << 3)} shr={Show(ia >> 1)} ushr={Show(ia >>> 28)} shl35={Show(Vector128.ShiftLeft(ia, 35))}");
var fa = Vector128.Create(1.5f, -2.5f, 3f, 4f);
Console.WriteLine($"float mul={Show(fa * Vector128.Create(2f))} div={Show(fa / Vector128.Create(2f))} neg={Show(-fa)} scalarmul={Show(fa * 3f)}");
var ua = Vector128.Create((ushort)0x8001, 2, 3, 0xFFFF, 5, 6, 7, 8);
Console.WriteLine($"ushort shr={Show(ua >> 1)} shl={Show(ua << 1)} min={Show(Vector128.Min(ua, Vector128.Create((ushort)4)))} max={Show(Vector128.Max(ua, Vector128.Create((ushort)4)))}");
var sa = Vector128.Create((short)-32768, 2, -3, 4, 5, 6, 7, 8);
Console.WriteLine($"short shr={Show(sa >> 1)} ushr={Show(sa >>> 1)} min={Show(Vector128.Min(sa, Vector128.Create((short)0)))}");

// Constants, load, store, views
Console.WriteLine($"zero={Show(Vector128<int>.Zero)} ones={Show(Vector128<byte>.AllBitsSet)} count={Vector128<byte>.Count},{Vector128<short>.Count},{Vector128<int>.Count},{Vector128<long>.Count},{Vector128<double>.Count}");
var buffer = new byte[40];
for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i * 7 + 1);
var loaded = Vector128.LoadUnsafe(ref buffer[3]);
Console.WriteLine($"load={Show(loaded)} offset={Show(Vector128.LoadUnsafe(ref buffer[0], 5))} as-ushort={Show(loaded.AsUInt16())} as-int={Show(loaded.AsInt32())} as-long={Show(loaded.AsInt64())} as-float={Show(Vector128.Create(1f, 2f, 3f, 4f).AsUInt32())}");
var target = new byte[24];
loaded.StoreUnsafe(ref target[2]);
Console.WriteLine(BitConverter.ToString(target));
var wide = new long[4];
Vector128.StoreUnsafe(Vector128.Create(0x0102030405060708L, 0x1112131415161718L), ref wide[0], 1);
Console.WriteLine(string.Join(",", wide));
var scratch = new int[6];
Vector128.Create(9, 8, 7, 6).StoreUnsafe(ref scratch[1]);
Console.WriteLine(string.Join(",", scratch));
try { Console.WriteLine(ia.GetElement(4)); } catch (ArgumentOutOfRangeException) { Console.WriteLine("GetElement(4): ArgumentOutOfRangeException"); }
Console.WriteLine($"element[2]={ia[2]} create-scalar={Show(Vector128.CreateScalar(42))}");

// The search the framework's span helpers use, with a Vector128 path and a scalar one.
static int IndexOfVector(byte[] data, byte value)
{
    int i = 0;
    var needle = Vector128.Create(value);
    for (; i <= data.Length - 16; i += 16)
    {
        uint mask = Vector128.Equals(Vector128.LoadUnsafe(ref data[i]), needle).ExtractMostSignificantBits();
        if (mask != 0) return i + BitOperations.TrailingZeroCount(mask);
    }
    for (; i < data.Length; i++) if (data[i] == value) return i;
    return -1;
}

static int IndexOfScalar(byte[] data, byte value)
{
    for (int i = 0; i < data.Length; i++) if (data[i] == value) return i;
    return -1;
}

static int IndexOf(byte[] data, byte value) => Vector128.IsHardwareAccelerated ? IndexOfVector(data, value) : IndexOfScalar(data, value);

var haystack = new byte[100];
for (int i = 0; i < haystack.Length; i++) haystack[i] = (byte)(i % 37);
int mismatches = 0, total = 0;
for (int length = 0; length <= 100; length++)
{
    var slice = haystack.AsSpan(0, length).ToArray();
    for (int value = 0; value < 40; value++)
    {
        total++;
        if (IndexOfVector(slice, (byte)value) != IndexOfScalar(slice, (byte)value) || IndexOf(slice, (byte)value) != IndexOfScalar(slice, (byte)value)) mismatches++;
    }
}
Console.WriteLine($"searches={total} mismatches={mismatches} first-37={IndexOf(haystack, 36)} missing={IndexOf(haystack, 99)}");
