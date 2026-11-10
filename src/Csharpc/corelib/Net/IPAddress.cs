// System.Net.EndPoint / IPEndPoint (IPAddress is the framework's own IL: System.Net.Primitives is a mixed assembly, ADR 0013).
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
