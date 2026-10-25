// System.Uri, the subset whose .NET behaviour is simple to state exactly: absolute http, https, ftp, ws
// and wss URIs with a DNS name or a canonical IPv4 address, an optional port, and a path, query and
// fragment made of characters .NET neither escapes nor unescapes (RFC 3986 unreserved, sub-delims, ':'
// and '@', no '%'), without dot segments. For those, .NET only lowercases the scheme and host, drops a
// default port and turns an empty path into "/". Everything else .NET accepts (relative URIs, other
// schemes, user info, IPv6, percent-encoding, escaping, IDN, dot-segment removal, implicit file paths)
// throws NotSupportedException rather than risk a different result. Also exact: file URIs of plain
// absolute paths ("/tmp/x.xml", "C:\\dir\\x.xml", "file:///tmp/x.xml": System.Xml loads files through
// them) and relative URIs of plain text (UriKind.RelativeOrAbsolute / Relative). UnescapeDataString is
// exact; the static escaping helpers are in UriMembers.cs.
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
        private readonly bool _relative;    // a relative URI: only OriginalString / ToString
        private readonly bool _file;        // a file URI (_path: its absolute path, '/'-separated)
        private readonly string _localPath; // a file URI's LocalPath

        public UriImpl(string uriString) : this(uriString, UriKind.Absolute) { }

        public UriImpl(string uriString, UriKind uriKind)
        {
            if (uriString == null) throw new ArgumentNullException("uriString");
            if (uriKind != UriKind.Absolute && uriKind != UriKind.RelativeOrAbsolute && uriKind != UriKind.Relative)
                throw new ArgumentException("The value '" + ((int)uriKind).ToString() + "' passed for the UriKind parameter is invalid.", "uriKind");
            _original = uriString;
            if (TryParseFile(uriString, out _path, out _localPath))
            {
                if (uriKind == UriKind.Relative) throw new UriFormatException(FormatNotDetermined);
                _file = true;
                _scheme = "file";
                _host = "";
                _port = -1;
                _query = _fragment = "";
                return;
            }
            string error = Parse(uriString, out _scheme, out _host, out _port, out _ipv4, out _path, out _query, out _fragment);
            if (error == FormatNotDetermined && uriKind != UriKind.Absolute)
            {
                CheckRelative(uriString);
                _relative = true;
                return;
            }
            if (error != null) throw new UriFormatException(error);
            if (uriKind == UriKind.Relative) throw new UriFormatException(FormatNotDetermined);
        }

        // A relative reference is kept as it is: only text .NET neither escapes nor unescapes.
        private static void CheckRelative(string s)
        {
            if (s.Trim().Length == 0 || !IsPlain(s, 0, true) || s.IndexOf('#') >= 0) throw Unsupported("this relative URI");
        }

        /// <summary>
        /// A file URI with a plain absolute path: an implicit one ("/dir/x", "C:\dir\x" or "C:/dir/x") or
        /// "file:///...". `path` is the URI's path ("/dir/x", "/C:/dir/x"), `localPath` LocalPath.
        /// </summary>
        private static bool TryParseFile(string s, out string path, out string localPath)
        {
            path = localPath = null;
            string rest;
            bool explicitScheme = s.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
            if (explicitScheme)
            {
                if (string.CompareOrdinal(s, 5, "///", 0, 3) != 0) throw Unsupported("this file URI");
                rest = s.Substring(7);
                if (rest.Length >= 3 && char.IsAsciiLetter(rest[1]) && rest[2] == ':') rest = rest.Substring(1); // file:///C:/...
            }
            else rest = s;
            bool drive = rest.Length >= 3 && char.IsAsciiLetter(rest[0]) && rest[1] == ':' && (rest[2] == '\\' || rest[2] == '/');
            if (!drive && (rest.Length == 0 || rest[0] != '/'))
            {
                if (explicitScheme) throw Unsupported("this file URI");
                return false;
            }
            if (!drive && rest.Length > 1 && (rest[1] == '/' || rest[1] == '\\')) throw Unsupported("UNC paths");
#if CSHARPC_TARGET_WINDOWS
            // .NET on Windows takes "/dir/x" for no implicit file path, and gives drive-less file URIs other
            // local paths than on Unix: not modelled.
            if (!drive) throw Unsupported("drive-less file paths on Windows");
#endif
            string slashed = drive ? rest.Replace('\\', '/') : rest;
            if (!IsPlain(slashed, 0, false) || HasDotSegment(slashed) || slashed.Contains("//")) throw Unsupported("file paths .NET escapes or rewrites");
            path = drive ? "/" + slashed : slashed;
            localPath = drive ? slashed.Replace('/', '\\') : slashed;
            return true;
        }

        private InvalidOperationException NotAbsolute() => new InvalidOperationException("This operation is not supported for a relative URI.");
        private string Absolute(string value) => _relative ? throw NotAbsolute() : value;

        public UriImpl(Uri baseUri, string relativeUri) => throw Unsupported("new Uri(Uri, string)");
        public UriImpl(Uri baseUri, Uri relativeUri) => throw Unsupported("new Uri(Uri, Uri)");

        public static bool TryCreate(string uriString, UriKind uriKind, out Uri result)
        {
            result = null;
            if (uriString == null) return false;
            if (TryParseFile(uriString, out _, out _))
            {
                if (uriKind == UriKind.Relative) return false;
                result = (Uri)(object)new UriImpl(uriString, UriKind.Absolute);
                return true;
            }
            string error = Parse(uriString, out _, out _, out _, out _, out _, out _, out _);
            if (error == FormatNotDetermined && uriKind != UriKind.Absolute)
            {
                CheckRelative(uriString);
                result = (Uri)(object)new UriImpl(uriString, uriKind);
                return true;
            }
            if (error != null || uriKind == UriKind.Relative) return false;
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
        public bool IsAbsoluteUri => !_relative;
        public string Scheme => Absolute(_scheme);
        public string Host => Absolute(_host);
        public string IdnHost => Absolute(_host);
        public string DnsSafeHost => Absolute(_host);
        public int Port => _relative ? throw NotAbsolute() : _port;
        public bool IsDefaultPort => _relative ? throw NotAbsolute() : _port == DefaultPort(_scheme);
        public string Authority => Absolute(_file ? "" : IsDefaultPort ? _host : _host + ":" + _port.ToString());
        public string AbsolutePath => Absolute(_path);
        public string LocalPath => Absolute(_file ? _localPath : _path);
        public string Query => Absolute(_query);
        public string Fragment => Absolute(_fragment);
        public string PathAndQuery => Absolute(_path + _query);
        public string UserInfo => Absolute("");
        public bool UserEscaped => false;
        public bool IsFile => _relative ? throw NotAbsolute() : _file;
        public bool IsUnc => _relative ? throw NotAbsolute() : false;
        public bool IsLoopback => _relative ? throw NotAbsolute() : _file || _host == "localhost" || _host == "127.0.0.1";
        public UriHostNameType HostNameType => _relative ? throw NotAbsolute() : _file ? UriHostNameType.Basic : _ipv4 ? UriHostNameType.IPv4 : UriHostNameType.Dns;
        public string AbsoluteUri => Absolute(_scheme + "://" + Authority + _path + _query + _fragment);

        public string[] Segments
        {
            get
            {
                if (_relative) throw NotAbsolute();
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

        public string GetLeftPart(UriPartial part) => _relative ? throw NotAbsolute() : part switch
        {
            UriPartial.Scheme => _scheme + "://",
            UriPartial.Authority => _scheme + "://" + Authority,
            UriPartial.Path => _scheme + "://" + Authority + _path,
            UriPartial.Query => _scheme + "://" + Authority + _path + _query,
            _ => throw new ArgumentException("The part parameter has an invalid value.", "part"),
        };

        private static int DefaultPort(string scheme) => scheme switch { "http" => 80, "https" => 443, "ftp" => 21, "ws" => 80, "wss" => 443, _ => -1 };

        public override string ToString() => _relative ? _original : AbsoluteUri;

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
            if (_relative || other._relative) return _relative && other._relative && _original == other._original;
            return _scheme == other._scheme && _host == other._host && _port == other._port && _path == other._path && _query == other._query;
        }

        public bool Equals(Uri other) => Equals((object)other);

        public override int GetHashCode() => _relative ? _original.GetHashCode() : (_scheme + "://" + Authority + _path + _query).GetHashCode();

        public static bool operator ==(UriImpl uri1, UriImpl uri2) => ReferenceEquals(uri1, uri2) || (uri1 is not null && uri1.Equals((object)uri2));
        public static bool operator !=(UriImpl uri1, UriImpl uri2) => !(uri1 == uri2);

        public static string UnescapeDataString(ReadOnlySpan<char> charsToUnescape) => UnescapeDataString(new string(charsToUnescape));

        public static bool TryUnescapeDataString(ReadOnlySpan<char> charsToUnescape, Span<char> destination, out int charsWritten)
        {
            string unescaped = UnescapeDataString(new string(charsToUnescape));
            if (unescaped.Length > destination.Length) { charsWritten = 0; return false; }
            unescaped.AsSpan().CopyTo(destination);
            charsWritten = unescaped.Length;
            return true;
        }

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
