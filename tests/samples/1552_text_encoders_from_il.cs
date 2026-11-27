// Base64Url, Utf16 validation, WebUtility and SecurityElement from CoreLib's IL (#103).
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Net;
using System.Security;
using System.Text.Unicode;

static class Program
{
    static void Main()
    {
        byte[] data = { 1, 2, 250, 251, 252, 0, 255 };
        string encoded = Base64Url.EncodeToString(data);
        Console.WriteLine($"{encoded} {Convert.ToHexString(Base64Url.DecodeFromChars(encoded))} max {Base64Url.GetMaxDecodedLength(encoded.Length)} len {Base64Url.GetEncodedLength(data.Length)}");
        Span<char> chars = stackalloc char[16];
        Console.WriteLine($"{Base64Url.EncodeToChars(data, chars, out int read, out int written)} {read} {written} {chars[..written].ToString()}");
        Console.WriteLine($"valid {Base64Url.IsValid("AQL6-_w")} invalid {Base64Url.IsValid("AQ+/")}");
        try { Base64Url.DecodeFromChars("A@@"); } catch (FormatException e) { Console.WriteLine(e.GetType().Name); }

        Console.WriteLine($"{Utf16.IndexOfInvalidSubsequence("ab\ud800c")} {Utf16.IndexOfInvalidSubsequence("ok")} {Utf16.IsValid("x\udc00")}");

        Console.WriteLine(WebUtility.HtmlEncode("<a href=\"x\">&é'</a>"));
        Console.WriteLine(WebUtility.HtmlDecode("&lt;b&gt; &amp; &#233; &eacute; &#x41; &unknown;"));
        Console.WriteLine($"{WebUtility.UrlEncode("a b&c=d/é")} {WebUtility.UrlDecode("a+b%26c%3Dd%2F%C3%A9")}");
        Console.WriteLine($"{WebUtility.HtmlEncode(null) == null} {WebUtility.UrlDecode(null) == null}");

        Console.WriteLine(SecurityElement.Escape("<a & \"b\" 'c'>"));
        Console.WriteLine($"{SecurityElement.IsValidText("ok")} {SecurityElement.IsValidTag("a b")} {SecurityElement.Escape(null) == null}");
        var element = new SecurityElement("tag", "text");
        element.AddAttribute("name", "value");
        Console.WriteLine(element.ToString().Trim().Replace("\r\n", "\n").Replace("\n", "|"));
    }
}
