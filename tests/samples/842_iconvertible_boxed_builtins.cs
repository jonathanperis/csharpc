// IConvertible through the interface on boxed enums, primitives, decimal, DateTime and strings, as System.Data's
// DbConnectionStringBuilder converts its values ((IConvertible)value).ToString(CultureInfo.InvariantCulture)),
// including the conversions that throw.
using System.Globalization;

object[] values = { DayOfWeek.Friday, System.IO.FileAccess.ReadWrite, 5432, true, 'x', (byte)7, (short)-3, 12345678901L, 3.5, 2.25f, 19.99m, "42", (ulong)18, new DateTime(2024, 2, 29, 13, 45, 0) };
foreach (var value in values)
{
    var c = (IConvertible)value;
    Console.WriteLine($"{value.GetType().Name}: {c.GetTypeCode()} '{c.ToString(CultureInfo.InvariantCulture)}'");
    Try("ToInt32", () => c.ToInt32(CultureInfo.InvariantCulture));
    Try("ToDouble", () => c.ToDouble(CultureInfo.InvariantCulture));
    Try("ToBoolean", () => c.ToBoolean(CultureInfo.InvariantCulture));
    Try("ToChar", () => c.ToChar(null));
    Try("ToDecimal", () => c.ToDecimal(null));
    Try("ToDateTime", () => c.ToDateTime(CultureInfo.InvariantCulture));
    Try("ToType(long)", () => c.ToType(typeof(long), CultureInfo.InvariantCulture));
    Try("ToType(string)", () => c.ToType(typeof(string), CultureInfo.InvariantCulture));
}

IConvertible big = 300;
Try("ToByte(300)", () => big.ToByte(null));
IConvertible text = "not a number";
Try("string ToInt32", () => text.ToInt32(CultureInfo.InvariantCulture));
IConvertible flag = "True";
Try("string ToBoolean", () => flag.ToBoolean(null));

static void Try(string what, Func<object> f)
{
    try { Console.WriteLine($"  {what}: {Convert.ToString(f(), CultureInfo.InvariantCulture)}"); }
    catch (Exception e) { Console.WriteLine($"  {what}: {e.GetType().Name}: {e.Message}"); }
}
