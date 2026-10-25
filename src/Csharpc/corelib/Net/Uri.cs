// System.Uri, the subset whose .NET behaviour is simple to state exactly: absolute http, https, ftp, ws
// and wss URIs with a DNS name or a canonical IPv4 address, an optional port, and a path, query and
// fragment made of characters .NET neither escapes nor unescapes (RFC 3986 unreserved, sub-delims, ':'
// and '@', no '%'), without dot segments. For those, .NET only lowercases the scheme and host, drops a
// default port and turns an empty path into "/". Everything else .NET accepts (relative URIs, other
// schemes, user info, IPv6, percent-encoding, escaping, IDN, dot-segment removal, implicit file paths)
// throws NotSupportedException rather than risk a different result. UnescapeDataString is exact; the
// static escaping helpers are in UriMembers.cs.
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Uri")]
    public class UriImpl : IEquatable<Uri>
    {
        public static readonly string UriSchemeHttp = "http";
        public static readonly string UriSchemeHttps = "https";
        public static readonly string UriSchemeFtp = "ftp";
        public static readonly string UriSchemeFtps = "ftps";
        public static readonly string UriSchemeFile = "file";
        public static readonly string UriSchemeMailto = "mailto";
        public static readonly string UriSchemeWs = "ws";
        public static readonly string UriSchemeWss = "wss";
        public static readonly string SchemeDelimiter = "://";

        private readonly string _original;
        private readonly string _scheme;
        private readonly string _host;
        private readonly int _port;
        private readonly bool _ipv4;
        private readonly string _path;
        private readonly string _query;
        private readonly string _fragment;

        public UriImpl(string uriString) : this(uriString, UriKind.Absolute) { }

        public UriImpl(string uriString, UriKind uriKind)
        {
            if (uriString == null) throw new ArgumentNullException("uriString");
            if (uriKind != UriKind.Absolute && uriKind != UriKind.RelativeOrAbsolute && uriKind != UriKind.Relative)
                throw new ArgumentException("The value '" + ((int)uriKind).ToString() + "' passed for the UriKind parameter is invalid.", "uriKind");
            if (uriKind == UriKind.Relative) throw Unsupported("relative URIs");
            string error = Parse(uriString, out _scheme, out _host, out _port, out _ipv4, out _path, out _query, out _fragment);
            if (error != null)
            {
                if (uriKind == UriKind.RelativeOrAbsolute && error == FormatNotDetermined) throw Unsupported("relative URIs");
                throw new UriFormatException(error);
            }
            _original = uriString;
        }

        public UriImpl(Uri baseUri, string relativeUri) => throw Unsupported("new Uri(Uri, string)");
        public UriImpl(Uri baseUri, Uri relativeUri) => throw Unsupported("new Uri(Uri, Uri)");

        public static bool TryCreate(string uriString, UriKind uriKind, out Uri result)
        {
            result = null;
            if (uriString == null) return false;
            if (uriKind == UriKind.Relative) throw Unsupported("relative URIs");
            string error = Parse(uriString, out _, out _, out _, out _, out _, out _, out _);
            if (error == FormatNotDetermined && uriKind == UriKind.RelativeOrAbsolute) throw Unsupported("relative URIs");
            if (error != null) return false;
            result = (Uri)(object)new UriImpl(uriString, UriKind.Absolute);
            return true;
        }

        private const string FormatNotDetermined = "Invalid URI: The format of the URI could not be determined.";

        private static NotSupportedException Unsupported(string what) =>
            new NotSupportedException("not supported by csharpc yet: System.Uri with " + what);

        // null: parsed; otherwise the UriFormatException message (only for errors .NET reports the same way).
        private static string Parse(string s, out string scheme, out string host, out int port, out bool ipv4,
            out string path, out string query, out string fragment)
        {
            scheme = host = path = query = fragment = null;
            port = 0;
            ipv4 = false;
            if (s.Length == 0) return "Invalid URI: The URI is empty.";
            int colon = s.IndexOf(':');
            if (colon < 0)
            {
                // Without a scheme only an implicit file path ("/x", "\\server\share") is absolute.
                string trimmed = s.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '/' || trimmed[0] == '\\') throw Unsupported("implicit file paths or blank strings");
                return FormatNotDetermined;
            }
            for (int i = 0; i < s.Length; i++)
                if (s[i] <= ' ' || s[i] >= 0x7F) throw Unsupported("whitespace, control or non-ASCII characters");
            scheme = s.Substring(0, colon).ToLowerInvariant();
            int defaultPort = scheme switch { "http" => 80, "https" => 443, "ftp" => 21, "ws" => 80, "wss" => 443, _ => -1 };
            if (defaultPort < 0) throw Unsupported("the scheme '" + s.Substring(0, colon) + "'");
            if (string.CompareOrdinal(s, colon + 1, "//", 0, 2) != 0) throw Unsupported("a hierarchical scheme without '//'");
            int start = colon + 3;
            int end = start;
            while (end < s.Length && s[end] != '/' && s[end] != '?' && s[end] != '#') end++;
            string authority = s.Substring(start, end - start);
            if (authority.IndexOf('@') >= 0) throw Unsupported("user info");
            if (authority.Length > 0 && authority[0] == '[') throw Unsupported("IPv6 addresses");
            int portAt = authority.IndexOf(':');
            host = (portAt < 0 ? authority : authority.Substring(0, portAt)).ToLowerInvariant();
            port = defaultPort;
            if (portAt >= 0)
            {
                string digits = authority.Substring(portAt + 1);
                if (digits.Length == 0 || digits.Length > 5) throw Unsupported("this port");
                int value = 0;
                foreach (char c in digits)
                {
                    if (c < '0' || c > '9') throw Unsupported("this port");
                    value = value * 10 + (c - '0');
                }
                if (value > 65535) throw Unsupported("this port");
                port = value;
            }
            if (!IsSimpleHost(host, out ipv4)) throw Unsupported("the host '" + host + "'");

            int queryAt = s.IndexOf('?', end), hashAt = s.IndexOf('#', end);
            if (queryAt > hashAt && hashAt >= 0) queryAt = -1; // a '?' inside the fragment
            int pathEnd = queryAt >= 0 ? queryAt : hashAt >= 0 ? hashAt : s.Length;
            path = s.Substring(end, pathEnd - end);
            query = queryAt >= 0 ? s.Substring(queryAt, (hashAt >= 0 ? hashAt : s.Length) - queryAt) : "";
            fragment = hashAt >= 0 ? s.Substring(hashAt) : "";
            if (!IsPlain(path, 0, false) || !IsPlain(query, 1, true) || !IsPlain(fragment, 1, true)) throw Unsupported("characters .NET escapes or unescapes");
            if (path.Length == 0) path = "/";
            if (HasDotSegment(path)) throw Unsupported("dot segments ('.' or '..') in the path");
            return null;
        }

        // RFC 3986 unreserved and sub-delims, ':' and '@', '/' (and '?' after a query or fragment start).
        private static bool IsPlain(string part, int from, bool questionMark)
        {
            for (int i = from; i < part.Length; i++)
            {
                char c = part[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                    || "-._~!$&'()*+,;=:@/".IndexOf(c) >= 0 || (questionMark && c == '?');
                if (!ok) return false;
            }
            return true;
        }

        private static bool HasDotSegment(string path)
        {
            foreach (string segment in path.Split('/'))
                if (segment == "." || segment == "..") return true;
            return false;
        }

        // A DNS name of letters, digits and inner hyphens per label, or a canonical dotted IPv4 address.
        private static bool IsSimpleHost(string host, out bool ipv4)
        {
            ipv4 = false;
            if (host.Length == 0 || host.Length > 255) return false;
            string[] labels = host.Split('.');
            bool numeric = true;
            foreach (string label in labels)
            {
                if (label.Length == 0 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-') return false;
                foreach (char c in label)
                {
                    if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
                    if (c < '0' || c > '9') numeric = false;
                }
            }
            if (!numeric) return !LooksNumericTail(labels);
            // .NET rewrites short or non-canonical IPv4 forms ("127.1", "010.0.0.1"): only the canonical one.
            if (labels.Length != 4) return false;
            foreach (string label in labels)
                if (label.Length > 3 || (label.Length > 1 && label[0] == '0') || int.Parse(label) > 255) return false;
            ipv4 = true;
            return true;
        }

        // "1.2.3.example" is a name; a name whose last label is all digits may be read as an address by .NET.
        private static bool LooksNumericTail(string[] labels)
        {
            string last = labels[labels.Length - 1];
            foreach (char c in last) if (c < '0' || c > '9') return false;
            return true;
        }

        public string OriginalString => _original;
        public bool IsAbsoluteUri => true;
        public string Scheme => _scheme;
        public string Host => _host;
        public string IdnHost => _host;
        public string DnsSafeHost => _host;
        public int Port => _port;
        public bool IsDefaultPort => _port == DefaultPort(_scheme);
        public string Authority => IsDefaultPort ? _host : _host + ":" + _port.ToString();
        public string AbsolutePath => _path;
        public string LocalPath => _path;
        public string Query => _query;
        public string Fragment => _fragment;
        public string PathAndQuery => _path + _query;
        public string UserInfo => "";
        public bool UserEscaped => false;
        public bool IsFile => false;
        public bool IsUnc => false;
        public UriHostNameType HostNameType => _ipv4 ? UriHostNameType.IPv4 : UriHostNameType.Dns;
        public string AbsoluteUri => _scheme + "://" + Authority + _path + _query + _fragment;

        public string[] Segments
        {
            get
            {
                var segments = new System.Collections.Generic.List<string>();
                int i = 0;
                while (i < _path.Length)
                {
                    int slash = _path.IndexOf('/', i);
                    int stop = slash < 0 ? _path.Length : slash + 1;
                    segments.Add(_path.Substring(i, stop - i));
                    i = stop;
                }
                return segments.ToArray();
            }
        }

        public string GetLeftPart(UriPartial part) => part switch
        {
            UriPartial.Scheme => _scheme + "://",
            UriPartial.Authority => _scheme + "://" + Authority,
            UriPartial.Path => _scheme + "://" + Authority + _path,
            UriPartial.Query => _scheme + "://" + Authority + _path + _query,
            _ => throw new ArgumentException("The part parameter has an invalid value.", "part"),
        };

        private static int DefaultPort(string scheme) => scheme switch { "http" => 80, "https" => 443, "ftp" => 21, "ws" => 80, "wss" => 443, _ => -1 };

        public override string ToString() => AbsoluteUri;

        // .NET compares the scheme, host, port, path and query (the scheme and host case-insensitively,
        // which the lowercased fields already are) and ignores the fragment; a string is parsed first.
        public override bool Equals(object comparand)
        {
            if (comparand == null) return false;
            if (ReferenceEquals(this, comparand)) return true;
            UriImpl other = comparand as UriImpl;
            if (other == null)
            {
                if (comparand is not string text || !TryCreate(text, UriKind.RelativeOrAbsolute, out Uri parsed)) return false;
                other = (UriImpl)(object)parsed;
            }
            return _scheme == other._scheme && _host == other._host && _port == other._port && _path == other._path && _query == other._query;
        }

        public bool Equals(Uri other) => Equals((object)other);

        public override int GetHashCode() => (_scheme + "://" + Authority + _path + _query).GetHashCode();

        public static bool operator ==(UriImpl uri1, UriImpl uri2) => ReferenceEquals(uri1, uri2) || (uri1 is not null && uri1.Equals((object)uri2));
        public static bool operator !=(UriImpl uri1, UriImpl uri2) => !(uri1 == uri2);

        // Uri.UnescapeDataString: %XX runs that decode as UTF-8; other '%' text stays as it is.
        public static string UnescapeDataString(string stringToUnescape)
        {
            if (stringToUnescape == null) throw new ArgumentNullException("stringToUnescape");
            if (stringToUnescape.IndexOf('%') < 0) return stringToUnescape;
            var sb = new StringBuilder(stringToUnescape.Length);
            var run = new System.Collections.Generic.List<byte>();
            int i = 0;
            while (i < stringToUnescape.Length)
            {
                if (stringToUnescape[i] == '%' && i + 2 < stringToUnescape.Length && Hex(stringToUnescape[i + 1]) >= 0 && Hex(stringToUnescape[i + 2]) >= 0)
                {
                    int begin = i;
                    run.Clear();
                    while (i + 2 < stringToUnescape.Length && stringToUnescape[i] == '%' && Hex(stringToUnescape[i + 1]) >= 0 && Hex(stringToUnescape[i + 2]) >= 0)
                    {
                        run.Add((byte)(Hex(stringToUnescape[i + 1]) * 16 + Hex(stringToUnescape[i + 2])));
                        i += 3;
                    }
                    bool ascii = true;
                    foreach (byte b in run) if (b >= 0x80) ascii = false;
                    if (!ascii)
                    {
                        string decoded;
                        try { decoded = new UTF8Encoding(false, true).GetString(run.ToArray()); }
                        catch (ArgumentException) { throw Unsupported("UnescapeDataString of escaped bytes that are not UTF-8"); }
                        sb.Append(decoded);
                    }
                    else
                        foreach (byte b in run) sb.Append((char)b);
                    continue;
                }
                sb.Append(stringToUnescape[i++]);
            }
            return sb.ToString();
        }

        private static int Hex(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        public static bool CheckSchemeName(string schemeName)
        {
            if (string.IsNullOrEmpty(schemeName) || !char.IsAsciiLetter(schemeName[0])) return false;
            foreach (char c in schemeName)
                if (!char.IsAsciiLetterOrDigit(c) && c != '+' && c != '-' && c != '.') return false;
            return true;
        }
    }
}
