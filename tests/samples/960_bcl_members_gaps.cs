#:property AllowUnsafeBlocks=true
// BCL members a large desktop app and its NuGet dependencies call: Base64 sizing and validation, span
// TrimEnd(char)/IsWhiteSpace, Encoding.UTF32, Math.DivRem(nuint), Buffer.MemoryCopy, WebUtility.UrlDecode,
// nint formatting and conversions, Marshal.StringToBSTR and Thread's apartment state.
#pragma warning disable CA1416
using System;
using System.Buffers.Text;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class Program
{
    static void Show(string name, Func<object> f)
    {
        try { Console.WriteLine(name + ": " + f()); }
        catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    static unsafe void Main()
    {
        Show("MaxEncoded 10", () => Base64.GetMaxEncodedToUtf8Length(10));
        Show("MaxEncoded 0", () => Base64.GetMaxEncodedToUtf8Length(0));
        Show("MaxEncoded max", () => Base64.GetMaxEncodedToUtf8Length(1610612733));
        Show("MaxEncoded over", () => Base64.GetMaxEncodedToUtf8Length(1610612734));
        Show("MaxEncoded negative", () => Base64.GetMaxEncodedToUtf8Length(-1));
        Show("MaxDecoded 16", () => Base64.GetMaxDecodedFromUtf8Length(16));
        Show("MaxDecoded 3", () => Base64.GetMaxDecodedFromUtf8Length(3));
        Show("MaxDecoded negative", () => Base64.GetMaxDecodedFromUtf8Length(-4));
        foreach (var text in new[] { "SGVsbG8=", "", "SGVs", "SGVsbG8", "SGVsbG8==", "SG Vs\r\nbG8=", "====", "AA==", "A===", "AA=A", "S*Vs", "AAA=AAAA" })
            Show("IsValid '" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "'", () => Base64.IsValid(text.AsSpan()) + " / " + Base64.IsValid(Encoding.ASCII.GetBytes(text)));

        Show("TrimEnd", () => "[" + "abc  ".AsSpan().TrimEnd(' ').ToString() + "]");
        Show("TrimStart", () => "[" + "  abc  ".AsSpan().TrimStart(' ').ToString() + "]");
        Show("Trim", () => "[" + "xxabcxx".AsSpan().Trim('x').ToString() + "]");
        Show("TrimEnd all", () => "[" + "xxx".AsSpan().TrimEnd('x').ToString() + "]");
        Show("IsWhiteSpace ws", () => "   ".AsSpan().IsWhiteSpace());
        Show("IsWhiteSpace empty", () => "".AsSpan().IsWhiteSpace());
        Show("IsWhiteSpace text", () => " a ".AsSpan().IsWhiteSpace());
        Show("IsWhiteSpace unicode", () => "\u00a0\u2003\t".AsSpan().IsWhiteSpace());

        var utf32 = Encoding.UTF32;
        Show("UTF32 name", () => utf32.EncodingName + " " + utf32.WebName + " " + utf32.CodePage + " " + utf32.WindowsCodePage);
        Show("UTF32 count", () => utf32.GetByteCount("hi"));
        Show("UTF32 bytes", () => Hex(utf32.GetBytes("hi\U0001F600é")));
        Show("UTF32 preamble", () => Hex(utf32.GetPreamble()));
        Show("UTF32 BE preamble", () => Hex(new UTF32Encoding(true, true).GetPreamble()));
        Show("UTF32 BE bytes", () => Hex(new UTF32Encoding(true, false).GetBytes("a\U0001F600")));
        Show("UTF32 chars", () => utf32.GetString(new byte[] { 0x68, 0, 0, 0, 0x00, 0xF6, 0x01, 0x00 }));
        Show("UTF32 BE chars", () => new UTF32Encoding(true, false).GetString(new byte[] { 0, 0, 0, 0x68, 0, 1, 0xF6, 0x00 }));
        Show("UTF32 lone surrogate", () => Hex(utf32.GetBytes("a\ud800b")));
        Show("UTF32 invalid scalar", () => utf32.GetString(new byte[] { 0x41, 0, 0, 0, 0, 0, 0x11, 0, 0x00, 0xD8, 0, 0, 0x42, 0, 0, 0 }));
        Show("UTF32 truncated", () => utf32.GetString(new byte[] { 0x41, 0, 0, 0, 0x42, 0 }));
        Show("UTF32 throwing", () => new UTF32Encoding(false, false, true).GetBytes("a\ud800"));
        Show("UTF32 throwing decode", () => new UTF32Encoding(false, false, true).GetString(new byte[] { 0, 0, 0x11, 0 }));
        Show("UTF32 max counts", () => utf32.GetMaxByteCount(7) + " " + utf32.GetMaxCharCount(7) + " " + utf32.GetMaxCharCount(0));
        Show("UTF32 max char counts", () => { var sb = new StringBuilder(); for (int i = 0; i <= 9; i++) sb.Append(utf32.GetMaxCharCount(i)).Append(' '); return sb.ToString(); });
        Show("UTF32 max counts negative", () => utf32.GetMaxByteCount(-1));
        Show("UTF32 chars count", () => utf32.GetCharCount(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0 }));
        Show("UTF32 equals", () => utf32.Equals(new UTF32Encoding(false, true)) + " " + utf32.Equals(new UTF32Encoding(true, true)));
        Show("GetEncoding 12000", () => Encoding.GetEncoding(12000).EncodingName + " " + Encoding.GetEncoding("utf-32").CodePage + " " + Encoding.GetEncoding(12001).EncodingName);
        Show("UTF32 decoder", () =>
        {
            var decoder = utf32.GetDecoder();
            var chars = new char[4];
            int n = decoder.GetChars(new byte[] { 0x41, 0, 0 }, 0, 3, chars, 0);
            n += decoder.GetChars(new byte[] { 0, 0x00, 0xF6 }, 0, 3, chars, n);
            n += decoder.GetChars(new byte[] { 0x01, 0 }, 0, 2, chars, n);
            return n + " " + new string(chars, 0, n);
        });

        Show("DivRem nuint", () => { var (q, r) = Math.DivRem((nuint)17, (nuint)5); return q + " " + r; });
        Show("DivRem nint", () => { var (q, r) = Math.DivRem((nint)(-17), (nint)5); return q + " " + r; });
        Show("DivRem by zero", () => Math.DivRem((nuint)1, (nuint)0));

        Show("MemoryCopy", () =>
        {
            byte* a = stackalloc byte[4] { 1, 2, 3, 4 };
            byte* b = stackalloc byte[4];
            Buffer.MemoryCopy(a, b, 4, 4);
            return b[0] + " " + b[3];
        });
        Show("MemoryCopy overlap", () =>
        {
            byte* a = stackalloc byte[6] { 1, 2, 3, 4, 5, 6 };
            Buffer.MemoryCopy(a, a + 2, 4, 4);
            return a[0] + " " + a[1] + " " + a[2] + " " + a[3] + " " + a[4] + " " + a[5];
        });
        Show("MemoryCopy too small", () =>
        {
            byte* a = stackalloc byte[4];
            byte* b = stackalloc byte[2];
            Buffer.MemoryCopy(a, b, 2, 4);
            return "no";
        });
        Show("MemoryCopy zero", () =>
        {
            byte* a = stackalloc byte[1];
            Buffer.MemoryCopy(a, a, 0, 0);
            return "ok";
        });

        foreach (var url in new[] { "a%20b+c", "%41%42", "caf%C3%A9", "%E2%82", "%E2%82%AC", "%zz", "%4", "100%", "%u0041%u00e9", "%u12", "a%2", "%uD83D%uDE00", "%C3%A9%u0041", "é+x", "%F0%9F%98%80", "%80", "" })
            Show("UrlDecode '" + url + "'", () => WebUtility.UrlDecode(url).Length + " " + Hex(Encoding.UTF8.GetBytes(WebUtility.UrlDecode(url))));
        Show("UrlDecode null", () => WebUtility.UrlDecode(null) == null);

        Show("nint hex", () => ((nint)255).ToString("x"));
        Show("nint format", () => ((nint)(-1234)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
        Show("nint provider", () => ((nint)42).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Show("nint ToInt64", () => ((nint)255).ToInt64());
        Show("nint ToInt32", () => ((nint)(-5)).ToInt32());
        Show("nuint hex", () => ((nuint)255).ToString("X4"));
        Show("nuint ToUInt64", () => ((nuint)255).ToUInt64());

        Show("BSTR", () =>
        {
            var bstr = Marshal.StringToBSTR("hi \u00e9");
            var s = Marshal.PtrToStringBSTR(bstr);
            var length = Marshal.ReadInt32(bstr, -4);
            Marshal.FreeBSTR(bstr);
            return s + " " + length;
        });
        Show("BSTR null", () => Marshal.StringToBSTR(null) == IntPtr.Zero);
        Show("BSTR empty", () =>
        {
            var bstr = Marshal.StringToBSTR("");
            var s = Marshal.PtrToStringBSTR(bstr);
            Marshal.FreeBSTR(bstr);
            return "[" + s + "]";
        });

        if (!OperatingSystem.IsWindows())
        {
            var t = new Thread(() => { });
            Show("apartment get", () => t.GetApartmentState());
            Show("apartment STA", () => { t.SetApartmentState(ApartmentState.STA); return "ok"; });
            Show("apartment MTA", () => { t.SetApartmentState(ApartmentState.MTA); return "ok"; });
            Show("apartment Unknown", () => { t.SetApartmentState(ApartmentState.Unknown); return "ok"; });
            Show("apartment bad", () => { t.SetApartmentState((ApartmentState)5); return "ok"; });
            Show("try STA", () => t.TrySetApartmentState(ApartmentState.STA));
            Show("try Unknown", () => t.TrySetApartmentState(ApartmentState.Unknown));
            Show("try bad", () => t.TrySetApartmentState((ApartmentState)7));
            t.Start();
            t.Join();
            Show("apartment after start", () => { t.SetApartmentState(ApartmentState.MTA); return "ok"; });
            Show("apartment current", () => Thread.CurrentThread.GetApartmentState());
        }
    }
}
