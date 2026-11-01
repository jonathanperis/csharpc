// IUtf8SpanFormattable.TryFormat through a constrained generic call (RazorSlices' Write overloads format this
// way) on decimal, char, DateTime, TimeSpan, DateOnly, TimeOnly and Guid: the UTF-8 of what ToString(format,
// provider) writes, false when it doesn't fit, and char as its UTF-8 encoding (a lone surrogate throws, as
// new Rune(char) does). Then Utf8Formatter.TryFormat(bool), which RazorSlices writes booleans with.
using System;
using System.Globalization;
using System.Text;

static string Utf8<T>(T value, string format, int size = 64) where T : IUtf8SpanFormattable
{
    Span<byte> buffer = stackalloc byte[size];
    return value.TryFormat(buffer, out int written, format, CultureInfo.InvariantCulture)
        ? $"{written}:{Encoding.UTF8.GetString(buffer[..written])}"
        : $"too small ({written})";
}

static string Utf16<T>(T value, string format) where T : ISpanFormattable
{
    Span<char> buffer = stackalloc char[64];
    return value.TryFormat(buffer, out int written, format, CultureInfo.InvariantCulture) ? new string(buffer[..written]) : "too small";
}

Console.WriteLine(Utf8(1234.5678m, ""));
Console.WriteLine(Utf8(1234.5678m, "N2"));
Console.WriteLine(Utf8(-0.001m, "E3"));
Console.WriteLine(Utf8(1234.5678m, "", size: 4));
Console.WriteLine(Utf8('a', ""));
Console.WriteLine(Utf8('é', ""));
Console.WriteLine(Utf8('€', ""));
Console.WriteLine(Utf8('€', "", size: 2));
try { Console.WriteLine(Utf8('\ud800', "")); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.GetType().Name} {e.ParamName}"); }
var date = new DateTime(2024, 2, 29, 13, 45, 6, 789, DateTimeKind.Utc);
Console.WriteLine(Utf8(date, ""));
Console.WriteLine(Utf8(date, "O"));
Console.WriteLine(Utf8(date, "yyyy-MM-dd HH:mm"));
Console.WriteLine(Utf8(date, "R", size: 10));
var span = new TimeSpan(1, 2, 3, 4, 567);
Console.WriteLine(Utf8(span, ""));
Console.WriteLine(Utf8(span, "c"));
Console.WriteLine(Utf8(span, "g"));
Console.WriteLine(Utf8(-span, @"hh\:mm"));
Console.WriteLine(Utf8(span, "", size: 3));
Console.WriteLine($"{Utf16(1.5m, "F3")} {Utf16('x', "")} {Utf16(date, "d")} {Utf16(span, "t")}".Replace("t:", "t"));
var day = new DateOnly(2024, 2, 29);
var time = new TimeOnly(13, 45, 6, 789);
Console.WriteLine($"{Utf8(day, "")} {Utf8(day, "o")} {Utf8(day, "dddd MMMM")} {Utf8(day, "", size: 3)}");
Console.WriteLine($"{Utf8(time, "")} {Utf8(time, "o")} {Utf8(time, "HH-mm-ss.fff")} {Utf8(time, "", size: 3)}");
Console.WriteLine($"{Utf16(day, "")} {Utf16(time, "T")}");
var guid = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");
Console.WriteLine($"{Utf8(guid, "")} {Utf8(guid, "N")} {Utf8(guid, "B")} {Utf8(guid, "", size: 8)}");

static string Bool(bool value, char symbol, int size)
{
    Span<byte> buffer = stackalloc byte[size];
    var format = symbol == ' ' ? default : new System.Buffers.StandardFormat(symbol);
    return System.Buffers.Text.Utf8Formatter.TryFormat(value, buffer, out int written, format) ? Encoding.UTF8.GetString(buffer[..written]) : $"too small ({written})";
}
Console.WriteLine($"{Bool(true, ' ', 8)} {Bool(false, 'G', 8)} {Bool(true, 'l', 8)} {Bool(false, 'l', 8)} {Bool(false, ' ', 4)} {Bool(true, ' ', 4)}");
try { Bool(true, 'x', 8); }
catch (FormatException e) { Console.WriteLine(e.Message); }
