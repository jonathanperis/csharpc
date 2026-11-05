// System.Text.Json's source-generated contexts name converters beyond the numeric primitives: Int128/UInt128,
// Half, DateOnly, TimeOnly, Uri, Version and ReadOnlyMemory<T>/Memory<T> (the generator emits
// JsonMetadataServices.Int128Converter, CreateReadOnlyMemoryInfo and so on). Round trips, strictness and errors.
// Also Int128/UInt128/Half Parse.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

record Rec(Int128 I, UInt128 U, DateOnly D, TimeOnly T, Uri Uri, Version V, ReadOnlyMemory<byte> Bytes, ReadOnlyMemory<float> Floats, Half H);

[JsonSerializable(typeof(Rec))]
[JsonSerializable(typeof(Int128))]
[JsonSerializable(typeof(UInt128))]
[JsonSerializable(typeof(Half))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
[JsonSerializable(typeof(Uri))]
[JsonSerializable(typeof(Version))]
[JsonSerializable(typeof(ReadOnlyMemory<byte>))]
[JsonSerializable(typeof(Memory<byte>))]
[JsonSerializable(typeof(ReadOnlyMemory<float>))]
[JsonSerializable(typeof(ReadOnlyMemory<int>))]
[JsonSerializable(typeof(Memory<int>))]
[JsonSerializable(typeof(Dictionary<Int128, int>))]
[JsonSerializable(typeof(Dictionary<DateOnly, int>))]
[JsonSerializable(typeof(Dictionary<Half, int>))]
partial class Ctx : JsonSerializerContext;

class Program
{
    static void Show(string name, Func<object> f)
    {
        try { Console.WriteLine(name + ": " + f()); }
        catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static string Ser<T>(System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, T value) => JsonSerializer.Serialize(value, info);
    static T Des<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) => JsonSerializer.Deserialize(json, info)!;

    static void Main()
    {
        var r = new Rec(Int128.Parse("12345678901234567890123"), (UInt128)5, new DateOnly(2026, 11, 4), new TimeOnly(13, 30), new Uri("https://example.com/"), new Version(1, 2, 3),
            new byte[] { 1, 2, 3 }, new float[] { 1.5f }, (Half)1.5f);
        var json = JsonSerializer.Serialize(r, Ctx.Default.Rec);
        Console.WriteLine(json);
        var back = JsonSerializer.Deserialize(json, Ctx.Default.Rec)!;
        Console.WriteLine(JsonSerializer.Serialize(back, Ctx.Default.Rec) == json);

        Show("i128", () => Ser(Ctx.Default.Int128, Int128.MinValue));
        Show("u128", () => Ser(Ctx.Default.UInt128, UInt128.MaxValue));
        Show("i128 read", () => Des("12345678901234567890123", Ctx.Default.Int128));
        Show("i128 read neg", () => Des("-5", Ctx.Default.Int128));
        Show("i128 read str", () => Des("\"5\"", Ctx.Default.Int128));
        Show("i128 read frac", () => Des("5.5", Ctx.Default.Int128));
        Show("i128 read big", () => Des("170141183460469231731687303715884105728", Ctx.Default.Int128));
        Show("u128 read", () => Des("340282366920938463463374607431768211455", Ctx.Default.UInt128));
        Show("u128 read neg", () => Des("-1", Ctx.Default.UInt128));
        Show("i128 dict", () => Ser(Ctx.Default.DictionaryInt128Int32, new Dictionary<Int128, int> { { 5, 1 } }));
        Show("i128 dict read", () => string.Join(",", Des("{\"-7\":1,\"8\":2}", Ctx.Default.DictionaryInt128Int32).Keys));

        Show("Int128.Parse", () => Int128.Parse("12345678901234567890123") + " " + Int128.Parse(" -7 ") + " " + Int128.Parse("+9"));
        Show("Int128.Parse bad", () => Int128.Parse("abc"));
        Show("Int128.Parse empty", () => Int128.Parse(""));
        Show("Int128.Parse big", () => Int128.Parse("170141183460469231731687303715884105728"));
        Show("Int128.Parse null", () => Int128.Parse(null!));
        Show("Int128.Parse min", () => Int128.Parse("-170141183460469231731687303715884105728"));
        Show("Int128.Parse max", () => Int128.Parse("170141183460469231731687303715884105727"));
        Show("Int128.Parse trailing", () => Int128.Parse("12x"));
        Show("Int128.Parse spaces", () => "[" + Int128.Parse("\t 42\r\n") + "]");
        Show("UInt128.Parse max", () => UInt128.Parse("340282366920938463463374607431768211455"));
        Show("UInt128.Parse over", () => UInt128.Parse("340282366920938463463374607431768211456"));
        Show("UInt128.Parse neg", () => UInt128.Parse("-1"));
        Show("UInt128.Parse -0", () => UInt128.Parse("-0"));
        Show("TryParse", () => { var ok = Int128.TryParse("77", out var v); var ok2 = Int128.TryParse("x", out var w); var ok3 = UInt128.TryParse((string)null, out var u); return ok + " " + v + " " + ok2 + " " + w + " " + ok3 + " " + u; });
        Show("Parse invariant provider", () => Int128.Parse("12", CultureInfo.InvariantCulture));
        Show("Parse span", () => Int128.Parse("123".AsSpan(), CultureInfo.InvariantCulture) + " " + Int128.TryParse("9".AsSpan(), out var sv) + sv);

        Show("dateonly", () => Ser(Ctx.Default.DateOnly, new DateOnly(2026, 11, 4)));
        Show("dateonly read", () => Des("\"2026-11-04\"", Ctx.Default.DateOnly).ToString("O", CultureInfo.InvariantCulture));
        foreach (var bad in new[] { "\"2026-1-4\"", "\"2026-11-04T10:00:00\"", "5", "\"2026-02-30\"", "\"0000-01-01\"", "\"2026-13-01\"", "\"\"", "null" })
            Show("dateonly read " + bad, () => Des(bad, Ctx.Default.DateOnly));
        Show("dateonly dict", () => Ser(Ctx.Default.DictionaryDateOnlyInt32, new Dictionary<DateOnly, int> { { new DateOnly(2026, 1, 2), 1 } }));
        Show("dateonly dict read", () => string.Join(",", Des("{\"2026-01-02\":1}", Ctx.Default.DictionaryDateOnlyInt32).Keys));

        Show("timeonly", () => Ser(Ctx.Default.TimeOnly, new TimeOnly(13, 30)) + Ser(Ctx.Default.TimeOnly, new TimeOnly(13, 30, 5, 120)) + Ser(Ctx.Default.TimeOnly, new TimeOnly(1, 2, 3).Add(TimeSpan.FromTicks(1))) + Ser(Ctx.Default.TimeOnly, TimeOnly.MaxValue) + Ser(Ctx.Default.TimeOnly, TimeOnly.MinValue));
        foreach (var good in new[] { "\"13:30\"", "\"13:30:15\"", "\"13:30:15.5\"", "\"13:30:15.1234567\"", "\"1:30\"", "\"01:30:15\"", "\"23:59:59.9999999\"", "\"0:00\"" })
            Show("timeonly read " + good, () => Des(good, Ctx.Default.TimeOnly).ToString("O", CultureInfo.InvariantCulture));
        foreach (var bad in new[] { "\"13:30:15.12345678\"", "\"24:00\"", "\"13:30 PM\"", "\"13:30:15.\"", "\"13\"", "\"13:5\"", "\"13:60\"", "\"13:30:60\"", "\" 13:30\"", "\"13:30:5\"", "\"13.30\"", "5", "\"\"" })
            Show("timeonly read " + bad, () => Des(bad, Ctx.Default.TimeOnly));

        Show("uri", () => Ser(Ctx.Default.Uri, new Uri("https://example.com/a?q=1")) + Ser(Ctx.Default.Uri, new Uri("rel/path", UriKind.Relative)));
        Show("uri read", () => Des("\"https://example.com/x\"", Ctx.Default.Uri).AbsoluteUri + " " + Des("\"/rel\"", Ctx.Default.Uri).IsAbsoluteUri);
        Show("uri read null", () => JsonSerializer.Deserialize("null", Ctx.Default.Uri) == null);
        Show("uri read num", () => Des("5", Ctx.Default.Uri));
        Show("version", () => Ser(Ctx.Default.Version, new Version(1, 2, 3)) + Ser(Ctx.Default.Version, new Version(1, 2)));
        Show("version read", () => Des("\"1.2.3.4\"", Ctx.Default.Version) + " " + Des("\"1.2\"", Ctx.Default.Version));
        Show("version read bad", () => Des("\"1\"", Ctx.Default.Version));
        Show("version read bad2", () => Des("\"a.b\"", Ctx.Default.Version));
        Show("version read num", () => Des("1.2", Ctx.Default.Version));
        Show("version null", () => JsonSerializer.Deserialize("null", Ctx.Default.Version) == null);

        Show("rom byte", () => Ser(Ctx.Default.ReadOnlyMemoryByte, new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 })) + Ser(Ctx.Default.ReadOnlyMemoryByte, default) + Ser(Ctx.Default.MemoryByte, new Memory<byte>(new byte[] { 9 })));
        Show("rom byte read", () => Convert.ToHexString(Des("\"AQID\"", Ctx.Default.ReadOnlyMemoryByte).ToArray()) + " " + JsonSerializer.Deserialize("null", Ctx.Default.ReadOnlyMemoryByte).Length);
        Show("rom byte bad", () => Des("\"!!\"", Ctx.Default.ReadOnlyMemoryByte));
        Show("memory byte read", () => Convert.ToHexString(Des("\"CQ==\"", Ctx.Default.MemoryByte).ToArray()));
        Show("rom float", () => Ser(Ctx.Default.ReadOnlyMemorySingle, new ReadOnlyMemory<float>(new float[] { 1.5f, 2 })) + Ser(Ctx.Default.MemoryInt32, new Memory<int>(new int[] { 1, 2 })) + Ser(Ctx.Default.ReadOnlyMemoryInt32, default));
        Show("rom float read", () => string.Join(",", Des("[1.5,2]", Ctx.Default.ReadOnlyMemorySingle).ToArray()) + " " + JsonSerializer.Deserialize("null", Ctx.Default.ReadOnlyMemoryInt32).Length + " " + string.Join(",", Des("[]", Ctx.Default.MemoryInt32).ToArray()));
        Show("rom float bad", () => Des("{}", Ctx.Default.ReadOnlyMemorySingle));
        Show("rom float badelement", () => Des("[1,\"x\"]", Ctx.Default.ReadOnlyMemoryInt32));

        Show("half", () => Ser(Ctx.Default.Half, (Half)1.5f) + Ser(Ctx.Default.Half, (Half)0.1f) + Ser(Ctx.Default.Half, (Half)65504f) + Ser(Ctx.Default.Half, Half.NegativeZero) + Ser(Ctx.Default.Half, Half.Epsilon));
        Show("half read", () => Des("1.5", Ctx.Default.Half) + " " + Des("0.1", Ctx.Default.Half) + " " + Des("-2e1", Ctx.Default.Half));
        foreach (var text in new[] { "1e-8", "70000", "65520", "65519.99", "0", "-0", "1e400", "\"1.5\"", "\"NaN\"", "true", "5.", "00" })
            Show("half read " + text, () => Des(text, Ctx.Default.Half));
        Show("half nan", () => Ser(Ctx.Default.Half, Half.NaN));
        Show("half inf", () => Ser(Ctx.Default.Half, Half.PositiveInfinity));
        Show("half dict", () => Ser(Ctx.Default.DictionaryHalfInt32, new Dictionary<Half, int> { { (Half)1.5f, 1 } }));
        Show("half dict read", () => string.Join(",", Des("{\"2.5\":1}", Ctx.Default.DictionaryHalfInt32).Keys));

        Show("Half.Parse", () => Half.Parse("1.5") + " " + Half.Parse("0.1") + " " + Half.Parse("1e-8") + " " + Half.Parse("70000") + " " + Half.Parse("-0") + " " + Half.Parse("65519.99") + " " + Half.Parse("1,000"));
        Show("Half.Parse specials", () => Half.Parse("NaN") + " " + Half.Parse("Infinity") + " " + Half.Parse("-Infinity") + " " + Half.Parse(" 3 "));
        Show("Half.Parse bad", () => Half.Parse("abc"));
        Show("Half.Parse null", () => Half.Parse(null!));
        Show("Half.TryParse", () => { var ok = Half.TryParse("2.5", out var v); var ok2 = Half.TryParse("zz", out var w); return ok + " " + v + " " + ok2 + " " + w; });
        Show("Half.Parse ties", () => BitConverter.HalfToUInt16Bits(Half.Parse("1.00048828125")) + " " + BitConverter.HalfToUInt16Bits(Half.Parse("1.00048828126")) + " " + BitConverter.HalfToUInt16Bits(Half.Parse("1.000488281249")) + " " + BitConverter.HalfToUInt16Bits(Half.Parse("1.001464843750")));
    }
}
