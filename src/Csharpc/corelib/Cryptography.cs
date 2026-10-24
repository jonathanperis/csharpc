// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Security.Cryptography/src/
// System/Security/Cryptography/HashAlgorithmName.cs and RSAParameters.cs; see THIRD-PARTY-NOTICES.md.
// The value types of System.Security.Cryptography that code passes around without hashing or signing
// anything (Roslyn's checksums and strong-name keys); the algorithms themselves are not implemented.
#nullable disable
using System;
using System.Security.Cryptography;

namespace CsharpcCoreLib
{
    [Shadows("System.Security.Cryptography.HashAlgorithmName")]
    public readonly struct HashAlgorithmNameImpl : IEquatable<HashAlgorithmName>
    {
        public static HashAlgorithmName MD5 => new HashAlgorithmName("MD5");
        public static HashAlgorithmName SHA1 => new HashAlgorithmName("SHA1");
        public static HashAlgorithmName SHA256 => new HashAlgorithmName("SHA256");
        public static HashAlgorithmName SHA384 => new HashAlgorithmName("SHA384");
        public static HashAlgorithmName SHA512 => new HashAlgorithmName("SHA512");
        public static HashAlgorithmName SHA3_256 => new HashAlgorithmName("SHA3-256");
        public static HashAlgorithmName SHA3_384 => new HashAlgorithmName("SHA3-384");
        public static HashAlgorithmName SHA3_512 => new HashAlgorithmName("SHA3-512");

        private readonly string _name;

        public HashAlgorithmNameImpl(string name)
        {
            // Note: No validation because we have to deal with default(HashAlgorithmName) regardless.
            _name = name;
        }

        public string Name => _name;

        public override string ToString() => _name ?? string.Empty;

        public override bool Equals(object obj) => obj is HashAlgorithmName other && Equals(other);

        public bool Equals(HashAlgorithmName other) =>
            // NOTE: intentionally ordinal and case sensitive, matches CNG.
            _name == other.Name;

        public override int GetHashCode() => _name == null ? 0 : _name.GetHashCode();

        public static bool operator ==(HashAlgorithmNameImpl left, HashAlgorithmNameImpl right) => left._name == right._name;

        public static bool operator !=(HashAlgorithmNameImpl left, HashAlgorithmNameImpl right) => !(left == right);

        public static bool TryFromOid(string oidValue, out HashAlgorithmName value)
        {
            if (oidValue == null) throw new ArgumentNullException("oidValue");
            switch (oidValue)
            {
                case "1.2.840.113549.2.5": value = MD5; return true;
                case "1.3.14.3.2.26": value = SHA1; return true;
                case "2.16.840.1.101.3.4.2.1": value = SHA256; return true;
                case "2.16.840.1.101.3.4.2.2": value = SHA384; return true;
                case "2.16.840.1.101.3.4.2.3": value = SHA512; return true;
                case "2.16.840.1.101.3.4.2.8": value = SHA3_256; return true;
                case "2.16.840.1.101.3.4.2.9": value = SHA3_384; return true;
                case "2.16.840.1.101.3.4.2.10": value = SHA3_512; return true;
                default: value = default; return false;
            }
        }

        public static HashAlgorithmName FromOid(string oidValue)
        {
            if (TryFromOid(oidValue, out HashAlgorithmName value)) return value;
            throw new CryptographicException("The specified OID (" + oidValue + ") does not represent a known hash algorithm.");
        }
    }

    [Shadows("System.Security.Cryptography.RSAParameters")]
    public struct RSAParametersImpl
    {
        public byte[] Exponent;
        public byte[] Modulus;
        public byte[] P;
        public byte[] Q;
        public byte[] DP;
        public byte[] DQ;
        public byte[] InverseQ;
        public byte[] D;
    }
}
