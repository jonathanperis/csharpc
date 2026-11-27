// Ascii, Base64, CompositeFormat and StandardFormat from CoreLib's IL (#423): ASCII transcoding, equality, casing and
// trimming, Base64 validation and in-place coding, composite-format parsing errors (the offsets and reasons) and formatting.
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;

static class Program
{
    static void Show(string label, OperationStatus s, int a, int b) => Console.WriteLine($"{label}: {s} {a} {b}");

    static void Main()
    {
        // Ascii.
        byte[] bytes = Encoding.ASCII.GetBytes("Hello, World 123");
        char[] chars = new char[8];
        Show("ToUtf16 short", Ascii.ToUtf16(bytes, chars, out int cw), cw, 0);
        Show("ToUtf16 bad", Ascii.ToUtf16(new byte[] { 0x41, 0x80, 0x42 }, new char[3], out cw), cw, 0);
        Show("FromUtf16 bad", Ascii.FromUtf16("abéc", new byte[4], out int bw), bw, 0);
        Console.WriteLine($"valid {Ascii.IsValid("abc")} {Ascii.IsValid("ab\u0080")} {Ascii.IsValid((byte)0x7F)} {Ascii.IsValid(new byte[] { 1, 200 })}");
        Console.WriteLine($"eq {Ascii.Equals("Abc", "Abc"u8)} {Ascii.EqualsIgnoreCase("HELLO"u8, "hello")} {Ascii.EqualsIgnoreCase("hé", "HÉ")}");
        var up = new byte[16];
        Show("ToUpper", Ascii.ToUpper(bytes, up, out bw), bw, 0);
        Console.WriteLine(Encoding.ASCII.GetString(up));
        byte[] padded = Encoding.ASCII.GetBytes("  \t x y \r\n");
        Console.WriteLine($"trim [{Encoding.ASCII.GetString(padded[Ascii.Trim(padded)])}] {Ascii.TrimStart("  ab ".AsSpan())} {Ascii.TrimEnd(" ab  ".AsSpan())}");

        // Base64.
        byte[] data = { 0, 1, 2, 250, 251, 252, 253 };
        var enc = new byte[Base64.GetMaxEncodedToUtf8Length(data.Length)];
        var st3 = Base64.EncodeToUtf8(data, enc, out int consumed, out int written);
        Console.WriteLine($"enc {st3} {consumed} {written} {Encoding.ASCII.GetString(enc)}");
        st3 = Base64.EncodeToUtf8(data, enc, out consumed, out written, isFinalBlock: false);
        Console.WriteLine($"enc partial {st3} {consumed} {written}");
        var dec = new byte[16];
        st3 = Base64.DecodeFromUtf8(enc, dec, out consumed, out written);
        Console.WriteLine($"dec {st3} {consumed} {written} {Convert.ToHexString(dec, 0, written)}");
        Console.WriteLine($"dec bad {Base64.DecodeFromUtf8("AQ=x"u8, dec, out consumed, out written)} {consumed} {written}");
        Console.WriteLine($"dec small {Base64.DecodeFromUtf8("AAECAwQF"u8, dec.AsSpan(0, 4), out consumed, out written)} {consumed} {written}");
        var inPlace = new byte[12]; data.AsSpan(0, 5).CopyTo(inPlace);
        Console.WriteLine($"inplace {Base64.EncodeToUtf8InPlace(inPlace, 5, out written)} {written} {Encoding.ASCII.GetString(inPlace, 0, written)}");
        Console.WriteLine($"inplace dec {Base64.DecodeFromUtf8InPlace(inPlace.AsSpan(0, written), out written)} {written}");
        Console.WriteLine($"valid {Base64.IsValid("AQID BA==", out int dl)} {dl} {Base64.IsValid("AQI")} {Base64.IsValid(" A Q = = "u8)} {Base64.GetMaxDecodedFromUtf8Length(10)}");
        try { Base64.GetMaxEncodedToUtf8Length(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message.Split('\n')[0].Trim()); }

        // CompositeFormat.
        var cf = CompositeFormat.Parse("{0,-5}|{1:X4}|{{x}}|{2}");
        Console.WriteLine($"{cf.Format} {cf.MinimumArgumentCount}");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, cf, "ab", 255, 1.5));
        Console.WriteLine(new StringBuilder().AppendFormat(CultureInfo.InvariantCulture, cf, 1, 2, 3).ToString());
        foreach (var bad in new[] { "{", "{x}", "}", "{0", "{0,}", "{0:{}" })
        {
            try { CompositeFormat.Parse(bad); Console.WriteLine("no error"); }
            catch (FormatException e) { Console.WriteLine($"{bad} -> {e.Message}"); }
        }
        // Large indices and alignments parse (the argument count wraps as CoreLib's int arithmetic does); formatting checks them.
        foreach (var big in new[] { "{999999}", "{99999999999}", "{0,1000000}" }) Console.WriteLine($"{big} {CompositeFormat.Parse(big).MinimumArgumentCount}");
        try { string.Format(null, cf, 1, 2); } catch (FormatException e) { Console.WriteLine(e.Message); }

        // StandardFormat.
        foreach (var f in new[] { "G", "D4", "x99", "N", "" })
        {
            var sf = StandardFormat.Parse(f);
            Console.WriteLine($"[{f}] {(int)sf.Symbol} {sf.Precision} {sf.HasPrecision} {sf.IsDefault} [{sf}]");
        }
        Console.WriteLine($"{StandardFormat.TryParse("D100", out var bad2)} {StandardFormat.TryParse("12", out bad2)} {new StandardFormat('X', 3) == StandardFormat.Parse("X3")}");
        try { StandardFormat.Parse("D100"); } catch (FormatException e) { Console.WriteLine(e.Message); }
        try { _ = new StandardFormat('D', 100); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message.Split('\n')[0].Trim()); }
    }
}
