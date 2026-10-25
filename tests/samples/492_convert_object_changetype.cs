// Convert's object/IConvertible overloads, GetTypeCode and ChangeType.
using System;
using System.Globalization;

enum Level : short { Low = -3, High = 300 }
enum Big : ulong { Max = ulong.MaxValue }

sealed class Meters : IConvertible
{
    public double Value;
    public Meters(double v) { Value = v; }
    public TypeCode GetTypeCode() => TypeCode.Object;
    public bool ToBoolean(IFormatProvider? p) => Value != 0;
    public byte ToByte(IFormatProvider? p) => (byte)Value;
    public char ToChar(IFormatProvider? p) => 'm';
    public DateTime ToDateTime(IFormatProvider? p) => throw new InvalidCastException("no dates");
    public decimal ToDecimal(IFormatProvider? p) => (decimal)Value;
    public double ToDouble(IFormatProvider? p) => Value;
    public short ToInt16(IFormatProvider? p) => (short)Value;
    public int ToInt32(IFormatProvider? p) => (int)Math.Round(Value);
    public long ToInt64(IFormatProvider? p) => (long)Value;
    public sbyte ToSByte(IFormatProvider? p) => (sbyte)Value;
    public float ToSingle(IFormatProvider? p) => (float)Value;
    public string ToString(IFormatProvider? p) => Value.ToString(p) + " m";
    public object ToType(Type t, IFormatProvider? p) => t == typeof(TimeSpan) ? TimeSpan.FromSeconds(Value) : throw new InvalidCastException("to " + t.Name);
    public ushort ToUInt16(IFormatProvider? p) => (ushort)Value;
    public uint ToUInt32(IFormatProvider? p) => (uint)Value;
    public ulong ToUInt64(IFormatProvider? p) => (ulong)Value;
}

class Program
{
    static void Try(string label, Func<object?> f)
    {
        try
        {
            var r = f();
            object shown = r is char ch ? $"U+{(int)ch:X4}" : r ?? "null"; // no NUL in the recorded output
            Console.WriteLine($"{label} = {shown} ({r?.GetType().Name ?? "-"})");
        }
        catch (Exception e) { Console.WriteLine($"{label} -> {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var inv = CultureInfo.InvariantCulture;
        object[] values = ["42", " -7 ", 3.5, 2.5f, 1234.5678m, (byte)200, (sbyte)-5, (short)-300, (ushort)65000, 123u, -9L,
            18446744073709551615UL, true, 'A', new DateTime(2020, 1, 2, 3, 4, 5), Level.Low, Level.High, Big.Max,
            new Meters(2.6), TimeSpan.FromSeconds(1), null!];
        foreach (var v in values)
        {
            string name = v is null ? "null" : $"{v.GetType().Name}({Convert.ToString(v, inv)})";
            Try($"ToInt32 {name}", () => Convert.ToInt32(v));
            Try($"ToInt64 {name}", () => Convert.ToInt64(v, inv));
            Try($"ToByte {name}", () => Convert.ToByte(v));
            Try($"ToUInt64 {name}", () => Convert.ToUInt64(v));
            Try($"ToDouble {name}", () => Convert.ToDouble(v, inv));
            Try($"ToDecimal {name}", () => Convert.ToDecimal(v, inv));
            Try($"ToBoolean {name}", () => Convert.ToBoolean(v));
            Try($"ToChar {name}", () => Convert.ToChar(v));
            Try($"ToSingle {name}", () => Convert.ToSingle(v, inv));
            Try($"ToInt16 {name}", () => Convert.ToInt16(v));
            Try($"ToString {name}", () => Convert.ToString(v, inv));
            if (v is not Enum) Try($"GetTypeCode {name}", () => Convert.GetTypeCode(v));
        }
        Try("ToDateTime string", () => Convert.ToDateTime((object)"2021-03-04", inv).ToString("s", inv));
        Try("ToDateTime int", () => Convert.ToDateTime((object)5));
        Try("ToBoolean string", () => Convert.ToBoolean((object)"True"));
        Try("ToBoolean bad", () => Convert.ToBoolean((object)"yes"));

        Try("ChangeType string->double", () => Convert.ChangeType("3.25", typeof(double), inv));
        Try("ChangeType double->int", () => Convert.ChangeType(3.5, typeof(int)));
        Try("ChangeType int->string", () => Convert.ChangeType(17, typeof(string), inv));
        Try("ChangeType int->decimal", () => Convert.ChangeType(17, typeof(decimal)));
        Try("ChangeType long->byte overflow", () => Convert.ChangeType(1000L, typeof(byte)));
        Try("ChangeType enum->int", () => Convert.ChangeType(Level.High, typeof(int)));
        Try("ChangeType int->object", () => Convert.ChangeType(5, typeof(object)));
        Try("ChangeType int->int", () => Convert.ChangeType(5, typeof(int)));
        Try("ChangeType int->enum", () => Convert.ChangeType(5, typeof(Level)));
        Try("ChangeType int->int?", () => Convert.ChangeType(5, typeof(int?)));
        Try("ChangeType null->int", () => Convert.ChangeType(null, typeof(int)));
        Try("ChangeType null->string", () => Convert.ChangeType(null, typeof(string)));
        Try("ChangeType null type", () => Convert.ChangeType(5, (Type)null!));
        Try("ChangeType TimeSpan->TimeSpan", () => Convert.ChangeType(TimeSpan.Zero, typeof(TimeSpan)));
        Try("ChangeType TimeSpan->int", () => Convert.ChangeType(TimeSpan.Zero, typeof(int)));
        Try("ChangeType Meters->TimeSpan", () => Convert.ChangeType(new Meters(90), typeof(TimeSpan)));
        Try("ChangeType Meters->Guid", () => Convert.ChangeType(new Meters(90), typeof(Guid)));
        Try("ChangeType Meters->int", () => Convert.ChangeType(new Meters(90.4), typeof(int)));
        Try("ChangeType TypeCode.Int16", () => Convert.ChangeType("12", TypeCode.Int16, inv));
        Try("ChangeType TypeCode.String", () => Convert.ChangeType(1.5, TypeCode.String, inv));
        Try("ChangeType TypeCode.Object", () => Convert.ChangeType(1.5, TypeCode.Object));
        Try("ChangeType TypeCode.Empty", () => Convert.ChangeType(1.5, TypeCode.Empty));
        Try("ChangeType TypeCode.DBNull", () => Convert.ChangeType(1.5, TypeCode.DBNull));
        Try("ChangeType null TypeCode.Int32", () => Convert.ChangeType(null, TypeCode.Int32));
        Try("ChangeType null TypeCode.String", () => Convert.ChangeType(null, TypeCode.String));
        Try("ChangeType TypeCode 17", () => Convert.ChangeType(1, (TypeCode)17));
        Try("ChangeType TimeSpan TypeCode.Int32", () => Convert.ChangeType(TimeSpan.Zero, TypeCode.Int32));
    }
}
