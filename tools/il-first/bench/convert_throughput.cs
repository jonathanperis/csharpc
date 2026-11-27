// Convert throughput, the program behind the NativeAOT column of docs/il-first/System.Convert.md (tools/il-first/measure-aot.sh):
// numeric conversions with rounding and overflow checks, base 2/8/16 parsing and formatting, conversions of boxed values through
// IConvertible, ChangeType, Base64 and hex text. The output is checksums only.
using System;
using System.Globalization;

long check = 0;
var inv = CultureInfo.InvariantCulture;
object[] boxes = { 42, 3.75, 12.5m, (short)-7, 255L, "123", true, 'x' };
byte[] data = new byte[3000];
for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 13 + 5);
for (int round = 0; round < 300; round++)
{
    for (int i = 0; i < 2000; i++)
    {
        double d = i * 0.37 - 300;
        check += Convert.ToInt32(d) + Convert.ToInt64(d * 1000) + Convert.ToByte(i & 0xFF) + Convert.ToInt16(i - 1000);
        string hex = Convert.ToString(i * 7919, 16);
        check += Convert.ToInt32(hex, 16) + Convert.ToString(i, 2).Length + Convert.ToInt64(Convert.ToString(i, 8), 8);
    }
    foreach (var box in boxes)
    {
        check += box is not char ? Convert.ToInt32(box, inv) : Convert.ToInt32((char)box);
        check += Convert.ToString(box, inv)!.Length + (Convert.ChangeType(box is char ? 1 : box, typeof(double), inv) is double ? 1 : 0);
    }
    string b64 = Convert.ToBase64String(data);
    check += b64.Length + Convert.FromBase64String(b64)[round] + Convert.ToHexString(data, 0, 100).Length;
}
Console.WriteLine(check);
