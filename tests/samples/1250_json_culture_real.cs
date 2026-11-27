#:property JsonSerializerIsReflectionEnabledByDefault=true
// System.Text.Json is culture-invariant: whatever the current culture is (a decimal comma in de-DE, pt-BR,
// fr-FR and cs-CZ, a Unicode minus in sv-SE, other digits and calendars in ar-SA, fa-IR, th-TH and hi-IN),
// Utf8JsonWriter, JsonSerializer (source-generated and reflection-style), JsonNode, JsonElement and the
// reader write and parse exactly the bytes the invariant culture produces (issue 89).
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Threading;

var cultures = new[] { "de-DE", "pt-BR", "fr-FR", "cs-CZ", "sv-SE", "ar-SA", "fa-IR", "he-IL", "hi-IN", "th-TH", "ja-JP", "ar-EG" }.Select(n => new CultureInfo(n)).ToArray();

string baseline = Run(CultureInfo.InvariantCulture, false);
Console.Write(baseline);
foreach (var culture in cultures)
{
    // The thread's own current culture, and a new thread that only has DefaultThreadCurrentCulture.
    foreach (bool viaDefault in new[] { false, true })
    {
        string got = Run(culture, viaDefault);
        string label = $"culture '{culture.Name}' ({(viaDefault ? "default thread culture" : "current culture")})";
        if (got == baseline) { Console.WriteLine(label + ": identical"); continue; }
        Console.WriteLine(label + ": DIFFERENT");
        var g = got.Split('\n'); var b = baseline.Split('\n');
        for (int i = 0; i < Math.Max(g.Length, b.Length); i++)
            if (i >= g.Length || i >= b.Length || g[i] != b[i])
                Console.WriteLine($"  line {i + 1}:\n    got:  {(i < g.Length ? g[i] : "<none>")}\n    want: {(i < b.Length ? b[i] : "<none>")}");
    }
}
// The cultures really are in effect: the same values formatted without a provider.
foreach (var name in new[] { "de-DE", "pt-BR", "fr-FR", "cs-CZ", "sv-SE" })
{
    CultureInfo.CurrentCulture = new CultureInfo(name);
    Console.WriteLine($"{name}: {1.5} {-2.5m} {-3.5f} {1234.5:N1}");
}
CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
Console.WriteLine($"sv-SE minus is U+{(int)(-1).ToString()[0]:X4}");

static string Run(CultureInfo culture, bool viaDefault)
{
    string result = "";
    if (viaDefault)
    {
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        try
        {
            var t = new Thread(() => result = Scenario());
            t.Start(); t.Join();
        }
        catch (PlatformNotSupportedException)
        {
            // Single-threaded platforms (WebAssembly): run on the main thread with the culture set.
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = culture;
            result = Scenario();
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        }
        CultureInfo.DefaultThreadCurrentCulture = null;
        CultureInfo.DefaultThreadCurrentUICulture = null;
        return result;
    }
    var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
    CultureInfo.CurrentCulture = culture;
    CultureInfo.CurrentUICulture = culture;
    try { return Scenario(); }
    finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved; }
}

// ---- scenario: every JSON number/date/guid path, formatted and parsed; output is built from invariant text only ----
static string I(IFormattable f) => f.ToString(null, CultureInfo.InvariantCulture);

static string Scenario()
{
    var o = new StringBuilder();
    void L(string s) => o.Append(s).Append('\n');

    // 1. Utf8JsonWriter: values, every property-name overload, minimized and indented.
    foreach (bool indented in new[] { false, true })
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented, NewLine = "\n" }))
        {
            w.WriteStartObject();
            var enc = JsonEncodedText.Encode("enc");
            w.WriteNumber("d", 1.5); w.WriteNumber("f", 2.5f); w.WriteNumber("m", 3.5m);
            w.WriteNumber(enc, -1.25); w.WriteNumber(enc, -2.25f); w.WriteNumber(enc, -3.25m);
            w.WriteNumber("u8"u8, 4.75); w.WriteNumber("u8"u8, 5.75f); w.WriteNumber("u8"u8, 6.75m);
            w.WriteNumber("span".AsSpan(), 7.125); w.WriteNumber("span".AsSpan(), 8.125f); w.WriteNumber("span".AsSpan(), 9.125m);
            w.WriteNumber("i", -42); w.WriteNumber("l", -4200000000L); w.WriteNumber("ul", ulong.MaxValue); w.WriteNumber("ui", uint.MaxValue);
            w.WriteNumber(enc, -42); w.WriteNumber("u8"u8, -4200000000L);
            w.WriteString("dt", new DateTime(2026, 10, 23, 8, 5, 3, 120, DateTimeKind.Utc));
            w.WriteString("dtl", new DateTime(2026, 10, 23, 8, 5, 3, DateTimeKind.Unspecified).AddTicks(1234567));
            w.WriteString("dto", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(-150)));
            w.WriteString(enc, new DateTime(2000, 2, 29, 0, 0, 0, DateTimeKind.Utc));
            w.WriteString("u8"u8, new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero));
            w.WriteString("g", new Guid("01234567-89ab-cdef-0123-456789abcdef"));
            w.WriteString(enc, new Guid("fedcba98-7654-3210-fedc-ba9876543210"));
            w.WriteStartArray("values");
            foreach (double d in new[] { 0.0, -0.0, 1.5, -2.5, 0.1 + 0.2, 1e15, 1e16, 123456789012345678.0, 1.2345678901234567e-7, 1e-5, 0.0001, double.MaxValue, double.Epsilon, 1.0 / 3 })
                w.WriteNumberValue(d);
            foreach (float f in new[] { 0f, 1.5f, -3.25f, 1e10f, 16777216f, float.MinValue, 3.4e-5f, 0.1f, 1f / 3 })
                w.WriteNumberValue(f);
            foreach (decimal m in new[] { 0m, 1.5m, -0.000001m, 1.10m, 79228162514264337593543950335m, -12345.6789m, 100m, 0.0000000000000000000000000001m })
                w.WriteNumberValue(m);
            w.WriteNumberValue(-7); w.WriteNumberValue(int.MinValue); w.WriteNumberValue(long.MinValue); w.WriteNumberValue(ulong.MaxValue); w.WriteNumberValue(uint.MaxValue);
            w.WriteStringValue(new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc));
            w.WriteStringValue(new DateTimeOffset(2026, 7, 4, 12, 30, 15, 500, TimeSpan.FromHours(9.5)));
            w.WriteStringValue(Guid.Empty);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        L("writer" + (indented ? " indented: " : ": ") + Encoding.UTF8.GetString(buffer.WrittenSpan));
    }
    // Non-finite values stay errors, and the message is the same in every culture.
    foreach (double bad in new[] { double.NaN, double.PositiveInfinity })
        try { using var w = new Utf8JsonWriter(new ArrayBufferWriter<byte>()); w.WriteNumberValue(bad); } catch (Exception e) { L("bad double: " + e.GetType().Name + ": " + e.Message); }
    try { using var w = new Utf8JsonWriter(new ArrayBufferWriter<byte>()); w.WriteStartObject(); w.WriteNumber("x", float.NegativeInfinity); } catch (Exception e) { L("bad float: " + e.GetType().Name + ": " + e.Message); }

    // 2. Utf8JsonReader: numbers, dates and guids parsed from the text .NET writes.
    {
        var reader = new Utf8JsonReader("""[1.5,-2.5e3,1E-7,123456789.125,1.10,-12345.6789,79228162514264337593543950335,-7,18446744073709551615,0.30000000000000004,3.4028235E+38,"2026-10-23T08:05:03.1234567Z","2026-01-02T03:04:05-02:30","2026-01-02T03:04:05.5","01234567-89ab-cdef-0123-456789abcdef"]"""u8);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                var sb = new StringBuilder("read number " + Encoding.UTF8.GetString(reader.ValueSpan) + ":");
                if (reader.TryGetDouble(out double d)) sb.Append(" d=" + I(d));
                if (reader.TryGetSingle(out float f)) sb.Append(" f=" + I(f));
                if (reader.TryGetDecimal(out decimal m)) sb.Append(" m=" + I(m));
                if (reader.TryGetInt32(out int i)) sb.Append(" i=" + I(i));
                if (reader.TryGetInt64(out long l)) sb.Append(" l=" + I(l));
                if (reader.TryGetUInt64(out ulong u)) sb.Append(" u=" + I(u));
                L(sb.ToString());
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                // Text without an offset means the machine's time zone for a DateTimeOffset, and an offset converts a
                // DateTime to local time: read each kind only the way that is time-zone independent.
                string text = reader.GetString()!;
                bool zulu = text.EndsWith('Z'), offset = !zulu && text.Length > 19 && (text[^6] == '-' || text[^6] == '+');
                if (!offset && reader.TryGetDateTime(out DateTime dt)) L("read DateTime " + dt.ToString("O", CultureInfo.InvariantCulture) + " " + dt.Kind);
                if ((zulu || offset) && reader.TryGetDateTimeOffset(out DateTimeOffset dto)) L("read DateTimeOffset " + dto.ToString("O", CultureInfo.InvariantCulture));
                if (reader.TryGetGuid(out Guid g)) L("read Guid " + g.ToString());
            }
        }
        try { var r = new Utf8JsonReader("1,5"u8); r.Read(); L("read '1,5': " + r.GetDouble()); } catch (Exception e) { L("read '1,5': " + e.GetType().Name); }
    }

    // 3. JsonSerializer with source-generated metadata and without (reflection-style).
    var rec = new Rec(1.5f, 2.5, 3.5m, (Half)4.5f, Int128.MinValue + 1, UInt128.MaxValue, -42, -4200000000L, new float[] { 4.5f, -1.25f }, new[] { 1.5f }, new[] { 2.5, -0.5 }, new[] { 3.5m, -0.25m });
    L("gen rec: " + JsonSerializer.Serialize(rec, Ctx.Default.Rec));
    L("refl rec: " + JsonSerializer.Serialize(rec));
    var back = JsonSerializer.Deserialize("""{"F":1.5,"D":2.5,"M":3.5,"H":4.5,"I":-170141183460469231731687303715884105727,"U":340282366920938463463374607431768211455,"N":-42,"L":-4200000000,"FloatMemory":[4.5,-1.25],"FloatArray":[1.5],"DoubleArray":[2.5,-0.5],"DecimalArray":[3.5,-0.25]}""", Ctx.Default.Rec)!;
    L("gen rec back: " + JsonSerializer.Serialize(back, Ctx.Default.Rec) + " " + (back.D == 2.5) + " " + (back.M == 3.5m));
    var back2 = JsonSerializer.Deserialize<Rec>("""{"F":1.5,"D":2.5,"M":3.5,"H":4.5,"I":-1,"U":1,"N":-42,"L":-4200000000,"FloatMemory":[4.5],"FloatArray":[1.5],"DoubleArray":[2.5],"DecimalArray":[3.5]}""")!;
    L("refl rec back: " + JsonSerializer.Serialize(back2));

    var stamps = new Stamps(new DateTime(2026, 10, 23, 8, 5, 3, 120, DateTimeKind.Utc), new DateTime(2000, 1, 1).AddTicks(1), new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(-150)),
        new TimeSpan(-3, 4, 5, 6, 789), new Guid("01234567-89ab-cdef-0123-456789abcdef"), new DateOnly(2026, 10, 23), new TimeOnly(8, 5, 3, 120), new Version(1, 2, 3, 4), new Uri("http://x.test/a?b=1.5"));
    L("gen stamps: " + JsonSerializer.Serialize(stamps, Ctx.Default.Stamps));
    L("refl stamps: " + JsonSerializer.Serialize(stamps));
    var sb2 = JsonSerializer.Deserialize(JsonSerializer.Serialize(stamps, Ctx.Default.Stamps), Ctx.Default.Stamps)!;
    L("stamps back: " + (sb2 == stamps) + " " + sb2.Span.ToString("c", CultureInfo.InvariantCulture) + " " + sb2.Utc.ToString("O", CultureInfo.InvariantCulture) + " " + sb2.Off.ToString("O", CultureInfo.InvariantCulture));
    L("refl stamps back: " + JsonSerializer.Deserialize<Stamps>(JsonSerializer.Serialize(stamps))!.Equals(stamps));

    // Numbers as strings, and named floating-point literals.
    var quoted = new Quoted(-42, -4200000000L, -2.5, 1.5f, -0.75m, (Half)(-1.5f), Int128.MinValue, ulong.MaxValue);
    L("gen quoted: " + JsonSerializer.Serialize(quoted, Ctx.Default.Quoted));
    L("refl quoted: " + JsonSerializer.Serialize(quoted));
    L("quoted back: " + (JsonSerializer.Deserialize(JsonSerializer.Serialize(quoted, Ctx.Default.Quoted), Ctx.Default.Quoted) == quoted));
    var named = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString, TypeInfoResolver = Ctx.Default };
    L("named: " + JsonSerializer.Serialize(new Floats(1.5, double.NaN, double.PositiveInfinity, -2.5f, float.NegativeInfinity, -0.25m, -7), named)
        + " " + JsonSerializer.Serialize(new Floats(-1.5, 0.1, 1e21, float.NaN, 3.4e-5f, 1.10m, 4200000000), named));
    var namedBack = JsonSerializer.Deserialize<Floats>("""{"A":"1.5","B":"NaN","C":-2.5,"D":"-Infinity","E":"-0.25","F":1.5,"G":"-3"}""", named)!;
    L("named back: " + I(namedBack.A) + " " + I(namedBack.B) + " " + I(namedBack.C) + " " + I(namedBack.D) + " " + I(namedBack.E) + " " + I(namedBack.F) + " " + I(namedBack.G));

    // Dictionary keys of every numeric/date/guid/enum type.
    L("keys int: " + JsonSerializer.Serialize(new Dictionary<int, string> { [-1] = "a", [20] = "b" }, Ctx.Default.DictionaryInt32String));
    L("keys long: " + JsonSerializer.Serialize(new Dictionary<long, string> { [-4200000000L] = "a" }, Ctx.Default.DictionaryInt64String));
    L("keys short/sbyte: " + JsonSerializer.Serialize(new Dictionary<short, int> { [-3] = 1 }, Ctx.Default.DictionaryInt16Int32) + JsonSerializer.Serialize(new Dictionary<sbyte, int> { [-3] = 1 }, Ctx.Default.DictionarySByteInt32));
    L("keys double: " + JsonSerializer.Serialize(new Dictionary<double, int> { [1.5] = 1, [-2.5] = 2, [1e21] = 3 }, Ctx.Default.DictionaryDoubleInt32));
    L("keys float: " + JsonSerializer.Serialize(new Dictionary<float, int> { [1.5f] = 1, [-0.25f] = 2 }, Ctx.Default.DictionarySingleInt32));
    L("keys decimal: " + JsonSerializer.Serialize(new Dictionary<decimal, int> { [1.5m] = 1, [-0.25m] = 2 }, Ctx.Default.DictionaryDecimalInt32));
    L("keys Half: " + JsonSerializer.Serialize(new Dictionary<Half, int> { [(Half)1.5f] = 1, [(Half)(-0.25f)] = 2 }, Ctx.Default.DictionaryHalfInt32));
    L("keys Int128: " + JsonSerializer.Serialize(new Dictionary<Int128, int> { [Int128.MinValue] = 1 }, Ctx.Default.DictionaryInt128Int32) + JsonSerializer.Serialize(new Dictionary<UInt128, int> { [UInt128.MaxValue] = 1 }, Ctx.Default.DictionaryUInt128Int32));
    L("keys DateTime: " + JsonSerializer.Serialize(new Dictionary<DateTime, int> { [new DateTime(2026, 10, 23, 8, 5, 3, DateTimeKind.Utc)] = 1 }, Ctx.Default.DictionaryDateTimeInt32)
        + JsonSerializer.Serialize(new Dictionary<DateTimeOffset, int> { [new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1))] = 1 }, Ctx.Default.DictionaryDateTimeOffsetInt32));
    L("keys TimeSpan/Guid: " + JsonSerializer.Serialize(new Dictionary<TimeSpan, int> { [new TimeSpan(-1, 2, 3)] = 1 }, Ctx.Default.DictionaryTimeSpanInt32)
        + JsonSerializer.Serialize(new Dictionary<Guid, int> { [new Guid("01234567-89ab-cdef-0123-456789abcdef")] = 1 }, Ctx.Default.DictionaryGuidInt32));
    L("keys DateOnly/TimeOnly: " + JsonSerializer.Serialize(new Dictionary<DateOnly, int> { [new DateOnly(2026, 10, 23)] = 1 }, Ctx.Default.DictionaryDateOnlyInt32)
        + JsonSerializer.Serialize(new Dictionary<TimeOnly, int> { [new TimeOnly(8, 5, 3, 120)] = 1 }, Ctx.Default.DictionaryTimeOnlyInt32));
    L("keys enum: " + JsonSerializer.Serialize(new Dictionary<Level, int> { [Level.Low] = 1, [Level.High] = 2, [(Level)(-3)] = 3 }, Ctx.Default.DictionaryLevelInt32)
        + JsonSerializer.Serialize(new Dictionary<NumLevel, int> { [NumLevel.Low] = 1, [(NumLevel)(-3)] = 3 }, Ctx.Default.DictionaryNumLevelInt32));
    L("enum values: " + JsonSerializer.Serialize(new NumLevel[] { NumLevel.Low, NumLevel.High, (NumLevel)(-3) }, Ctx.Default.NumLevelArray) + JsonSerializer.Serialize(new Level[] { Level.Low, (Level)(-3) }, Ctx.Default.LevelArray));
    L("keys refl: " + JsonSerializer.Serialize(new Dictionary<double, decimal> { [-1.5] = -2.5m }) + JsonSerializer.Serialize(new Dictionary<long, Half> { [-1] = (Half)(-2.5f) }));
    var kb = JsonSerializer.Deserialize("""{"-1.5":1,"2.5":2}""", Ctx.Default.DictionaryDoubleInt32)!;
    L("keys back: " + I(kb.Keys.First()) + " " + I(JsonSerializer.Deserialize("""{"-0.25":1}""", Ctx.Default.DictionaryDecimalInt32)!.Keys.First()) + " " + I(JsonSerializer.Deserialize("""{"-7":1}""", Ctx.Default.DictionaryInt32Int32)!.Keys.First()));

    var lk = JsonSerializer.Deserialize("""{"-5":1,"High":2,"7":3}""", Ctx.Default.DictionaryLevelInt32)!;
    L("keys enum back: " + string.Join(",", lk.Select(kv => kv.Key + "=" + kv.Value)) + " " + JsonSerializer.Deserialize("""{"-5":1,"7":3}""", Ctx.Default.DictionaryNumLevelInt32)!.Count);

    // object-typed values and Dictionary<string, object>.
    L("object: " + JsonSerializer.Serialize<object>(new object[] { 1.5, 2.5f, 3.5m, -4, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Guid.Empty }, Ctx.Default.Object));
    var objs = JsonSerializer.Deserialize("""{"a":1.5,"b":[-2.5,3],"c":{"d":0.1}}""", Ctx.Default.DictionaryStringObject)!;
    L("objects: " + string.Join(",", objs.Select(kv => kv.Key + "=" + kv.Value)) + " | " + JsonSerializer.Serialize(objs, Ctx.Default.DictionaryStringObject));

    // 4. JsonNode / JsonValue / JsonArray / JsonObject.
    var arr = new JsonArray(1.5f, 2.5, 3.5m, JsonSerializer.SerializeToNode((Half)4.5f, Ctx.Default.Half), -7, -4200000000L, JsonSerializer.SerializeToNode(Int128.MinValue + 1, Ctx.Default.Int128), new DateTime(2026, 10, 23, 8, 5, 3, DateTimeKind.Utc), Guid.Empty);
    L("node array: " + arr.ToJsonString() + " | " + arr.ToString().Replace("\r", "").Replace("\n", "\\n"));
    var obj = new JsonObject { ["f"] = 1.5f, ["d"] = -2.5, ["m"] = 3.5m, ["n"] = JsonValue.Create(-42), ["when"] = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)), ["t"] = JsonSerializer.SerializeToNode(new TimeSpan(1, 2, 3), Ctx.Default.TimeSpan) };
    L("node object: " + obj.ToJsonString());
    foreach (var kv in obj) L("node " + kv.Key + ": " + kv.Value!.ToJsonString() + " | " + kv.Value + " | " + kv.Value.GetValueKind());
    L("node value: " + JsonValue.Create(1.5).ToString() + JsonValue.Create(-2.5f).ToString() + JsonValue.Create(3.5m).ToString() + JsonValue.Create(-4).ToString() + " " + JsonValue.Create(1.5).ToJsonString());
    var parsed = JsonNode.Parse("""{"a":1.5,"b":-2.5e3,"c":[0.1,12345678901234567890],"d":"2026-10-23T08:05:03Z","e":"01234567-89ab-cdef-0123-456789abcdef"}""")!;
    L("node parse: " + parsed.ToJsonString() + " " + I(parsed["a"]!.GetValue<double>()) + " " + I((double)parsed["b"]!) + " " + I((decimal)parsed["a"]!) + " " + I((float)parsed["a"]!)
        + " " + I(parsed["d"]!.GetValue<DateTime>().Ticks) + " " + parsed["e"]!.GetValue<Guid>() + " " + I(parsed["c"]![1]!.GetValue<decimal>()));
    L("node deep: " + JsonNode.DeepEquals(parsed["a"], JsonValue.Create(1.5)) + " " + JsonNode.DeepEquals(parsed["a"], JsonValue.Create(1.5m)));
    L("node serialize: " + JsonSerializer.Serialize(parsed, Ctx.Default.JsonNode) + " " + JsonSerializer.SerializeToNode(rec, Ctx.Default.Rec)!.ToJsonString());
    L("node convert: " + JsonSerializer.Deserialize(JsonSerializer.SerializeToNode(rec, Ctx.Default.Rec), Ctx.Default.Rec)!.Equals(rec));

    // 5. JsonDocument / JsonElement.
    using (var doc = JsonDocument.Parse("""{"a":1.5,"b":-2.5e3,"c":[0.1,12345678901234567890,1E400,-0],"d":"2026-10-23T08:05:03.5+02:00","e":"01234567-89ab-cdef-0123-456789abcdef","f":123456789.125,"g":1.10}"""))
    {
        var root = doc.RootElement;
        L("element raw: " + root.GetRawText());
        L("element tostring: " + root.GetProperty("a").ToString() + " " + root.GetProperty("c").ToString() + " " + root.GetProperty("g").GetRawText() + " " + root.GetProperty("g").ToString());
        L("element numbers: " + I(root.GetProperty("a").GetDouble()) + " " + I(root.GetProperty("a").GetSingle()) + " " + I(root.GetProperty("a").GetDecimal()) + " " + I(root.GetProperty("b").GetDouble()) + " " + root.GetProperty("b").TryGetInt32(out int bi) + I(bi) + " " + I(root.GetProperty("f").GetDecimal()) + " " + I(root.GetProperty("g").GetDecimal()));
        L("element try: " + root.GetProperty("a").TryGetInt32(out _) + " " + root.GetProperty("c")[1].TryGetUInt64(out ulong uu) + I(uu) + " " + root.GetProperty("c")[2].TryGetDouble(out double inf) + " " + root.GetProperty("c")[3].TryGetDecimal(out decimal negz) + I(negz));
        L("element date/guid: " + root.GetProperty("d").GetDateTimeOffset().ToString("O", CultureInfo.InvariantCulture) + " " + " " + root.GetProperty("e").GetGuid());
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer)) root.WriteTo(w);
        L("element writeto: " + Encoding.UTF8.GetString(buffer.WrittenSpan));
        L("element serialize: " + JsonSerializer.Serialize(root, Ctx.Default.JsonElement) + " " + JsonSerializer.Serialize(doc, Ctx.Default.JsonDocument));
        L("element deserialize: " + JsonSerializer.Deserialize(root.GetRawText(), Ctx.Default.JsonElement).GetProperty("f").GetRawText());
    }
    var el = JsonSerializer.SerializeToElement(rec, Ctx.Default.Rec);
    L("serialize to element: " + el.GetRawText() + " " + JsonSerializer.SerializeToDocument(rec, Ctx.Default.Rec).RootElement.GetProperty("D").GetRawText());

    // 6. Schema: constructor defaults go through the same number formatting.
    L("schema: " + JsonSchemaExporter.GetJsonSchemaAsNode(Ctx.Default.Defaults).ToJsonString());

    // 7. Streams and the async API go through the same writer.
    var ms = new MemoryStream();
    JsonSerializer.Serialize(ms, rec, Ctx.Default.Rec);
    L("stream: " + Encoding.UTF8.GetString(ms.ToArray()));
    L("utf8: " + Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(new[] { 1.5, -2.5 }, Ctx.Default.DoubleArray)));
    return o.ToString();
}

public record Rec(float F, double D, decimal M, Half H, Int128 I, UInt128 U, int N, long L, ReadOnlyMemory<float> FloatMemory, float[] FloatArray, double[] DoubleArray, decimal[] DecimalArray)
{
    // Records compare arrays by reference: compare their text instead.
    public virtual bool Equals(Rec? other) => other is not null && JsonSerializer.Serialize(this, Ctx.Default.Rec) == JsonSerializer.Serialize(other, Ctx.Default.Rec);
    public override int GetHashCode() => 0;
}

public record Stamps(DateTime Utc, DateTime Unspec, DateTimeOffset Off, TimeSpan Span, Guid Id, DateOnly Day, TimeOnly Time, Version Ver, Uri Link);

[JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
public record Quoted(int I, long L, double D, float F, decimal M, Half H, Int128 B, ulong U);

public record Floats(double A, double B, double C, float D, float E, decimal F, long G);

public record Defaults(double Ratio = 1.5, decimal Price = -0.25m, float Scale = 2.5f, int Count = -7, long Big = -4200000000L, Half Small = default);

public enum Level { Low = -5, Mid = 0, High = 7 }

[JsonConverter(typeof(JsonNumberEnumConverter<NumLevel>))]
public enum NumLevel { Low = -5, Mid = 0, High = 7 }

[JsonSerializable(typeof(Rec))]
[JsonSerializable(typeof(Stamps))]
[JsonSerializable(typeof(Quoted))]
[JsonSerializable(typeof(Defaults))]
[JsonSerializable(typeof(Floats))]
[JsonSerializable(typeof(double[]))]
[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(decimal[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Half))]
[JsonSerializable(typeof(Int128))]
[JsonSerializable(typeof(NumLevel[]))]
[JsonSerializable(typeof(Level[]))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonDocument))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<int, string>))]
[JsonSerializable(typeof(Dictionary<long, string>))]
[JsonSerializable(typeof(Dictionary<short, int>))]
[JsonSerializable(typeof(Dictionary<sbyte, int>))]
[JsonSerializable(typeof(Dictionary<int, int>))]
[JsonSerializable(typeof(Dictionary<double, int>))]
[JsonSerializable(typeof(Dictionary<float, int>))]
[JsonSerializable(typeof(Dictionary<decimal, int>))]
[JsonSerializable(typeof(Dictionary<Half, int>))]
[JsonSerializable(typeof(Dictionary<Int128, int>))]
[JsonSerializable(typeof(Dictionary<UInt128, int>))]
[JsonSerializable(typeof(Dictionary<DateTime, int>))]
[JsonSerializable(typeof(Dictionary<DateTimeOffset, int>))]
[JsonSerializable(typeof(Dictionary<TimeSpan, int>))]
[JsonSerializable(typeof(Dictionary<Guid, int>))]
[JsonSerializable(typeof(Dictionary<DateOnly, int>))]
[JsonSerializable(typeof(Dictionary<TimeOnly, int>))]
[JsonSerializable(typeof(Dictionary<Level, int>))]
[JsonSerializable(typeof(Dictionary<NumLevel, int>))]
public partial class Ctx : JsonSerializerContext;
