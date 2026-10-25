// System.UriBuilder, after dotnet/runtime's UriBuilder.cs (release/11.0, MIT): the components, and
// ToString's composition (the port whenever one is set, as .NET writes it). Paths needing escaping are
// not supported yet (NotSupportedException rather than a differently escaped string).
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.UriBuilder")]
    public class UriBuilderImpl
    {
        private string _scheme = "http";
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _host = "localhost";
        private int _port = -1;
        private string _path = "/";
        private string _query = string.Empty;
        private string _fragment = string.Empty;
        private bool _changed = true;
        private Uri _uri;

        public UriBuilderImpl() { }

        public UriBuilderImpl(string uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            // A string without a scheme ("www.contoso.com") is taken as http.
            _uri = Uri.TryCreate(uri, UriKind.Absolute, out Uri absolute) ? absolute : new Uri(Uri.UriSchemeHttp + Uri.SchemeDelimiter + uri);
            SetFieldsFromUri();
        }

        public UriBuilderImpl(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            _uri = uri;
            SetFieldsFromUri();
        }

        public UriBuilderImpl(string schemeName, string hostName)
        {
            Scheme = schemeName;
            Host = hostName;
        }

        public UriBuilderImpl(string scheme, string host, int portNumber) : this(scheme, host) { Port = portNumber; }

        public UriBuilderImpl(string scheme, string host, int port, string pathValue) : this(scheme, host, port) { Path = pathValue; }

        public string Scheme
        {
            get => _scheme;
            set
            {
                value ??= string.Empty;
                if (value.Length != 0)
                {
                    if (!Uri.CheckSchemeName(value))
                    {
                        int index = value.IndexOf(':');
                        if (index != -1) value = value.Substring(0, index);
                        if (!Uri.CheckSchemeName(value)) throw new ArgumentException("Invalid URI: The URI scheme is not valid.", "value");
                    }
                    value = value.ToLowerInvariant();
                }
                _scheme = value;
                _changed = true;
            }
        }

        public string UserName
        {
            get => _username;
            set { _username = value ?? string.Empty; _changed = true; }
        }

        public string Password
        {
            get => _password;
            set { _password = value ?? string.Empty; _changed = true; }
        }

        public string Host
        {
            get => _host;
            set
            {
                if (!string.IsNullOrEmpty(value) && value.AsSpan().IndexOfAny(":/\\?#@[]") >= 0)
                {
                    if (value.Contains(':'))
                    {
                        if (!value.StartsWith('[') || !value.EndsWith(']')) value = "[" + value + "]";
                        if (value.AsSpan(1, value.Length - 2).IndexOfAny("/\\?#@[]") >= 0)
                            throw new ArgumentException("Invalid URI: The hostname could not be parsed.", "value");
                    }
                    else throw new ArgumentException("Invalid URI: The hostname could not be parsed.", "value");
                }
                _host = value ?? string.Empty;
                _changed = true;
            }
        }

        public int Port
        {
            get => _port;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 0xFFFF);
                _port = value;
                _changed = true;
            }
        }

        public string Path
        {
            get => _path;
            set
            {
                if (string.IsNullOrEmpty(value)) _path = "/";
                else
                {
                    value = value.Replace('\\', '/');
                    foreach (char c in value)
                        if (c <= ' ' || c >= 0x7F || c == '"' || c == '<' || c == '>' || c == '^' || c == '`' || c == '{' || c == '|' || c == '}' || c == '%')
                            throw new NotSupportedException("UriBuilder.Path with characters that need escaping is not supported by csharpc yet.");
                    _path = value;
                }
                _changed = true;
            }
        }

        public string Query
        {
            get => _query;
            set
            {
                if (!string.IsNullOrEmpty(value) && value[0] != '?') value = '?' + value;
                _query = value ?? string.Empty;
                _changed = true;
            }
        }

        public string Fragment
        {
            get => _fragment;
            set
            {
                if (!string.IsNullOrEmpty(value) && value[0] != '#') value = '#' + value;
                _fragment = value ?? string.Empty;
                _changed = true;
            }
        }

        public Uri Uri
        {
            get
            {
                if (_changed)
                {
                    _uri = new Uri(ToString());
                    SetFieldsFromUri();
                    _changed = false;
                }
                return _uri;
            }
        }

        public override bool Equals(object rparam) => rparam is not null && Uri.Equals(new Uri(rparam.ToString()));
        public override int GetHashCode() => Uri.GetHashCode();

        private static string EncodeUserInfo(string input) =>
            input.AsSpan().IndexOfAny("/\\?#@") < 0 ? input
                : input.Replace("/", "%2F").Replace("\\", "%5C").Replace("?", "%3F").Replace("#", "%23").Replace("@", "%40");

        private void SetFieldsFromUri()
        {
            _scheme = _uri.Scheme;
            _host = _uri.Host;
            _port = _uri.Port;
            _path = _uri.AbsolutePath;
            _query = _uri.Query;
            _fragment = _uri.Fragment;
            string userInfo = _uri.UserInfo;
            if (userInfo.Length > 0)
            {
                int index = userInfo.IndexOf(':');
                if (index != -1)
                {
                    _password = userInfo.Substring(index + 1);
                    _username = userInfo.Substring(0, index);
                }
                else _username = userInfo;
            }
        }

        public override string ToString()
        {
            if (UserName.Length == 0 && Password.Length != 0)
                throw new UriFormatException("Invalid URI: The username:password construct is badly formed.");
            var sb = new StringBuilder();
            string scheme = Scheme, host = Host;
            if (scheme.Length != 0)
            {
                // The registered schemes' delimiters (UriParser): authority schemes "://", mailto/news/urn ":";
                // an unknown scheme "://" when there is a host.
                string delimiter = scheme switch
                {
                    "http" or "https" or "ws" or "wss" or "ftp" or "ftps" or "file" or "gopher" or "nntp" or "telnet" or "ldap" or "net.tcp" or "net.pipe" => "://",
                    "mailto" or "news" or "urn" => ":",
                    _ => host.Length == 0 ? ":" : "://",
                };
                sb.Append(scheme).Append(delimiter);
            }
            string username = EncodeUserInfo(UserName);
            if (username.Length != 0)
            {
                sb.Append(username);
                string password = EncodeUserInfo(Password);
                if (password.Length != 0) sb.Append(':').Append(password);
                sb.Append('@');
            }
            if (host.Length != 0)
            {
                sb.Append(host);
                if (_port != -1) sb.Append(':').Append(_port);
            }
            string path = Path;
            if (path.Length != 0)
            {
                if (!path.StartsWith('/') && host.Length != 0) sb.Append('/');
                sb.Append(path);
            }
            sb.Append(Query).Append(Fragment);
            return sb.ToString();
        }
    }
}
