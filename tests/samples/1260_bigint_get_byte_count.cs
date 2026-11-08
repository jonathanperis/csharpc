// BigInteger.GetByteCount(isUnsigned): the length ToByteArray() has for the signed form, the magnitude's bytes for the
// unsigned one (zero takes one byte; a negative value has no unsigned representation). Npgsql's numeric converter sizes its
// buffers with it.
using System.Numerics;

string[] values =
{
    "0", "1", "127", "128", "255", "256", "-1", "-128", "-129", "-255", "-256", "32767", "32768", "65535", "65536", "-32768", "-32769",
    "8388607", "8388608", "16777215", "16777216", "-8388608", "-8388609",
    "2147483647", "2147483648", "4294967295", "4294967296", "-2147483648", "-2147483649",
    "9223372036854775807", "9223372036854775808", "18446744073709551615", "18446744073709551616",
    "-9223372036854775808", "-9223372036854775809",
    "79228162514264337593543950335", "340282366920938463463374607431768211455", "340282366920938463463374607431768211456",
    "-340282366920938463463374607431768211456", "-340282366920938463463374607431768211457",
    "1" + new string('0', 80), "-1" + new string('0', 80),
};
foreach (string s in values)
{
    BigInteger v = BigInteger.Parse(s);
    string unsigned;
    try { unsigned = v.GetByteCount(true).ToString(); }
    catch (Exception e) { unsigned = e.GetType().Name + ": " + e.Message; }
    Console.WriteLine($"{(s.Length > 40 ? s.Substring(0, 6) + ".." + s.Length : s)} signed={v.GetByteCount()} explicit={v.GetByteCount(false)} toByteArray={v.ToByteArray().Length} unsigned={unsigned}");
}
Console.WriteLine(BigInteger.Zero.GetByteCount() + " " + BigInteger.One.GetByteCount(true) + " " + BigInteger.MinusOne.GetByteCount());
Console.WriteLine((BigInteger.Pow(2, 64) - 1).GetByteCount(true) + " " + BigInteger.Pow(2, 64).GetByteCount(true) + " " + (-BigInteger.Pow(2, 64)).GetByteCount());
