// System.Net.IPNetwork (an address prefix, "10.0.0.0/8") and System.Net.Security.SslClientHelloInfo,
// after dotnet/runtime's IPNetwork.cs (release/11.0, MIT), over the address bytes instead of UInt128.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace CsharpcCoreLib
{
    [Shadows("System.Net.IPNetwork")]
    public readonly struct IPNetworkImpl : IEquatable<IPNetworkImpl>
    {
        private readonly IPAddress _baseAddress;
        public IPAddress BaseAddress => _baseAddress ?? IPAddress.Any;
        public int PrefixLength { get; }

        public IPNetworkImpl(IPAddress baseAddress, int prefixLength)
        {
            ArgumentNullException.ThrowIfNull(baseAddress);
            if (prefixLength < 0 || prefixLength > MaxPrefixLength(baseAddress)) throw new ArgumentOutOfRangeException("prefixLength");
            _baseAddress = ClearHostBits(baseAddress, prefixLength);
            PrefixLength = prefixLength;
        }

        private static int MaxPrefixLength(IPAddress address) => address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

        private static bool Matches(byte[] a, byte[] b, int prefixLength)
        {
            for (int bit = 0; bit < prefixLength; bit += 8)
            {
                int bits = Math.Min(8, prefixLength - bit);
                int mask = (0xFF << (8 - bits)) & 0xFF;
                if ((a[bit / 8] & mask) != (b[bit / 8] & mask)) return false;
            }
            return true;
        }

        private static IPAddress ClearHostBits(IPAddress address, int prefixLength)
        {
            if (prefixLength == 0) return address.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any;
            byte[] bytes = address.GetAddressBytes();
            bool changed = false;
            for (int bit = 0; bit < bytes.Length * 8; bit += 8)
            {
                int keep = Math.Clamp(prefixLength - bit, 0, 8);
                byte masked = (byte)(bytes[bit / 8] & ((0xFF << (8 - keep)) & 0xFF));
                if (masked != bytes[bit / 8]) { bytes[bit / 8] = masked; changed = true; }
            }
            if (!changed) return address;
            return address.AddressFamily == AddressFamily.InterNetwork ? new IPAddress(bytes) : new IPAddress(bytes, 0);
        }

        public bool Contains(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            IPAddress baseAddress = BaseAddress;
            if (address.AddressFamily != baseAddress.AddressFamily)
            {
                if (baseAddress.AddressFamily != AddressFamily.InterNetwork || !address.IsIPv4MappedToIPv6) return false;
                address = address.MapToIPv4();
            }
            if (PrefixLength == 0) return true;
            return Matches(baseAddress.GetAddressBytes(), address.GetAddressBytes(), PrefixLength);
        }

        public static IPNetworkImpl Parse(string s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return Parse(s.AsSpan());
        }

        public static IPNetworkImpl Parse(ReadOnlySpan<char> s)
        {
            if (!TryParse(s, out IPNetworkImpl result)) throw new FormatException("An invalid IP network was specified.");
            return result;
        }

        public static bool TryParse(string s, out IPNetworkImpl result)
        {
            if (s == null) { result = default; return false; }
            return TryParse(s.AsSpan(), out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, out IPNetworkImpl result)
        {
            int separator = s.LastIndexOf('/');
            if (separator >= 0
                && IPAddress.TryParse(new string(s.Slice(0, separator)), out IPAddress address)
                && int.TryParse(s.Slice(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int prefixLength)
                && prefixLength <= MaxPrefixLength(address))
            {
                result = new IPNetworkImpl(address, prefixLength);
                return true;
            }
            result = default;
            return false;
        }

        public override string ToString() => BaseAddress.ToString() + "/" + ((uint)PrefixLength).ToString(CultureInfo.InvariantCulture);

        public bool Equals(IPNetworkImpl other) => PrefixLength == other.PrefixLength && BaseAddress.Equals(other.BaseAddress);
        public override bool Equals(object obj) => obj is IPNetworkImpl other && Equals(other);
        public static bool operator ==(IPNetworkImpl left, IPNetworkImpl right) => left.Equals(right);
        public static bool operator !=(IPNetworkImpl left, IPNetworkImpl right) => !left.Equals(right);
        public override int GetHashCode() => HashCode.Combine(BaseAddress, PrefixLength);
    }

    [Shadows("System.Net.Security.SslClientHelloInfo")]
    public readonly struct SslClientHelloInfoImpl
    {
        public string ServerName { get; }
        public SslProtocols SslProtocols { get; }

        public SslClientHelloInfoImpl(string serverName, SslProtocols sslProtocols)
        {
            ServerName = serverName;
            SslProtocols = sslProtocols;
        }
    }
}
