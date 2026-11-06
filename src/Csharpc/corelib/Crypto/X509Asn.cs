// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Security.Cryptography/src/
// System/Security/Cryptography/{Oid,OidCollection,AsnEncodedData}.cs and X509Certificates/{X500DistinguishedName,
// X500NameEncoder,PublicKey}.cs; see THIRD-PARTY-NOTICES.md.
// The ASN.1 side of X.509 on Linux: a small DER reader, OIDs with the friendly names .NET reports, AsnEncodedData,
// X500DistinguishedName (decoded as .NET prints it: ", " between RDNs, the last RDN first, quotes where needed)
// and PublicKey. The certificate itself is in X509Certificates.cs (docs/adr/0022-tls.md).
#nullable disable
#if CSHARPC_TARGET_LINUX
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CsharpcCoreLib
{
    /// <summary>Just enough DER to read certificates: tag-length-value walking over a byte array.</summary>
    internal static class Der
    {
        internal static CryptographicException Corrupt() => new CryptographicException("ASN1 corrupted data.");

        /// <summary>Reads the element at <paramref name="pos"/> (not past <paramref name="limit"/>); <paramref name="next"/> is the position after it.</summary>
        internal static void Read(byte[] data, int pos, int limit, out int tag, out int start, out int length, out int next)
        {
            if (pos + 2 > limit) throw Corrupt();
            tag = data[pos];
            if ((tag & 0x1F) == 0x1F) throw Corrupt(); // high tag numbers: not in the structures read here
            int b = data[pos + 1];
            int p = pos + 2;
            if (b < 0x80) length = b;
            else
            {
                int n = b & 0x7F;
                if (n == 0 || n > 4 || p + n > limit) throw Corrupt();
                long l = 0;
                for (int i = 0; i < n; i++) l = (l << 8) | data[p + i];
                if (l > int.MaxValue) throw Corrupt();
                length = (int)l;
                p += n;
            }
            start = p;
            if (length < 0 || start + length > limit) throw Corrupt();
            next = start + length;
        }

        internal static byte[] Slice(byte[] data, int start, int length)
        {
            byte[] r = new byte[length];
            Buffer.BlockCopy(data, start, r, 0, length);
            return r;
        }

        /// <summary>The whole encoding (tag, length and content) of the element at <paramref name="pos"/>.</summary>
        internal static byte[] Element(byte[] data, int pos, int limit, out int next)
        {
            Read(data, pos, limit, out _, out _, out _, out next);
            return Slice(data, pos, next - pos);
        }

        internal static string ReadOid(byte[] data, int start, int length)
        {
            if (length == 0) throw Corrupt();
            var sb = new StringBuilder();
            int first = data[start];
            if (first < 40) sb.Append('0').Append('.').Append(first);
            else if (first < 80) sb.Append('1').Append('.').Append(first - 40);
            else sb.Append('2').Append('.').Append(first - 80);
            long value = 0;
            for (int i = 1; i < length; i++)
            {
                byte b = data[start + i];
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) { sb.Append('.').Append(value); value = 0; }
            }
            return sb.ToString();
        }

        internal static byte[] EncodeOid(string oid)
        {
            string[] parts = oid.Split('.');
            var bytes = new List<byte>();
            bytes.Add((byte)(int.Parse(parts[0]) * 40 + int.Parse(parts[1])));
            for (int i = 2; i < parts.Length; i++)
            {
                long v = long.Parse(parts[i]);
                var chunk = new List<byte>();
                chunk.Add((byte)(v & 0x7F));
                v >>= 7;
                while (v > 0) { chunk.Add((byte)((v & 0x7F) | 0x80)); v >>= 7; }
                for (int k = chunk.Count - 1; k >= 0; k--) bytes.Add(chunk[k]);
            }
            return bytes.ToArray();
        }

        internal static DateTime ReadTime(byte[] data, int tag, int start, int length)
        {
            string s = Encoding.ASCII.GetString(data, start, length);
            int year, i;
            if (tag == 0x17) // UTCTime YYMMDDhhmm[ss]Z: 1950-2049
            {
                year = int.Parse(s.Substring(0, 2));
                year += year >= 50 ? 1900 : 2000;
                i = 2;
            }
            else if (tag == 0x18) // GeneralizedTime YYYYMMDDhhmm[ss]Z
            {
                year = int.Parse(s.Substring(0, 4));
                i = 4;
            }
            else throw Corrupt();
            int month = int.Parse(s.Substring(i, 2)), day = int.Parse(s.Substring(i + 2, 2));
            int hour = int.Parse(s.Substring(i + 4, 2)), minute = int.Parse(s.Substring(i + 6, 2));
            int second = 0;
            if (s.Length > i + 8 && s[i + 8] != 'Z') second = int.Parse(s.Substring(i + 8, 2));
            return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
        }
    }

    internal static class OidTable
    {
        // Value, friendly name (as OidLookup reports them on Linux).
        private static readonly string[] s_table =
        {
            "1.2.840.113549.1.1.1", "RSA",
            "1.2.840.113549.1.1.5", "sha1RSA",
            "1.2.840.113549.1.1.11", "sha256RSA",
            "1.2.840.113549.1.1.12", "sha384RSA",
            "1.2.840.113549.1.1.13", "sha512RSA",
            "1.2.840.113549.1.1.10", "RSASSA-PSS",
            "1.2.840.10045.2.1", "ECC",
            "1.2.840.10045.4.3.2", "sha256ECDSA",
            "1.2.840.10045.4.3.3", "sha384ECDSA",
            "1.2.840.10045.4.3.4", "sha512ECDSA",
            "1.2.840.10040.4.1", "DSA",
            "1.3.101.112", "Ed25519",
            "2.5.4.3", "CN", "2.5.4.4", "SN", "2.5.4.5", "SERIALNUMBER", "2.5.4.6", "C", "2.5.4.7", "L", "2.5.4.8", "S",
            "2.5.4.9", "STREET", "2.5.4.10", "O", "2.5.4.11", "OU", "2.5.4.12", "T", "2.5.4.17", "PostalCode", "2.5.4.42", "G",
            "1.2.840.113549.1.9.1", "E", "0.9.2342.19200300.100.1.25", "DC",
            "1.3.6.1.5.5.7.3.1", "Server Authentication",
            "1.3.6.1.5.5.7.3.2", "Client Authentication",
            "1.3.6.1.5.5.7.3.3", "Code Signing",
            "1.3.6.1.5.5.7.3.4", "Secure Email",
            "2.5.29.14", "Subject Key Identifier",
            "2.5.29.15", "Key Usage",
            "2.5.29.17", "Subject Alternative Name",
            "2.5.29.19", "Basic Constraints",
            "2.5.29.35", "Authority Key Identifier",
            "2.5.29.37", "Enhanced Key Usage",
        };

        internal static string FriendlyName(string value)
        {
            for (int i = 0; i < s_table.Length; i += 2)
                if (s_table[i] == value) return s_table[i + 1];
            return null;
        }

        internal static string Value(string friendlyName)
        {
            for (int i = 0; i < s_table.Length; i += 2)
                if (string.Equals(s_table[i + 1], friendlyName, StringComparison.OrdinalIgnoreCase)) return s_table[i];
            return null;
        }
    }

    /// <summary>Decodes X500DistinguishedName's DER the way X509Pal does on Unix (X500NameEncoder.X500DistinguishedNameDecode).</summary>
    internal static class X500Decoder
    {
        private const string NeedsQuotingChars = ",+=\"\n<>#;";

        private static bool IsQuotableWhitespace(char c) => c == ' ' || (c >= 0x09 && c <= 0x0D);

        private static bool NeedsQuoting(string value)
        {
            if (value.Length == 0 || IsQuotableWhitespace(value[0]) || IsQuotableWhitespace(value[value.Length - 1])) return true;
            return value.IndexOfAny(NeedsQuotingChars.ToCharArray()) >= 0;
        }

        internal static string Decode(byte[] name, bool printOid, X500DistinguishedNameFlags flags)
        {
            bool reverse = (flags & X500DistinguishedNameFlags.Reversed) != 0;
            bool quoteIfNeeded = (flags & X500DistinguishedNameFlags.DoNotUseQuotes) == 0;
            bool useMultiSeparator = (flags & X500DistinguishedNameFlags.DoNotUsePlusSign) == 0;
            string dnSeparator;
            if ((flags & X500DistinguishedNameFlags.UseSemicolons) != 0) dnSeparator = "; ";
            else if ((flags & (X500DistinguishedNameFlags.UseNewLines | X500DistinguishedNameFlags.UseCommas)) == X500DistinguishedNameFlags.UseNewLines) dnSeparator = Environment.NewLine;
            else dnSeparator = ", ";
            string multi = useMultiSeparator ? " + " : " ";
            try
            {
                return DecodeCore(name, printOid, reverse, quoteIfNeeded, dnSeparator, multi);
            }
            catch (CryptographicException)
            {
                return ""; // Windows compat, as in .NET
            }
        }

        private static string DecodeCore(byte[] name, bool printOid, bool reverse, bool quoteIfNeeded, string dnSeparator, string multi)
        {
            Der.Read(name, 0, name.Length, out int tag, out int start, out int length, out int next);
            if (tag != 0x30 || next != name.Length) throw Der.Corrupt();
            var rdns = new List<(int Start, int End)>();
            for (int pos = start; pos < next;)
            {
                Der.Read(name, pos, next, out int rtag, out int rstart, out int rlen, out int rnext);
                if (rtag != 0x31) throw Der.Corrupt();
                rdns.Add((rstart, rnext));
                pos = rnext;
            }
            var sb = new StringBuilder();
            bool firstRdn = true;
            for (int k = 0; k < rdns.Count; k++)
            {
                var (rs, re) = rdns[reverse ? rdns.Count - 1 - k : k];
                if (!firstRdn) sb.Append(dnSeparator);
                firstRdn = false;
                bool firstAttribute = true;
                for (int pos = rs; pos < re;)
                {
                    Der.Read(name, pos, re, out int atag, out int astart, out int alen, out int anext);
                    if (atag != 0x30) throw Der.Corrupt();
                    Der.Read(name, astart, anext, out int otag, out int ostart, out int olen, out int onext);
                    if (otag != 0x06) throw Der.Corrupt();
                    string oid = Der.ReadOid(name, ostart, olen);
                    Der.Read(name, onext, anext, out int vtag, out int vstart, out int vlen, out int vnext);
                    string value = DecodeString(name, vtag, vstart, vlen, onext, vnext);
                    if (!firstAttribute) sb.Append(multi);
                    firstAttribute = false;
                    if (printOid)
                    {
                        string friendly = OidTable.FriendlyName(oid);
                        if (friendly == null || friendly == oid) sb.Append("OID.").Append(oid); else sb.Append(friendly);
                        sb.Append('=');
                    }
                    if (quoteIfNeeded && NeedsQuoting(value)) sb.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
                    else sb.Append(value);
                    pos = anext;
                }
            }
            return sb.ToString();
        }

        private static string DecodeString(byte[] d, int tag, int start, int length, int elementStart, int elementEnd)
        {
            switch (tag)
            {
                case 0x0C: return new UTF8Encoding(false, true).GetString(d, start, length);
                case 0x13: case 0x16: case 0x14: case 0x1A: return Encoding.Latin1.GetString(d, start, length); // Printable, IA5, T61 (Latin-1 as .NET), Visible
                case 0x1E: return Encoding.BigEndianUnicode.GetString(d, start, length);
                case 0x1C: return new UTF32Encoding(true, false).GetString(d, start, length);
                default:
                    // Other types print as '#' and the hex of the whole encoding (RFC 2253).
                    return "#" + Convert.ToHexString(d, elementStart, elementEnd - elementStart);
            }
        }
    }

    [Shadows("System.Security.Cryptography.AsnEncodedData")]
    public class AsnEncodedDataImpl
    {
        private Oid _oid;
        private byte[] _rawData;

        protected AsnEncodedDataImpl() { }

        public AsnEncodedDataImpl(byte[] rawData)
        {
            Reset(null, rawData);
        }

        public AsnEncodedDataImpl(string oid, byte[] rawData)
        {
            Reset(oid == null ? null : new Oid(oid), rawData);
        }

        public AsnEncodedDataImpl(Oid oid, byte[] rawData)
        {
            Reset(oid, rawData);
        }

        public AsnEncodedDataImpl(AsnEncodedData asnEncodedData)
        {
            if (asnEncodedData == null) throw new ArgumentNullException("asnEncodedData");
            Reset(asnEncodedData.Oid, asnEncodedData.RawData);
        }

        public AsnEncodedDataImpl(ReadOnlySpan<byte> rawData)
        {
            Reset(null, rawData.ToArray());
        }

        public AsnEncodedDataImpl(Oid oid, ReadOnlySpan<byte> rawData)
        {
            Reset(oid, rawData.ToArray());
        }

        private void Reset(Oid oid, byte[] rawData)
        {
            if (rawData == null) throw new ArgumentNullException("rawData");
            _oid = oid == null ? null : new Oid(oid);
            _rawData = (byte[])rawData.Clone();
        }

        public Oid Oid
        {
            get => _oid;
            set => _oid = value == null ? null : new Oid(value);
        }

        public byte[] RawData
        {
            get => _rawData;
            set
            {
                if (value == null) throw new ArgumentNullException("value");
                _rawData = (byte[])value.Clone();
            }
        }

        public virtual void CopyFrom(AsnEncodedData asnEncodedData)
        {
            if (asnEncodedData == null) throw new ArgumentNullException("asnEncodedData");
            Reset(asnEncodedData.Oid, asnEncodedData.RawData);
        }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X500DistinguishedName")]
    public sealed class X500DistinguishedNameImpl : AsnEncodedDataImpl
    {
        private string _name;

        public X500DistinguishedNameImpl(byte[] encodedDistinguishedName) : base(encodedDistinguishedName) { }

        public X500DistinguishedNameImpl(AsnEncodedData encodedDistinguishedName) : base(encodedDistinguishedName) { }

        public X500DistinguishedNameImpl(X500DistinguishedName distinguishedName) : base(distinguishedName.RawData) { }

        public string Name => _name ??= Decode(X500DistinguishedNameFlags.Reversed);

        public string Decode(X500DistinguishedNameFlags flag)
        {
            if (((uint)flag & ~0x71F1u) != 0) throw new ArgumentException("Enumeration value '" + (int)flag + "' is not valid.", "flag");
            return X500Decoder.Decode(RawData, true, flag);
        }

        public override string ToString() => Name;
    }

    [Shadows("System.Security.Cryptography.X509Certificates.PublicKey")]
    public sealed class PublicKeyImpl
    {
        private readonly Oid _oid;
        private readonly AsnEncodedData _keyValue;
        private readonly AsnEncodedData _parameters;

        public PublicKeyImpl(Oid oid, AsnEncodedData parameters, AsnEncodedData keyValue)
        {
            _oid = new Oid(oid);
            _parameters = new AsnEncodedData(parameters);
            _keyValue = new AsnEncodedData(keyValue);
        }

        public Oid Oid => _oid;
        public AsnEncodedData EncodedKeyValue => _keyValue;
        public AsnEncodedData EncodedParameters => _parameters;

        public System.Security.Cryptography.RSA GetRSAPublicKey()
        {
            if (_oid.Value != "1.2.840.113549.1.1.1") return null;
            return X509Keys.RsaPublic(_keyValue.RawData);
        }
    }

    internal static class X509Keys
    {
        /// <summary>An RSA with the public key of an RSAPublicKey (PKCS#1) DER, as the SubjectPublicKeyInfo's bit string holds it.</summary>
        internal static RSA RsaPublic(byte[] pkcs1)
        {
            Der.Read(pkcs1, 0, pkcs1.Length, out int tag, out int start, out int length, out int end);
            if (tag != 0x30) throw Der.Corrupt();
            Der.Read(pkcs1, start, end, out int t1, out int s1, out int l1, out int n1);
            Der.Read(pkcs1, n1, end, out int t2, out int s2, out int l2, out int n2);
            if (t1 != 0x02 || t2 != 0x02) throw Der.Corrupt();
            var parameters = new RSAParameters { Modulus = StripZeros(pkcs1, s1, l1), Exponent = StripZeros(pkcs1, s2, l2) };
            RSA rsa = RSA.Create();
            try { rsa.ImportParameters(parameters); }
            catch { rsa.Dispose(); throw; }
            return rsa;
        }

        private static byte[] StripZeros(byte[] data, int start, int length)
        {
            while (length > 1 && data[start] == 0) { start++; length--; }
            return Der.Slice(data, start, length);
        }
    }
}
#endif
