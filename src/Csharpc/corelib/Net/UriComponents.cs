// Uri.GetComponents for the component sets System.Net.Http asks for (the Location header's absolute URI, request
// targets). The rest of UriComponents' combinations (Unescaped forms, delimiters, user info) are explicit
// NotSupportedExceptions rather than a guess at .NET's rules.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Uri")]
    internal static class UriGetComponentsMembers
    {
        // The span form of EscapeDataString (FormUrlEncodedContent escapes each name and value with it).
        public static bool TryEscapeDataString(ReadOnlySpan<char> charsToEscape, Span<char> destination, out int charsWritten)
        {
            string escaped = Uri.EscapeDataString(charsToEscape);
            if (escaped.Length > destination.Length) { charsWritten = 0; return false; }
            escaped.AsSpan().CopyTo(destination);
            charsWritten = escaped.Length;
            return true;
        }

        [ShadowInstance]
        public static string GetComponents(Uri uri, UriComponents components, UriFormat format)
        {
            if (format < UriFormat.UriEscaped || format > UriFormat.SafeUnescaped) throw new ArgumentOutOfRangeException("format");
            if (!uri.IsAbsoluteUri) throw new InvalidOperationException("This operation is not supported for a relative URI.");
            if (format == UriFormat.UriEscaped)
            {
                switch (components)
                {
                    case UriComponents.AbsoluteUri: return uri.AbsoluteUri;
                    case UriComponents.PathAndQuery: return uri.PathAndQuery;
                    case UriComponents.HttpRequestUrl: return uri.Scheme + "://" + uri.Authority + uri.PathAndQuery;
                    case UriComponents.Scheme: return uri.Scheme;
                    case UriComponents.Host: return uri.Host;
                    case UriComponents.Query: return uri.Query.Length > 0 ? uri.Query.Substring(1) : string.Empty;
                    case UriComponents.Fragment: return uri.Fragment.Length > 0 ? uri.Fragment.Substring(1) : string.Empty;
                }
            }
            throw new NotSupportedException("csharpc: Uri.GetComponents(" + components + ", " + format + ") is not supported yet.");
        }
    }
}
