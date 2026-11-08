// System.Buffers.Text.Utf8Formatter.TryFormat for double, float and decimal, and Base64.EncodeToUtf8 with its
// OperationStatus results (the members System.Text.Json's Utf8JsonWriter calls when it is compiled from IL).
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;

static string Text(bool ok, byte[] buffer, int written) => ok ? Encoding.ASCII.GetString(buffer, 0, written) : "<false>";

static string Format(double value, StandardFormat format = default, int capacity = 64)
{
    var buffer = new byte[capacity];
    bool ok = Utf8Formatter.TryFormat(value, buffer, out int written, format);
    return Text(ok, buffer, written);
}

static string FormatF(float value, StandardFormat format = default, int capacity = 64)
{
    var buffer = new byte[capacity];
    bool ok = Utf8Formatter.TryFormat(value, buffer, out int written, format);
    return Text(ok, buffer, written);
}

static string FormatM(decimal value, StandardFormat format = default, int capacity = 64)
{
    var buffer = new byte[capacity];
    bool ok = Utf8Formatter.TryFormat(value, buffer, out int written, format);
    return Text(ok, buffer, written);
}

static string Encode(byte[] input, int capacity, bool final)
{
    var output = new byte[capacity];
    var status = Base64.EncodeToUtf8(input, output, out int consumed, out int produced, final);
    return $"{status} consumed={consumed} written={produced} '{Encoding.ASCII.GetString(output, 0, produced)}'";
}

Console.WriteLine(Format(0.0));
Console.WriteLine(Format(1.5));
Console.WriteLine(Format(-123.456789));
Console.WriteLine(Format(1e20));
Console.WriteLine(Format(1.0 / 3));
Console.WriteLine(Format(double.MaxValue));
Console.WriteLine(Format(double.Epsilon));
Console.WriteLine(Format(3.14159265358979, new StandardFormat('F', 3)));
Console.WriteLine(Format(1234.5, new StandardFormat('E', 2)));
Console.WriteLine(FormatF(0.1f));
Console.WriteLine(FormatF(16777216f));
Console.WriteLine(FormatF(2.5f, new StandardFormat('F', 1)));
Console.WriteLine(FormatM(0m));
Console.WriteLine(FormatM(79228162514264337593543950335m));
Console.WriteLine(FormatM(-1.2300m));
Console.WriteLine(FormatM(10.005m, new StandardFormat('F', 2)));
Console.WriteLine(Format(123456.789, capacity: 5));
Console.WriteLine(FormatM(1.25m, capacity: 2));

try { Format(1.0, new StandardFormat('Z')); }
catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

Console.WriteLine(Encode([], 8, true));
Console.WriteLine(Encode([1], 8, true));
Console.WriteLine(Encode([1, 2], 8, true));
Console.WriteLine(Encode([1, 2, 3], 8, true));
Console.WriteLine(Encode([1, 2, 3, 4], 8, true));
Console.WriteLine(Encode([1, 2, 3, 4], 8, false));
Console.WriteLine(Encode([1, 2, 3, 4, 5], 8, false));
Console.WriteLine(Encode([1, 2, 3, 4, 5], 8, true));
Console.WriteLine(Encode([1, 2, 3, 4, 5], 4, true));
Console.WriteLine(Encode([255, 254, 253, 252, 251, 250, 249], 12, true));
Console.WriteLine(Encode(Encoding.UTF8.GetBytes("Hello, base64!"), 32, true));
