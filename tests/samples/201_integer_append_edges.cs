// Integer formatting at digit-count boundaries: ToString, interpolation and StringBuilder.Append
// for every integer type, including the extremes and values around powers of ten.
using System.Text;

long[] signed = { 0, 1, -1, 9, 10, -10, 99, 100, -100, 999, 1000, 9999, 10000, -99999, 100000,
    999999, 1000000, int.MaxValue, int.MinValue, int.MaxValue + 1L, int.MinValue - 1L,
    999999999999, -1000000000000, long.MaxValue, long.MinValue, long.MinValue + 1 };
ulong[] unsigned = { 0, 9, 10, 99, 100, 4294967295, 4294967296, 9999999999999999999, 10000000000000000000, ulong.MaxValue };

var sb = new StringBuilder();
foreach (long v in signed)
{
    sb.Append(v).Append('|');
    if (v >= int.MinValue && v <= int.MaxValue) sb.Append((int)v).Append('|').Append(((int)v).ToString()).Append('|');
    string interpolated = $"{v}";
    sb.Append(v.ToString()).Append('|').Append(interpolated).Append('\n');
}
foreach (ulong v in unsigned)
{
    sb.Append(v).Append('|');
    if (v <= uint.MaxValue) sb.Append((uint)v).Append('|').Append(((uint)v).ToString()).Append('|');
    sb.Append(v.ToString()).Append('\n');
}
sb.Append((short)-32768).Append(' ').Append((byte)255).Append(' ').Append((sbyte)-128).Append(' ').Append((ushort)65535).Append('\n');
Console.Write(sb.ToString());
Console.WriteLine(sb.Length);

// Growing the buffer one formatted number at a time, starting from the smallest capacity.
var grow = new StringBuilder(1);
for (int i = -50; i <= 50; i += 7) grow.Append(i * 12345679);
Console.WriteLine(grow.ToString());
Console.WriteLine($"{grow.Length} {grow.Capacity >= grow.Length}");
