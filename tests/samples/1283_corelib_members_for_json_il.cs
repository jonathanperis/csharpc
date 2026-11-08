// CoreLib members System.Text.Json's IL calls when it is compiled from the framework (the IL-first A/B, #187):
// Utf8Parser for floating point, decimal, Guid and TimeSpan; Utf8Formatter for Guid and TimeSpan; Utf8.IsValid;
// Base64.DecodeFromUtf8; MemoryExtensions.Sort(keys, items) and LastIndexOfAnyExcept; List.AddRange(ReadOnlySpan);
// the UTF-8 TryFormat of Half, Int128 and Version; and Dictionary alternate lookups.
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using System.Text.Unicode;

static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

static void ParseDouble(string s, char format = default)
{
    bool ok = Utf8Parser.TryParse(Bytes(s), out double value, out int consumed, format);
    Console.WriteLine($"double '{s}' {(int)format} -> {ok} {value:R} consumed={consumed}");
}

static void ParseDecimal(string s)
{
    bool ok = Utf8Parser.TryParse(Bytes(s), out decimal value, out int consumed);
    Console.WriteLine($"decimal '{s}' -> {ok} {value} consumed={consumed}");
}

ParseDouble("3.14159");
ParseDouble("-0.5e3 tail");
ParseDouble("1E-7,");
ParseDouble("12abc");
ParseDouble("abc");
ParseDouble("");
ParseDouble("1.5e", 'G');
ParseDouble("1.5e");
ParseDouble("1.5", 'E');
ParseDouble("1.5e+", 'e');
ParseDouble("1.5e+3", 'E');
ParseDouble("-.5");
ParseDouble("5.");
ParseDouble("+7");
ParseDouble("2.5e2", 'F');
ParseDouble("0.30000000000000004");
ParseDouble("1e400");
ParseDecimal("1234.5000");
ParseDecimal("-0.0000001");
ParseDecimal("79228162514264337593543950336");
ParseDecimal("7.5x");
{
    bool ok = Utf8Parser.TryParse(Bytes("2.5"), out float f, out int c);
    Console.WriteLine($"float {ok} {f:R} {c}");
}
{
    bool ok = Utf8Parser.TryParse(Bytes("0f8fad5b-d9cb-469f-a165-70867728950e!"), out Guid g, out int c);
    Console.WriteLine($"guid {ok} {g} {c}");
    ok = Utf8Parser.TryParse(Bytes("0f8fad5bd9cb469fa16570867728950e"), out g, out c, 'N');
    Console.WriteLine($"guid N {ok} {g} {c}");
    ok = Utf8Parser.TryParse(Bytes("0f8fad5b-d9cb-469f-a165-70867728950"), out g, out c);
    Console.WriteLine($"guid short {ok} {g} {c}");
    ok = Utf8Parser.TryParse(Bytes("{0f8fad5b-d9cb-469f-a165-70867728950e}"), out g, out c, 'B');
    Console.WriteLine($"guid B {ok} {g} {c}");
}
{
    bool ok = Utf8Parser.TryParse(Bytes("1.02:03:04.0050000"), out TimeSpan t, out int c);
    Console.WriteLine($"timespan {ok} {t} {c}");
    ok = Utf8Parser.TryParse(Bytes("-00:00:30"), out t, out c);
    Console.WriteLine($"timespan {ok} {t} {c}");
    ok = Utf8Parser.TryParse(Bytes("garbage"), out t, out c);
    Console.WriteLine($"timespan {ok} {t} {c}");
}

static string Format(Guid value, StandardFormat format = default)
{
    var buffer = new byte[64];
    bool ok = Utf8Formatter.TryFormat(value, buffer, out int written, format);
    return ok ? Encoding.ASCII.GetString(buffer, 0, written) : "<false>";
}

static string FormatSpan(TimeSpan value, StandardFormat format = default)
{
    var buffer = new byte[64];
    bool ok = Utf8Formatter.TryFormat(value, buffer, out int written, format);
    return ok ? Encoding.ASCII.GetString(buffer, 0, written) : "<false>";
}

var guid = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");
Console.WriteLine(Format(guid));
Console.WriteLine(Format(guid, new StandardFormat('N')));
Console.WriteLine(Format(guid, new StandardFormat('B')));
Console.WriteLine(Format(guid, new StandardFormat('P')));
try { Format(guid, new StandardFormat('X')); }
catch (FormatException e) { Console.WriteLine(e.Message); }
var span = new TimeSpan(1, 2, 3, 4, 5);
Console.WriteLine(FormatSpan(span));
Console.WriteLine(FormatSpan(span, new StandardFormat('G')));
Console.WriteLine(FormatSpan(span, new StandardFormat('g')));
Console.WriteLine(FormatSpan(-span, new StandardFormat('c')));

Console.WriteLine(Utf8.IsValid(Bytes("plain ascii and ünïcödé 日本語 😀")));
Console.WriteLine(Utf8.IsValid(new byte[] { 0x61, 0xC0, 0x80 }));
Console.WriteLine(Utf8.IsValid(new byte[] { 0xED, 0xA0, 0x80 }));
Console.WriteLine(Utf8.IsValid(new byte[] { 0xF4, 0x90, 0x80, 0x80 }));
Console.WriteLine(Utf8.IsValid(new byte[] { 0xE6, 0x97 }));
Console.WriteLine(Utf8.IsValid(ReadOnlySpan<byte>.Empty));

static string Decode(string text, int capacity, bool final = true)
{
    var output = new byte[capacity];
    var status = Base64.DecodeFromUtf8(Bytes(text), output, out int consumed, out int written, final);
    return $"{status} consumed={consumed} written={written} {BitConverter.ToString(output, 0, written)}";
}

Console.WriteLine(Decode("AQIDBA==", 16));
Console.WriteLine(Decode("AQID", 16));
Console.WriteLine(Decode("AQI=", 16));
Console.WriteLine(Decode("AQ==", 16));
Console.WriteLine(Decode("", 16));
Console.WriteLine(Decode("AQID BAUG\r\n", 16));
Console.WriteLine(Decode("AQIDBAUG", 4));
Console.WriteLine(Decode("AQIDBA", 16, false));
Console.WriteLine(Decode("AQIDBA", 16, true));
Console.WriteLine(Decode("AQ!DBAUG", 16));
Console.WriteLine(Decode("AQIDBA==x", 16));
{
    var inPlace = Bytes("SGVsbG8sIGJhc2U2NCE=");
    var status = Base64.DecodeFromUtf8InPlace(inPlace, out int written);
    Console.WriteLine($"{status} {written} {Encoding.UTF8.GetString(inPlace, 0, written)}");
}

{
    var keys = new[] { (5, 2), (3, 0), (5, 1), (1, 3) };
    var items = new[] { "a", "b", "c", "d" };
    MemoryExtensions.Sort(keys.AsSpan(), items.AsSpan());
    Console.WriteLine(string.Join(",", keys) + " " + string.Join("", items));
}
Console.WriteLine(((ReadOnlySpan<byte>)new byte[] { 1, 2, 9, 9 }).LastIndexOfAnyExcept((byte)9));
Console.WriteLine(((ReadOnlySpan<byte>)new byte[] { 9, 9 }).LastIndexOfAnyExcept((byte)9));
{
    var list = new List<int> { 1 };
    CollectionExtensions.AddRange(list, new int[] { 2, 3 }.AsSpan());
    Console.WriteLine(string.Join(",", list));
}

{
    var buffer = new byte[40];
    Console.WriteLine(((Half)1.5f).TryFormat(buffer, out int n, "F2", null) + " " + Encoding.ASCII.GetString(buffer, 0, n));
    Console.WriteLine(Int128.Parse("-170141183460469231731687303715884105728").TryFormat(buffer, out n, default, null) + " " + Encoding.ASCII.GetString(buffer, 0, n));
    Console.WriteLine(UInt128.MaxValue.TryFormat(buffer, out n, default, null) + " " + Encoding.ASCII.GetString(buffer, 0, n));
    Console.WriteLine(new Version(1, 2, 3).TryFormat(buffer, out n) + " " + Encoding.ASCII.GetString(buffer, 0, n));
    Console.WriteLine(new Version(10, 20).TryFormat(new byte[2], out n) + " " + n);
}

{
    var map = new Dictionary<string, int> { ["alpha"] = 1, ["beta"] = 2 };
    var lookup = map.GetAlternateLookup<ReadOnlySpan<char>>();
    Console.WriteLine(lookup.TryGetValue("alpha".AsSpan(), out int v) + " " + v);
    Console.WriteLine(lookup.TryGetValue("gamma".AsSpan(), out v) + " " + v);
    Console.WriteLine(lookup.ContainsKey("beta".AsSpan()) + " " + lookup["beta".AsSpan()]);
    lookup["delta".AsSpan()] = 4;
    Console.WriteLine(lookup.TryAdd("delta".AsSpan(), 5) + " " + lookup.TryAdd("eps".AsSpan(), 6) + " " + map.Count + " " + map["delta"] + " " + map["eps"]);
    Console.WriteLine(lookup.TryGetValue("beta".AsSpan(), out string actual, out v) + " " + actual + " " + v);
    Console.WriteLine(lookup.Remove("alpha".AsSpan()) + " " + lookup.Remove("alpha".AsSpan()) + " " + map.Count);
    Console.WriteLine(map.TryGetAlternateLookup<ReadOnlySpan<char>>(out _) + " " + new Dictionary<int, int>().TryGetAlternateLookup<ReadOnlySpan<char>>(out _));
    try { _ = lookup["missing".AsSpan()]; }
    catch (KeyNotFoundException e) { Console.WriteLine(e.Message); }
    var utf8 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Key"] = 7 };
    Console.WriteLine(utf8.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue("kEY".AsSpan(), out v) + " " + v);
}
