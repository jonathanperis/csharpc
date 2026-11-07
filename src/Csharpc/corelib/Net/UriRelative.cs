// System.Uri's relative-URI half, after dotnet/runtime's Uri.cs and UriExt.cs (release/11.0, MIT) for the URIs
// Net/Uri.cs models (no escaping, no user info): MakeRelativeUri / MakeRelative (PathDifference), base +
// relative resolution (CombineUri) behind the Uri(Uri, string) constructors and TryCreate, GetComponents and
// Compare, IsBaseOf (UriHelper.TestForSubPath), IsWellFormedOriginalString, TryFormat. Resolving against a file
// URI, a relative reference .NET would read as a URI with a scheme ("g:h", "http:g") and everything the parser
// in Uri.cs refuses stay NotSupportedException.
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    public partial class UriImpl
    {
        // ---- MakeRelativeUri ----

        public Uri MakeRelativeUri(Uri uri)
        {
            if (uri == null) throw new ArgumentNullException("uri");
            UriImpl other = (UriImpl)(object)uri;
            if (_relative || other._relative) throw NotAbsolute();

            // The user info is ignored when computing a relative Uri.
            if (_scheme == other._scheme && _host == other._host && _port == other._port)
            {
                string otherPath = other.AbsolutePath;
                string relativeUriString = PathDifference(AbsolutePath, otherPath, !IsDosPath);

                // A relative reference's first path segment cannot hold a ':' (RFC 3986, 4.2), except for a
                // full DOS path.
                if (ColonInFirstPathSegment(relativeUriString) && !(other.IsDosPath && otherPath.Equals(relativeUriString, StringComparison.Ordinal)))
                    relativeUriString = "./" + relativeUriString;

                relativeUriString += other._query + other._fragment;
                return (Uri)(object)new UriImpl(relativeUriString, new RawRelative());
            }
            return uri;
        }

        public string MakeRelative(Uri toUri)
        {
            if (toUri == null) throw new ArgumentNullException("toUri");
            UriImpl other = (UriImpl)(object)toUri;
            if (_relative || other._relative) throw NotAbsolute();
            if (_scheme == other._scheme && _host == other._host && _port == other._port)
                return PathDifference(AbsolutePath, other.AbsolutePath, !IsDosPath);
            return toUri.ToString();
        }

        private static bool ColonInFirstPathSegment(string uriString)
        {
            int index = uriString.IndexOfAny(new[] { ':', '\\', '/', '?', '#' });
            return index >= 0 && uriString[index] == ':';
        }

        private static string PathDifference(string path1, string path2, bool compareCase)
        {
            int i;
            int si = -1;
            for (i = 0; (i < path1.Length) && (i < path2.Length); ++i)
            {
                if ((path1[i] != path2[i]) && (compareCase || (char.ToLowerInvariant(path1[i]) != char.ToLowerInvariant(path2[i]))))
                    break;
                else if (path1[i] == '/')
                    si = i;
            }
            if (i == 0) return path2;
            if ((i == path1.Length) && (i == path2.Length)) return string.Empty;

            StringBuilder relPath = new StringBuilder();
            for (; i < path1.Length; ++i)
                if (path1[i] == '/') relPath.Append("../");
            // Same path except that path1 ended with a file name and path2 didn't
            if (relPath.Length == 0 && path2.Length - 1 == si) return "./";
            return relPath.Append(path2, si + 1, path2.Length - si - 1).ToString();
        }

        // ---- base + relative ----

        public UriImpl(Uri baseUri, string relativeUri) : this(ResolveToString(baseUri, relativeUri), UriKind.Absolute) { }

        public UriImpl(Uri baseUri, Uri relativeUri) : this(ResolveToString(baseUri, relativeUri), UriKind.Absolute) { }

        public UriImpl(Uri baseUri, string relativeUri, bool dontEscape) : this(ResolveToString(baseUri, relativeUri), UriKind.Absolute) { _userEscaped = dontEscape; }

        public static bool TryCreate(Uri baseUri, string relativeUri, out Uri result)
        {
            if (TryCreate(relativeUri, UriKind.RelativeOrAbsolute, out Uri relativeLink))
            {
                if (!relativeLink.IsAbsoluteUri) return TryCreate(baseUri, relativeLink, out result);
                result = relativeLink;
                return true;
            }
            result = null;
            return false;
        }

        public static bool TryCreate(Uri baseUri, Uri relativeUri, out Uri result)
        {
            result = null;
            if (baseUri == null || relativeUri == null) return false;
            UriImpl b = (UriImpl)(object)baseUri;
            if (b._relative) return false;
            return TryCreate(ResolveRelative(b, (UriImpl)(object)relativeUri), UriKind.Absolute, out result);
        }

        private static string ResolveToString(Uri baseUri, string relativeUri)
        {
            if (baseUri == null) throw new ArgumentNullException("baseUri");
            if (((UriImpl)(object)baseUri)._relative) throw new ArgumentOutOfRangeException("baseUri");
            // null reads as "" (.NET resolves it to the base).
            return ResolveRelative((UriImpl)(object)baseUri, new UriImpl(relativeUri ?? "", UriKind.RelativeOrAbsolute));
        }

        private static string ResolveToString(Uri baseUri, Uri relativeUri)
        {
            if (baseUri == null) throw new ArgumentNullException("baseUri");
            if (relativeUri == null) throw new ArgumentNullException("relativeUri");
            if (((UriImpl)(object)baseUri)._relative) throw new ArgumentOutOfRangeException("baseUri");
            return ResolveRelative((UriImpl)(object)baseUri, (UriImpl)(object)relativeUri);
        }

        // ResolveHelper: the string of the Uri that `relative` names against absolute `b`; an absolute `relative`
        // names itself.
        private static string ResolveRelative(UriImpl b, UriImpl relative)
        {
            if (!relative._relative) return relative._original;
            string rel = relative._original;
            if (b._file) throw Unsupported("a relative reference resolved against a file URI");
            if (rel.Length == 0) return b.AbsoluteUri;
            if (rel[0] == '#') return b._scheme + "://" + b.Authority + b._path + b._query + rel;
            if (rel[0] == '?') return b._scheme + "://" + b.Authority + b._path + rel;
            // "g:h" and "http:g" name a scheme to .NET, "C:\x" a DOS path.
            int separator = rel.IndexOfAny(new[] { ':', '\\', '/', '?', '#' });
            if (separator >= 0 && rel[separator] == ':') throw Unsupported("a relative reference with a ':' in its first segment");
            if (rel[0] == '/')
            {
                if (rel.Length >= 2 && rel[1] == '/') return b._scheme + ":" + rel;
                return b._scheme + "://" + b.Authority + rel;
            }
            // A relative path: the base path up to its last slash, then the reference, dot segments removed
            // (the query and fragment stay as they are).
            string left = b._path;
            left = left.Substring(0, left.LastIndexOf('/') + 1);
            int extraAt = rel.IndexOfAny(new[] { '?', '#' });
            string relPath = extraAt < 0 ? rel : rel.Substring(0, extraAt);
            string extra = extraAt < 0 ? "" : rel.Substring(extraAt);
            return b._scheme + "://" + b.Authority + Compress(left + relPath) + extra;
        }

        // ---- IsBaseOf ----

        public bool IsBaseOf(Uri uri)
        {
            if (uri == null) throw new ArgumentNullException("uri");
            if (_relative) return false;
            UriImpl link = (UriImpl)(object)uri;
            if (link._relative)
            {
                if (!TryCreate(ResolveRelative(this, link), UriKind.Absolute, out Uri resolved)) return false;
                link = (UriImpl)(object)resolved;
            }
            if (_scheme != link._scheme) return false;
            const UriComponents Compared = UriComponents.AbsoluteUri & ~UriComponents.Fragment & ~UriComponents.UserInfo;
            return TestForSubPath(GetParts(Compared, UriFormat.SafeUnescaped), link.GetParts(Compared, UriFormat.SafeUnescaped), IsDosPath || link.IsDosPath);
        }

        // UriHelper.TestForSubPath: the path of `self` up to its last slash is a prefix of `other`'s.
        private static bool TestForSubPath(string self, string other, bool ignoreCase)
        {
            int i = 0;
            bool allSameBeforeSlash = true;
            for (; i < self.Length && i < other.Length; ++i)
            {
                char chSelf = self[i], chOther = other[i];
                if (chSelf == '?' || chSelf == '#') return true;
                if (chSelf == '/')
                {
                    if (chOther != '/') return false;
                    if (!allSameBeforeSlash) return false;
                    allSameBeforeSlash = true;
                    continue;
                }
                if (chOther == '?' || chOther == '#') break;
                if (!ignoreCase)
                {
                    if (chSelf != chOther) allSameBeforeSlash = false;
                }
                else if (char.ToLowerInvariant(chSelf) != char.ToLowerInvariant(chOther)) allSameBeforeSlash = false;
            }
            for (; i < self.Length; ++i)
            {
                char c = self[i];
                if (c == '?' || c == '#') return true;
                if (c == '/') return false;
            }
            return true;
        }

        // ---- GetComponents / Compare ----

        public string GetComponents(UriComponents components, UriFormat format)
        {
            if (((components & UriComponents.SerializationInfoString) != 0) && components != UriComponents.SerializationInfoString)
                throw new ArgumentOutOfRangeException("components", components, "UriComponents.SerializationInfoString must not be combined with other UriComponents.");
            if (((int)format & ~(int)UriFormat.SafeUnescaped) != 0) throw new ArgumentOutOfRangeException("format");
            if (_relative)
            {
                if (components == UriComponents.SerializationInfoString) return _original;
                throw NotAbsolute();
            }
            return GetParts(components, format);
        }

        // Everything a URI of this subset holds is its own escaped, unescaped and safe-unescaped form.
        internal string GetParts(UriComponents parts, UriFormat format)
        {
            if (parts == UriComponents.Scheme) return _scheme;
            if ((parts & UriComponents.SerializationInfoString) != 0) parts |= UriComponents.AbsoluteUri;
            if ((parts & UriComponents.NormalizedHost) != 0)
            {
                parts |= UriComponents.Host;
                if (_host.Contains("xn--")) throw Unsupported("NormalizedHost of a punycode host name");
            }
            int defaultPort = DefaultPort(_scheme);
            bool notDefaultPort = _port != defaultPort;
            if (parts == UriComponents.Port || parts == UriComponents.StrongPort)
                return notDefaultPort || (parts == UriComponents.StrongPort && defaultPort != -1) ? _port.ToString() : "";
            if ((parts & UriComponents.StrongPort) != 0) parts |= UriComponents.Port;
            // A host spelled differently from its canonical form is rebuilt, which needs a valid format.
            if (parts == UriComponents.Host && (format == UriFormat.UriEscaped || HostIsCanonicalInOriginal())) return _host;
            if (format != UriFormat.UriEscaped && format != UriFormat.Unescaped && format != UriFormat.SafeUnescaped)
                throw new ArgumentOutOfRangeException("uriFormat");

            var sb = new StringBuilder();
            if ((parts & UriComponents.Scheme) != 0)
            {
                sb.Append(_scheme);
                if (parts != UriComponents.Scheme) sb.Append("://");
            }
            if ((parts & UriComponents.Host) != 0) sb.Append(_host);
            if ((parts & UriComponents.Port) != 0 && (notDefaultPort || ((parts & UriComponents.StrongPort) != 0 && defaultPort != -1)))
                sb.Append(':').Append(_port.ToString());
            if ((parts & UriComponents.Path) != 0)
            {
                string path = _path;
                if (parts == UriComponents.Path && path.Length != 0 && path[0] == '/') path = path.Substring(1);
                sb.Append(path);
                if (parts == UriComponents.Path) return sb.ToString();
            }
            if ((parts & UriComponents.Query) != 0 && _query.Length != 0)
                sb.Append(parts == UriComponents.Query ? _query.Substring(1) : _query);
            if ((parts & UriComponents.Fragment) != 0 && _fragment.Length != 0)
                sb.Append(parts == UriComponents.Fragment ? _fragment.Substring(1) : _fragment);
            return sb.ToString();
        }

        private bool HostIsCanonicalInOriginal()
        {
            if (_file) return true;
            int start = _original.IndexOf("://", StringComparison.Ordinal) + 3;
            int end = _original.IndexOfAny(new[] { ':', '/', '?', '#' }, start);
            return string.CompareOrdinal(_original, start, _host, 0, end < 0 ? _original.Length - start : end - start) == 0 && (end < 0 ? _original.Length - start : end - start) == _host.Length;
        }

        public static int Compare(Uri uri1, Uri uri2, UriComponents partsToCompare, UriFormat compareFormat, StringComparison comparisonType)
        {
            if (uri1 == null) return uri2 == null ? 0 : -1;
            if (uri2 == null) return 1;
            UriImpl a = (UriImpl)(object)uri1, b = (UriImpl)(object)uri2;
            // A relative uri is always less than an absolute one.
            if (a._relative || b._relative)
                return !a._relative ? 1 : !b._relative ? -1 : string.Compare(a._original, b._original, comparisonType);
            return string.Compare(a.GetParts(partsToCompare, compareFormat), b.GetParts(partsToCompare, compareFormat), comparisonType);
        }

        // ---- well-formedness, formatting, host names ----

        // For this subset a reference is well formed unless it is an implicit file path or a relative reference
        // with a fragment.
        public bool IsWellFormedOriginalString() => _relative ? _original.IndexOf('#') < 0 : !_implicitFile;

        public static bool IsWellFormedUriString(string uriString, UriKind uriKind)
        {
            if (!TryCreate(uriString, uriKind, out Uri result)) return false;
            return result.IsWellFormedOriginalString();
        }

        public bool TryFormat(Span<char> destination, out int charsWritten)
        {
            string text = ToString();
            if (text.Length > destination.Length)
            {
                charsWritten = 0;
                return false;
            }
            text.AsSpan().CopyTo(destination);
            charsWritten = text.Length;
            return true;
        }

        // Host names of the shape Net/Uri.cs parses (DNS names, canonical IPv4 addresses); the others .NET
        // classifies by rules that aren't ported.
        public static UriHostNameType CheckHostName(string name)
        {
            if (string.IsNullOrEmpty(name)) return UriHostNameType.Unknown;
            if (IsSimpleHost(name.ToLowerInvariant(), out bool ipv4)) return ipv4 ? UriHostNameType.IPv4 : UriHostNameType.Dns;
            throw Unsupported("the host name '" + name + "'");
        }
    }
}
