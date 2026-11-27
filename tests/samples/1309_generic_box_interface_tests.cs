// `box T; isinst I`, `castclass I` and `callvirt I.M` in generic code (the shape of `value is IFormattable` and of
// ((ISpanFormattable)value).TryFormat in the framework's AppendFormatted<T>): the answers for the built-in value types, enums, nullables
// and user structs must be .NET's, and a call through the interface must reach the type's own implementation.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

enum Color { Red, Green }

struct Plain { public int X; }

struct Formattable : IFormattable, IComparable<Formattable>
{
    public int X;
    public string ToString(string format, IFormatProvider provider) => "F" + X + (format ?? "-");
    public int CompareTo(Formattable other) => X.CompareTo(other.X);
}

class Program
{
    static string Tests<T>(T value) =>
        string.Join(" ", new[]
        {
            (value is IFormattable).ToString(),
            ((object)value is ISpanFormattable).ToString(),
            (value is IComparable).ToString(),
            (value is IComparable<T>).ToString(),
            (value is IEquatable<T>).ToString(),
            (value is IConvertible).ToString(),
            (value is ValueType).ToString(),
            (value is Enum).ToString(),
            (value is IUtf8SpanFormattable).ToString(),
            (value is IDisposable).ToString(),
        });

    static string Format<T>(T value, string format)
    {
        if (value is IFormattable formattable) return formattable.ToString(format, null);
        return value?.ToString() ?? "null";
    }

    static string Span<T>(T value)
    {
        if (value is ISpanFormattable spanFormattable)
        {
            Span<char> buffer = stackalloc char[64];
            return spanFormattable.TryFormat(buffer, out var written, default, null) ? new string(buffer.Slice(0, written)) : "too long";
        }
        return "not span formattable";
    }

    static void Show<T>(string name, T value, string format = null) =>
        Console.WriteLine($"{name}: {Tests(value)} | {Format(value, format)} | {Span(value)}");

    static void Main()
    {
        Show("int", 42, "X");
        Show("uint", 42u, "D5");
        Show("long", -7L);
        Show("byte", (byte)200, "X2");
        Show("sbyte", (sbyte)-5);
        Show("short", (short)-3);
        Show("char", 'c');
        Show("bool", true);
        Show("double", 1.5, "F3");
        Show("float", 2.5f);
        Show("decimal", 3.25m, "F1");
        Show("Int128", Int128.MaxValue);
        Show("DateTime", new DateTime(2026, 11, 10, 8, 30, 0, DateTimeKind.Utc), "yyyy-MM-dd");
        Show("TimeSpan", TimeSpan.FromMinutes(90));
        Show("Guid", new Guid("11111111-2222-3333-4444-555555555555"), "N");
        Show("plain struct", new Plain { X = 1 });
        Show("formattable struct", new Formattable { X = 9 }, "q");
        Show("string", "text");
        Show("object", new object());
        Show("tuple", (1, "a"));

        // The framework's own users of those shapes
        var builder = new StringBuilder();
        builder.Append($"{42} {-7L} {'c'} {true} {1.5} {Color.Red} {new Formattable { X = 3 }} {"s"} {(int?)8} {(Half)2.5f}");
        builder.Append($" [{12,6}] [{12,-6}] [{255:X4}] [{3.14159:F2}]");
        Console.WriteLine(builder.ToString());
        Console.WriteLine(string.Format("{0} {1:X} {2:F1} {3}", 10, 255, 2.55, Color.Green));
    }
}
