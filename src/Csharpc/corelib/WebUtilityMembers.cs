// System.Net.WebUtility.UrlDecode(string), after dotnet/runtime's WebUtility.UrlDecode and its UrlDecoder:
// '+' is a space and %XX collects bytes decoded as UTF-8 (an invalid sequence becomes U+FFFD); a malformed
// escape is kept as written. (The legacy %uXXXX form is not decoded since .NET 5.)
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Net.WebUtility")]
    internal static class WebUtilityMembers
    {
        /// <summary>Letters, digits and -_.!*() stay; a space is '+'; every other byte of the UTF-8 form becomes %XX (upper-case hex).</summary>
        public static string UrlEncode(string value)
        {
            if (value == null) return null;
            if (value.Length == 0) return string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            var result = new StringBuilder(bytes.Length + 8);
            foreach (byte b in bytes)
            {
                char ch = (char)b;
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.' || ch == '!' || ch == '*' || ch == '(' || ch == ')')
                    result.Append(ch);
                else if (ch == ' ') result.Append('+');
                else
                {
                    result.Append('%');
                    result.Append("0123456789ABCDEF"[b >> 4]);
                    result.Append("0123456789ABCDEF"[b & 15]);
                }
            }
            return result.ToString();
        }

        public static string UrlDecode(string encodedValue)
        {
            if (encodedValue == null) return null;
            int count = encodedValue.Length;
            var result = new StringBuilder(count);
            byte[] bytes = null;
            int byteCount = 0;

            void Flush()
            {
                if (byteCount == 0) return;
                result.Append(Encoding.UTF8.GetString(bytes, 0, byteCount));
                byteCount = 0;
            }

            for (int pos = 0; pos < count; pos++)
            {
                char ch = encodedValue[pos];
                if (ch == '+') ch = ' ';
                else if (ch == '%' && pos < count - 2)
                {
                    {
                        int h1 = Hex(encodedValue[pos + 1]), h2 = Hex(encodedValue[pos + 2]);
                        if (h1 >= 0 && h2 >= 0)
                        {
                            bytes ??= new byte[count];
                            bytes[byteCount++] = (byte)((h1 << 4) | h2);
                            pos += 2;
                            continue;
                        }
                    }
                }
                if ((ch & 0xFF80) == 0)
                {
                    // 7-bit chars join the pending bytes: they end a multi-byte sequence the way real bytes do.
                    bytes ??= new byte[count];
                    bytes[byteCount++] = (byte)ch;
                }
                else
                {
                    Flush();
                    result.Append(ch);
                }
            }
            Flush();
            return result.ToString();
        }

        private static int Hex(char c) =>
            c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
    }
}
