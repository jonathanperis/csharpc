// Convert from CoreLib's IL (#423): enum values through Convert and IConvertible (type codes, ChangeType to and from their
// underlying types and strings), conversions each source type's IConvertible refuses (InvalidCastException with .NET's texts,
// reached through the typed overloads and through boxes), Convert.ToString(object) with a provider, and base conversions.
using System;
using System.Globalization;

enum Small : byte { A = 1, B = 200 }
enum Signed : long { Low = -5, High = 1L << 40 }

static class Program
{
    static void Try(string label, Func<object> f)
    {
        try { Console.WriteLine($"{label}: {f()} ({f()?.GetType().Name})"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var inv = CultureInfo.InvariantCulture;
        Try("GetTypeCode Small", () => Convert.GetTypeCode(Small.B));
        Try("GetTypeCode Signed", () => Convert.GetTypeCode(Signed.Low));
        Try("ToInt32 Small", () => Convert.ToInt32(Small.B));
        Try("ToInt64 Signed", () => Convert.ToInt64(Signed.High));
        Try("ToByte Signed", () => Convert.ToByte(Signed.Low));
        Try("ChangeType Small->int", () => Convert.ChangeType(Small.B, typeof(int)));
        Try("ChangeType Signed->string", () => Convert.ChangeType(Signed.Low, typeof(string), inv));
        Try("ChangeType Small->Small", () => Convert.ChangeType(Small.A, typeof(Small)));
        Try("ChangeType Small->Enum", () => Convert.ChangeType(Small.A, typeof(Enum)));
        Try("ToDateTime Small", () => Convert.ToDateTime(Small.A));
        Try("ToBoolean char", () => Convert.ToBoolean('a'));
        Try("ToChar double", () => Convert.ToChar(1.0));
        Try("ToDateTime int", () => Convert.ToDateTime(5));
        Try("ToInt32 DateTime", () => Convert.ToInt32(DateTime.MinValue));
        Try("IConvertible char->bool", () => ((IConvertible)'a').ToBoolean(null));
        Try("IConvertible decimal->char", () => ((IConvertible)1.5m).ToChar(null));
        Try("IConvertible int->Guid", () => ((IConvertible)5).ToType(typeof(Guid), null));
        Try("IConvertible int->object", () => ((IConvertible)5).ToType(typeof(object), null));
        Try("ChangeType double->Uri", () => Convert.ChangeType(1.5, typeof(Uri)));
        Try("ChangeType null->int", () => Convert.ChangeType(null, typeof(int)));
        Try("ToString(object, provider)", () => Convert.ToString((object)(-1.5), new NumberFormatInfo { NegativeSign = "~", NumberDecimalSeparator = "," }));
        Try("ToString(base 2)", () => Convert.ToString(-5, 2));
        Try("ToInt16 hex", () => Convert.ToInt16("8000", 16));
        Try("ToByte bad base", () => Convert.ToByte("1", 3));
        Try("ToInt32 empty", () => Convert.ToInt32("", 16));
        Try("ToUInt64 negative", () => Convert.ToUInt64("-1", 10));
        Try("FromHexString odd", () => Convert.FromHexString("ABC"));
        Try("FromBase64 bad", () => Convert.FromBase64String("AB$="));
        Try("ToBase64 lines", () => Convert.ToBase64String(new byte[60], Base64FormattingOptions.InsertLineBreaks).Length);
        Try("IsDBNull", () => Convert.IsDBNull(DBNull.Value));
    }
}
