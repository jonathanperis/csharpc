// System.Net.IPAddress / EndPoint / IPEndPoint. Parsing and formatting follow .NET's
// IPv4AddressHelper/IPv6AddressHelper and IPAddressParser (RFC 5952 output, inet_aton-style IPv4 input).
#nullable disable
using System;
using System.Net;
using System.Net.Sockets;

namespace CsharpcCoreLib
{
    [Shadows("System.Net.EndPoint")]
    public abstract class EndPointImpl
    {
        public virtual AddressFamily AddressFamily => throw new NotImplementedException();
    }

    [Shadows("System.Net.IPAddress")]
    public class IPAddressImpl
    {
        // IPv4: _v4 holds the address in network byte order as read left to right ("1.2.3.4" = 0x01020304).
        private readonly uint _v4;
        private readonly ushort[] _v6;  // null for IPv4
        private readonly long _scopeId;

        public static readonly IPAddress Any = new IPAddress(new byte[] { 0, 0, 0, 0 });
        public static readonly IPAddress Loopback = new IPAddress(new byte[] { 127, 0, 0, 1 });
        public static readonly IPAddress Broadcast = new IPAddress(new byte[] { 255, 255, 255, 255 });
        public static readonly IPAddress None = Broadcast;
        public static readonly IPAddress IPv6Any = new IPAddress(new byte[16], 0);
        public static readonly IPAddress IPv6None = IPv6Any;
        public static readonly IPAddress IPv6Loopback = new IPAddress(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, 0);

        public IPAddressImpl(long newAddress)
        {
            if (newAddress < 0 || newAddress > 0xFFFFFFFF) throw new ArgumentOutOfRangeException("newAddress");
            // The long is the address as stored in memory on a little-endian machine (.NET's "Address").
            uint v = (uint)newAddress;
            _v4 = (v << 24) | ((v & 0xFF00) << 8) | ((v >> 8) & 0xFF00) | (v >> 24);
        }

        public IPAddressImpl(byte[] address)
        {
            if (address == null) throw new ArgumentNullException("address");
            if (address.Length == 4) _v4 = (uint)(address[0] << 24 | address[1] << 16 | address[2] << 8 | address[3]);
            else if (address.Length == 16) _v6 = Words(address);
            else throw new ArgumentException("An invalid IP address was specified.", "address");
        }

        public IPAddressImpl(byte[] address, long scopeid)
        {
            if (address == null) throw new ArgumentNullException("address");
            if (address.Length != 16) throw new ArgumentException("An invalid IP address was specified.", "address");
            if (scopeid < 0 || scopeid > 0xFFFFFFFF) throw new ArgumentOutOfRangeException("scopeid");
            _v6 = Words(address);
            _scopeId = scopeid;
        }

        private IPAddressImpl(ushort[] words, long scopeId) { _v6 = words; _scopeId = scopeId; }
        private IPAddressImpl(uint v4, bool _) { _v4 = v4; }

        private static ushort[] Words(byte[] bytes)
        {
            var words = new ushort[8];
            for (int i = 0; i < 8; i++) words[i] = (ushort)(bytes[2 * i] << 8 | bytes[2 * i + 1]);
            return words;
        }

        public AddressFamily AddressFamily => _v6 == null ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;

        public long ScopeId
        {
            get
            {
                if (_v6 == null) throw new SocketException((int)SocketError.OperationNotSupported);
                return _scopeId;
            }
        }

        public bool IsIPv4MappedToIPv6 =>
            _v6 != null && _v6[0] == 0 && _v6[1] == 0 && _v6[2] == 0 && _v6[3] == 0 && _v6[4] == 0 && _v6[5] == 0xFFFF;

        public bool IsIPv6LinkLocal => _v6 != null && (_v6[0] & 0xFFC0) == 0xFE80;
        public bool IsIPv6Multicast => _v6 != null && (_v6[0] & 0xFF00) == 0xFF00;

        public byte[] GetAddressBytes()
        {
            if (_v6 == null) return new[] { (byte)(_v4 >> 24), (byte)(_v4 >> 16), (byte)(_v4 >> 8), (byte)_v4 };
            var bytes = new byte[16];
            for (int i = 0; i < 8; i++) { bytes[2 * i] = (byte)(_v6[i] >> 8); bytes[2 * i + 1] = (byte)_v6[i]; }
            return bytes;
        }

        public IPAddress MapToIPv6()
        {
            if (_v6 != null) return (IPAddress)(object)this;
            return (IPAddress)(object)new IPAddressImpl(new ushort[] { 0, 0, 0, 0, 0, 0xFFFF, (ushort)(_v4 >> 16), (ushort)_v4 }, 0);
        }

        public IPAddress MapToIPv4()
        {
            if (_v6 == null) return (IPAddress)(object)this;
            return (IPAddress)(object)new IPAddressImpl((uint)(_v6[6] << 16 | _v6[7]), true);
        }

        public static bool IsLoopback(IPAddress address)
        {
            if (address == null) throw new ArgumentNullException("address");
            var a = (IPAddressImpl)(object)address;
            if (a._v6 == null) return (a._v4 >> 24) == 127;
            if (a.IsIPv4MappedToIPv6) return (a._v6[6] >> 8) == 127;
            for (int i = 0; i < 7; i++) if (a._v6[i] != 0) return false;
            return a._v6[7] == 1;
        }

        public static short HostToNetworkOrder(short host) => (short)((host & 0xFF) << 8 | (host >> 8) & 0xFF);
        public static int HostToNetworkOrder(int host) =>
            (int)(((uint)host << 24) | (((uint)host & 0xFF00) << 8) | (((uint)host >> 8) & 0xFF00) | ((uint)host >> 24));
        public static long HostToNetworkOrder(long host) =>
            (long)((ulong)HostToNetworkOrder((int)host) << 32 | (uint)HostToNetworkOrder((int)(host >> 32)));
        public static short NetworkToHostOrder(short network) => HostToNetworkOrder(network);
        public static int NetworkToHostOrder(int network) => HostToNetworkOrder(network);
        public static long NetworkToHostOrder(long network) => HostToNetworkOrder(network);

        public override bool Equals(object comparand)
        {
            var other = comparand as IPAddressImpl;
            if (other == null || (other._v6 == null) != (_v6 == null)) return false;
            if (_v6 == null) return other._v4 == _v4;
            for (int i = 0; i < 8; i++) if (other._v6[i] != _v6[i]) return false;
            return other._scopeId == _scopeId;
        }

        public override int GetHashCode()
        {
            if (_v6 == null) return (int)_v4;
            int h = (int)_scopeId;
            for (int i = 0; i < 8; i++) h = h * 31 + _v6[i];
            return h;
        }

        // ---- formatting ------------------------------------------------------------------------
        public override string ToString()
        {
            if (_v6 == null) return V4ToString(_v4);
            var sb = new System.Text.StringBuilder();
            ushort[] w = _v6;
            if (ShouldHaveIpv4Embedded(w))
            {
                AppendSections(sb, w, 0, 6);
                if (sb.Length == 0 || sb[sb.Length - 1] != ':') sb.Append(':');
                sb.Append(V4ToString((uint)(w[6] << 16 | w[7])));
            }
            else AppendSections(sb, w, 0, 8);
            if (_scopeId != 0) sb.Append('%').Append((uint)_scopeId);
            return sb.ToString();
        }

        private static string V4ToString(uint v) =>
            (v >> 24).ToString() + "." + ((v >> 16) & 0xFF).ToString() + "." + ((v >> 8) & 0xFF).ToString() + "." + (v & 0xFF).ToString();

        // .NET's IPAddressParser.ShouldHaveIpv4Embedded.
        private static bool ShouldHaveIpv4Embedded(ushort[] n)
        {
            if (n[0] == 0 && n[1] == 0 && n[2] == 0 && n[3] == 0 && n[6] != 0)
            {
                if (n[4] == 0 && (n[5] == 0 || n[5] == 0xFFFF)) return true;
                if (n[4] == 0xFFFF && n[5] == 0) return true;
            }
            return n[4] == 0 && n[5] == 0x5EFE;
        }

        // RFC 5952: the longest run (first one on ties) of two or more zero words becomes "::".
        private static void AppendSections(System.Text.StringBuilder sb, ushort[] w, int start, int end)
        {
            int bestStart = -1, bestLength = 0;
            for (int i = start; i < end;)
            {
                if (w[i] != 0) { i++; continue; }
                int j = i;
                while (j < end && w[j] == 0) j++;
                if (j - i > bestLength && j - i > 1) { bestStart = i; bestLength = j - i; }
                i = j;
            }
            bool needColon = false;
            for (int i = start; i < end; i++)
            {
                if (i == bestStart)
                {
                    sb.Append("::");
                    i += bestLength - 1;
                    needColon = false;
                    continue;
                }
                if (needColon) sb.Append(':');
                sb.Append(w[i].ToString("x"));
                needColon = true;
            }
        }

        // ---- parsing ---------------------------------------------------------------------------
        public static IPAddress Parse(string ipString)
        {
            if (ipString == null) throw new ArgumentNullException("ipString");
            var address = TryParseCore(ipString);
            if (address == null) throw new FormatException("An invalid IP address was specified.");
            return address;
        }

        public static bool TryParse(string ipString, out IPAddress address)
        {
            address = ipString == null ? null : TryParseCore(ipString);
            return address != null;
        }

        private static IPAddress TryParseCore(string s)
        {
            if (s.IndexOf(':') >= 0) return TryParseV6(s);
            uint v;
            return TryParseV4(s, 0, s.Length, out v) ? (IPAddress)(object)new IPAddressImpl(v, true) : null;
        }

        /// <summary>inet_aton forms, as .NET's IPv4AddressHelper.ParseNonCanonical: 1-4 parts, each
        /// decimal, octal (leading 0) or hex (0x); the last part fills the remaining bytes.</summary>
        private static bool TryParseV4(string s, int start, int end, out uint result)
        {
            result = 0;
            if (start >= end) return false;
            var parts = new ulong[4];
            int count = 0, i = start;
            while (true)
            {
                if (count == 4 || i >= end) return false;
                int numberBase = 10;
                if (s[i] == '0')
                {
                    numberBase = 8;
                    if (i + 1 < end && (s[i + 1] == 'x' || s[i + 1] == 'X')) { numberBase = 16; i += 2; }
                }
                int digitsStart = i;
                ulong value = 0;
                while (i < end && s[i] != '.')
                {
                    int d = Digit(s[i]);
                    if (d < 0 || d >= numberBase) return false;
                    value = value * (ulong)numberBase + (ulong)d;
                    if (value > 0xFFFFFFFF) return false;
                    i++;
                }
                if (i == digitsStart && numberBase != 8) return false;  // "0x" alone, or an empty part
                parts[count++] = value;
                if (i >= end) break;
                i++;  // '.'
                if (i >= end) return false;  // trailing dot
            }
            ulong total;
            switch (count)
            {
                case 1: total = parts[0]; break;
                case 2: if (parts[0] > 0xFF || parts[1] > 0xFFFFFF) return false; total = parts[0] << 24 | parts[1]; break;
                case 3:
                    if (parts[0] > 0xFF || parts[1] > 0xFF || parts[2] > 0xFFFF) return false;
                    total = parts[0] << 24 | parts[1] << 16 | parts[2]; break;
                default:
                    for (int k = 0; k < 4; k++) if (parts[k] > 0xFF) return false;
                    total = parts[0] << 24 | parts[1] << 16 | parts[2] << 8 | parts[3]; break;
            }
            result = (uint)total;
            return true;
        }

        private static int Digit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static IPAddress TryParseV6(string s)
        {
            int start = 0, end = s.Length;
            if (end > 0 && s[0] == '[')
            {
                int close = s.IndexOf(']');
                if (close < 0) return null;
                // "[addr]" or "[addr]:port" (the port is ignored, as .NET does).
                if (close + 1 < end && s[close + 1] != ':') return null;
                start = 1; end = close;
            }
            long scopeId = 0;
            int percent = s.IndexOf('%', start);
            if (percent >= 0 && percent < end)
            {
                if (percent + 1 >= end) return null;
                long scope = 0;
                for (int i = percent + 1; i < end; i++)
                {
                    if (s[i] < '0' || s[i] > '9') return null;  // interface names need if_nametoindex
                    scope = scope * 10 + (s[i] - '0');
                    if (scope > 0xFFFFFFFF) return null;
                }
                scopeId = scope;
                end = percent;
            }
            var words = new ushort[8];
            int count = 0, compressAt = -1, p = start;
            if (p + 1 < end && s[p] == ':' && s[p + 1] == ':') { compressAt = 0; p += 2; }
            else if (p < end && s[p] == ':') return null;
            while (p < end)
            {
                if (count == 8) return null;
                // An embedded IPv4 address ends the address.
                int dot = -1;
                for (int q = p; q < end && s[q] != ':'; q++) if (s[q] == '.') { dot = q; break; }
                if (dot >= 0)
                {
                    if (count > 6) return null;
                    uint v4;
                    if (!TryParseStrictV4(s, p, end, out v4)) return null;
                    words[count++] = (ushort)(v4 >> 16);
                    words[count++] = (ushort)v4;
                    p = end;
                    break;
                }
                int value = 0, digits = 0;
                while (p < end && s[p] != ':')
                {
                    int d = Digit(s[p]);
                    if (d < 0 || ++digits > 4) return null;
                    value = value * 16 + d;
                    p++;
                }
                if (digits == 0) return null;
                words[count++] = (ushort)value;
                if (p < end)
                {
                    p++;  // ':'
                    if (p < end && s[p] == ':')
                    {
                        if (compressAt >= 0) return null;
                        compressAt = count;
                        p++;
                    }
                    else if (p >= end) return null;  // trailing single ':'
                }
            }
            if (compressAt >= 0)
            {
                if (count == 8) return null;
                int move = count - compressAt;
                for (int k = 0; k < move; k++) words[7 - k] = words[count - 1 - k];
                for (int k = compressAt; k < 8 - move; k++) words[k] = 0;
            }
            else if (count != 8) return null;
            return (IPAddress)(object)new IPAddressImpl(words, scopeId);
        }

        private static bool TryParseStrictV4(string s, int start, int end, out uint result)
        {
            result = 0;
            int parts = 0, i = start;
            while (i < end)
            {
                int value = 0, digits = 0;
                while (i < end && s[i] != '.')
                {
                    if (s[i] < '0' || s[i] > '9' || ++digits > 3) return false;
                    value = value * 10 + (s[i] - '0');
                    i++;
                }
                if (digits == 0 || value > 255) return false;
                result = result << 8 | (uint)value;
                parts++;
                if (i < end) { i++; if (i >= end) return false; }
            }
            return parts == 4;
        }
    }

    [Shadows("System.Net.IPEndPoint")]
    public class IPEndPointImpl : EndPoint
    {
        public const int MinPort = 0, MaxPort = 0xFFFF;
        private IPAddress _address;
        private int _port;

        public IPEndPointImpl(long address, int port) : this(new IPAddress(address), port) { }

        public IPEndPointImpl(IPAddress address, int port)
        {
            if (address == null) throw new ArgumentNullException("address");
            if (port < MinPort || port > MaxPort) throw new ArgumentOutOfRangeException("port");
            _address = address;
            _port = port;
        }

        public IPAddress Address
        {
            get => _address;
            set => _address = value ?? throw new ArgumentNullException("value");
        }

        public int Port
        {
            get => _port;
            set
            {
                if (value < MinPort || value > MaxPort) throw new ArgumentOutOfRangeException("value");
                _port = value;
            }
        }

        public override AddressFamily AddressFamily => _address.AddressFamily;

        public override string ToString() =>
            _address.AddressFamily == AddressFamily.InterNetworkV6
                ? "[" + _address.ToString() + "]:" + _port.ToString()
                : _address.ToString() + ":" + _port.ToString();

        public override bool Equals(object comparand) =>
            comparand is IPEndPoint other && other.Address.Equals(_address) && other.Port == _port;

        public override int GetHashCode() => _address.GetHashCode() ^ _port;

        public static IPEndPoint Parse(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            IPEndPoint result;
            if (TryParse(s, out result)) return result;
            throw new FormatException("An invalid IPEndPoint was specified.");
        }

        public static bool TryParse(string s, out IPEndPoint result)
        {
            result = null;
            if (s == null) return false;
            int addressLength = s.Length;
            int lastColon = s.LastIndexOf(':');
            if (lastColon > 0)
            {
                // "[v6]:port", "v4:port"; a bare IPv6 address has its last colon inside the address.
                if (s[lastColon - 1] == ']') addressLength = lastColon;
                else if (s.Substring(0, lastColon).LastIndexOf(':') < 0) addressLength = lastColon;
            }
            IPAddress address;
            if (!IPAddress.TryParse(s.Substring(0, addressLength), out address)) return false;
            int port = 0;
            if (addressLength != s.Length)
            {
                if (addressLength + 1 >= s.Length) return false;
                for (int i = addressLength + 1; i < s.Length; i++)
                {
                    if (s[i] < '0' || s[i] > '9') return false;
                    port = port * 10 + (s[i] - '0');
                    if (port > MaxPort) return false;
                }
            }
            result = new IPEndPoint(address, port);
            return true;
        }
    }
}
