// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Security.Cryptography/src/
// System/Security/Cryptography/AsymmetricAlgorithm.cs, RSA.cs, RSASignaturePadding.cs, RSAEncryptionPadding.cs,
// AsymmetricSignature(De)Formatter.cs, RSAPKCS1Signature(De)Formatter.cs and Common/.../RSAOpenSsl.cs;
// see THIRD-PARTY-NOTICES.md.
// RSA's abstract surface for WASI (argument checks in .NET's order); WASI has no implementation yet, so
// RSA.Create() isn't there (a compile-time error). Linux, macOS and Windows compile .NET's own RSA from
// the framework's IL (Linux over the OpenSSL contract, macOS over the Apple crypto shim, Windows over CNG).
// Linux compiles System.Security.Cryptography and System.Net.Security from the framework's IL over the OpenSSL and GSS contracts
// (Crypto/CryptoNative*.cs, Net/NetSecurityNative.cs; docs/il-first/System.Net.Security.md); the other targets keep this copy (#449).
#if CSHARPC_TARGET_WINDOWS || CSHARPC_TARGET_MACOS || CSHARPC_TARGET_WASI
#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CsharpcCoreLib
{
    [Shadows("System.Security.Cryptography.AsymmetricAlgorithm")]
    public abstract class AsymmetricAlgorithmImpl : IDisposable
    {
        protected int KeySizeValue;
        protected KeySizes[] LegalKeySizesValue = null!;

        protected AsymmetricAlgorithmImpl() { }

        public virtual int KeySize
        {
            get => KeySizeValue;
            set
            {
                if (!value.IsLegalSize(LegalKeySizes))
                    throw new CryptographicException("Specified key is not a valid size for this algorithm.");
                KeySizeValue = value;
            }
        }

        public virtual KeySizes[] LegalKeySizes => (KeySizes[])LegalKeySizesValue!.Clone();

        public virtual string SignatureAlgorithm => throw new NotImplementedException();

        public virtual string KeyExchangeAlgorithm => throw new NotImplementedException();

        public virtual void FromXmlString(string xmlString) => throw new NotImplementedException();

        public virtual string ToXmlString(bool includePrivateParameters) => throw new NotImplementedException();

        public void Clear()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose() => Clear();

        protected virtual void Dispose(bool disposing) { }
    }

    [Shadows("System.Security.Cryptography.RSA")]
    public abstract class RSAImpl : AsymmetricAlgorithm
    {

        public int GetMaxOutputSize()
        {
            if (KeySize <= 0) throw new CryptographicException("Specified key is not a valid size for this algorithm.");
            return (KeySize + 7) >>> 3;
        }

        public abstract RSAParameters ExportParameters(bool includePrivateParameters);
        public abstract void ImportParameters(RSAParameters parameters);
        public virtual byte[] Encrypt(byte[] data, RSAEncryptionPadding padding) => throw DerivedClassMustOverride();
        public virtual byte[] Decrypt(byte[] data, RSAEncryptionPadding padding) => throw DerivedClassMustOverride();
        public virtual byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) => throw DerivedClassMustOverride();
        public virtual bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) => throw DerivedClassMustOverride();

        protected virtual byte[] HashData(byte[] data, int offset, int count, HashAlgorithmName hashAlgorithm) =>
            RsaDigest.Hash(hashAlgorithm, new ReadOnlySpan<byte>(data, offset, count));

        protected virtual byte[] HashData(Stream data, HashAlgorithmName hashAlgorithm)
        {
            var buffer = new MemoryStream();
            data.CopyTo(buffer);
            return RsaDigest.Hash(hashAlgorithm, new ReadOnlySpan<byte>(buffer.GetBuffer(), 0, (int)buffer.Length));
        }

        private static Exception DerivedClassMustOverride() => new NotImplementedException("Derived classes must provide an implementation.");

        public virtual byte[] DecryptValue(byte[] rgb) => throw new NotSupportedException("Method not supported.");

        public virtual byte[] EncryptValue(byte[] rgb) => throw new NotSupportedException("Method not supported.");

        public byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            ArgumentNullException.ThrowIfNull(data);
            return SignData(data, 0, data.Length, hashAlgorithm, padding);
        }

        public virtual byte[] SignData(byte[] data, int offset, int count, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, data.Length);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, data.Length - offset);
            ArgumentException.ThrowIfNullOrEmpty(hashAlgorithm.Name, nameof(hashAlgorithm));
            ArgumentNullException.ThrowIfNull(padding);

            byte[] hash = HashData(data, offset, count, hashAlgorithm);
            return SignHash(hash, hashAlgorithm, padding);
        }

        public virtual byte[] SignData(Stream data, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentException.ThrowIfNullOrEmpty(hashAlgorithm.Name, nameof(hashAlgorithm));
            ArgumentNullException.ThrowIfNull(padding);

            byte[] hash = HashData(data, hashAlgorithm);
            return SignHash(hash, hashAlgorithm, padding);
        }

        public bool VerifyData(byte[] data, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            ArgumentNullException.ThrowIfNull(data);
            return VerifyData(data, 0, data.Length, signature, hashAlgorithm, padding);
        }

        public virtual bool VerifyData(byte[] data, int offset, int count, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, data.Length);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, data.Length - offset);
            ArgumentNullException.ThrowIfNull(signature);
            ArgumentException.ThrowIfNullOrEmpty(hashAlgorithm.Name, nameof(hashAlgorithm));
            ArgumentNullException.ThrowIfNull(padding);

            byte[] hash = HashData(data, offset, count, hashAlgorithm);
            return VerifyHash(hash, signature, hashAlgorithm, padding);
        }

        public bool VerifyData(Stream data, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(signature);
            ArgumentException.ThrowIfNullOrEmpty(hashAlgorithm.Name, nameof(hashAlgorithm));
            ArgumentNullException.ThrowIfNull(padding);

            byte[] hash = HashData(data, hashAlgorithm);
            return VerifyHash(hash, signature, hashAlgorithm, padding);
        }

        public override string KeyExchangeAlgorithm => "RSA";
        public override string SignatureAlgorithm => "RSA";
    }

    [Shadows("System.Security.Cryptography.RSASignaturePadding")]
    public sealed class RSASignaturePaddingImpl : IEquatable<RSASignaturePadding>
    {
        private static readonly RSASignaturePadding s_pkcs1 = (RSASignaturePadding)(object)new RSASignaturePaddingImpl(RSASignaturePaddingMode.Pkcs1);
        private static readonly RSASignaturePadding s_pss = (RSASignaturePadding)(object)new RSASignaturePaddingImpl(RSASignaturePaddingMode.Pss);

        private readonly RSASignaturePaddingMode _mode;

        private RSASignaturePaddingImpl(RSASignaturePaddingMode mode) => _mode = mode;

        public static RSASignaturePadding Pkcs1 => s_pkcs1;
        public static RSASignaturePadding Pss => s_pss;

        public RSASignaturePaddingMode Mode => _mode;

        public override int GetHashCode() => _mode.GetHashCode();

        public override bool Equals(object obj) => Equals(obj as RSASignaturePadding);

        public bool Equals(RSASignaturePadding other) => other is not null && _mode == other.Mode;

        public static bool operator ==(RSASignaturePaddingImpl left, RSASignaturePaddingImpl right) =>
            left is null ? right is null : left.Equals((RSASignaturePadding)(object)right);

        public static bool operator !=(RSASignaturePaddingImpl left, RSASignaturePaddingImpl right) => !(left == right);

        public override string ToString() => _mode.ToString();
    }

    [Shadows("System.Security.Cryptography.RSAEncryptionPadding")]
    public sealed class RSAEncryptionPaddingImpl : IEquatable<RSAEncryptionPadding>
    {
        public static RSAEncryptionPadding Pkcs1 { get; } = (RSAEncryptionPadding)(object)new RSAEncryptionPaddingImpl(RSAEncryptionPaddingMode.Pkcs1, default);
        public static RSAEncryptionPadding OaepSHA1 { get; } = CreateOaep(HashAlgorithmName.SHA1);
        public static RSAEncryptionPadding OaepSHA256 { get; } = CreateOaep(HashAlgorithmName.SHA256);
        public static RSAEncryptionPadding OaepSHA384 { get; } = CreateOaep(HashAlgorithmName.SHA384);
        public static RSAEncryptionPadding OaepSHA512 { get; } = CreateOaep(HashAlgorithmName.SHA512);
        public static RSAEncryptionPadding OaepSHA3_256 { get; } = CreateOaep(HashAlgorithmName.SHA3_256);
        public static RSAEncryptionPadding OaepSHA3_384 { get; } = CreateOaep(HashAlgorithmName.SHA3_384);
        public static RSAEncryptionPadding OaepSHA3_512 { get; } = CreateOaep(HashAlgorithmName.SHA3_512);

        private readonly RSAEncryptionPaddingMode _mode;
        private readonly HashAlgorithmName _oaepHashAlgorithm;

        private RSAEncryptionPaddingImpl(RSAEncryptionPaddingMode mode, HashAlgorithmName oaepHashAlgorithm)
        {
            _mode = mode;
            _oaepHashAlgorithm = oaepHashAlgorithm;
        }

        public static RSAEncryptionPadding CreateOaep(HashAlgorithmName hashAlgorithm)
        {
            ArgumentException.ThrowIfNullOrEmpty(hashAlgorithm.Name, nameof(hashAlgorithm));
            return (RSAEncryptionPadding)(object)new RSAEncryptionPaddingImpl(RSAEncryptionPaddingMode.Oaep, hashAlgorithm);
        }

        public RSAEncryptionPaddingMode Mode => _mode;

        public HashAlgorithmName OaepHashAlgorithm => _oaepHashAlgorithm;

        public override int GetHashCode()
        {
            int h1 = _mode.GetHashCode(), h2 = _oaepHashAlgorithm.GetHashCode();
            return ((h1 << 5) + h1) ^ h2;
        }

        public override bool Equals(object obj) => Equals(obj as RSAEncryptionPadding);

        public bool Equals(RSAEncryptionPadding other) =>
            other is not null && _mode == other.Mode && _oaepHashAlgorithm == other.OaepHashAlgorithm;

        public static bool operator ==(RSAEncryptionPaddingImpl left, RSAEncryptionPaddingImpl right) =>
            left is null ? right is null : left.Equals((RSAEncryptionPadding)(object)right);

        public static bool operator !=(RSAEncryptionPaddingImpl left, RSAEncryptionPaddingImpl right) => !(left == right);

        public override string ToString() => _mode.ToString() + _oaepHashAlgorithm.Name;
    }

    [Shadows("System.Security.Cryptography.AsymmetricSignatureFormatter")]
    public abstract class AsymmetricSignatureFormatterImpl
    {
        protected AsymmetricSignatureFormatterImpl() { }

        public abstract void SetKey(AsymmetricAlgorithm key);
        public abstract void SetHashAlgorithm(string strName);

        public virtual byte[] CreateSignature(HashAlgorithm hash)
        {
            ArgumentNullException.ThrowIfNull(hash);
            SetHashAlgorithm(SignatureHashNames.AlgorithmName(hash));
            return CreateSignature(hash.Hash);
        }

        public abstract byte[] CreateSignature(byte[] rgbHash);
    }

    [Shadows("System.Security.Cryptography.AsymmetricSignatureDeformatter")]
    public abstract class AsymmetricSignatureDeformatterImpl
    {
        protected AsymmetricSignatureDeformatterImpl() { }

        public abstract void SetKey(AsymmetricAlgorithm key);
        public abstract void SetHashAlgorithm(string strName);

        public virtual bool VerifySignature(HashAlgorithm hash, byte[] rgbSignature)
        {
            ArgumentNullException.ThrowIfNull(hash);
            SetHashAlgorithm(SignatureHashNames.AlgorithmName(hash));
            return VerifySignature(hash.Hash, rgbSignature);
        }

        public abstract bool VerifySignature(byte[] rgbHash, byte[] rgbSignature);
    }

    [Shadows("System.Security.Cryptography.RSAPKCS1SignatureFormatter")]
    public class RSAPKCS1SignatureFormatterImpl : AsymmetricSignatureFormatter
    {
        private RSA _rsaKey;
        private string _algName;

        public RSAPKCS1SignatureFormatterImpl() { }

        public RSAPKCS1SignatureFormatterImpl(AsymmetricAlgorithm key)
        {
            ArgumentNullException.ThrowIfNull(key);
            _rsaKey = (RSA)key;
        }

        public override void SetKey(AsymmetricAlgorithm key)
        {
            ArgumentNullException.ThrowIfNull(key);
            _rsaKey = (RSA)key;
        }

        public override void SetHashAlgorithm(string strName) => _algName = SignatureHashNames.Canonical(strName);

        public override byte[] CreateSignature(byte[] rgbHash)
        {
            ArgumentNullException.ThrowIfNull(rgbHash);
            if (_algName == null) throw new CryptographicUnexpectedOperationException("No hash algorithm has been associated with this formatter object, assign one via the SetHashAlgorithm method.");
            if (_rsaKey == null) throw new CryptographicUnexpectedOperationException("No asymmetric key object has been associated with this formatter object, assign one via the SetKey method.");
            return _rsaKey.SignHash(rgbHash, new HashAlgorithmName(_algName), RSASignaturePadding.Pkcs1);
        }
    }

    [Shadows("System.Security.Cryptography.RSAPKCS1SignatureDeformatter")]
    public class RSAPKCS1SignatureDeformatterImpl : AsymmetricSignatureDeformatter
    {
        private RSA _rsaKey;
        private string _algName;

        public RSAPKCS1SignatureDeformatterImpl() { }

        public RSAPKCS1SignatureDeformatterImpl(AsymmetricAlgorithm key)
        {
            ArgumentNullException.ThrowIfNull(key);
            _rsaKey = (RSA)key;
        }

        public override void SetKey(AsymmetricAlgorithm key)
        {
            ArgumentNullException.ThrowIfNull(key);
            _rsaKey = (RSA)key;
        }

        public override void SetHashAlgorithm(string strName) => _algName = SignatureHashNames.Canonical(strName);

        public override bool VerifySignature(byte[] rgbHash, byte[] rgbSignature)
        {
            ArgumentNullException.ThrowIfNull(rgbHash);
            ArgumentNullException.ThrowIfNull(rgbSignature);
            if (_algName == null) throw new CryptographicUnexpectedOperationException("No hash algorithm has been associated with this formatter object, assign one via the SetHashAlgorithm method.");
            if (_rsaKey == null) throw new CryptographicUnexpectedOperationException("No asymmetric key object has been associated with this formatter object, assign one via the SetKey method.");
            return _rsaKey.VerifyHash(rgbHash, rgbSignature, new HashAlgorithmName(_algName), RSASignaturePadding.Pkcs1);
        }
    }

    // The formatters' SetHashAlgorithm: a name CryptoConfig.MapNameToOID knows (its default OID table, matched
    // case-insensitively; the Oid.FromFriendlyName fallback is left out) is kept, upper-cased by
    // HashAlgorithmNames.ToUpper; anything else unsets the algorithm.
    internal static class SignatureHashNames
    {
        private static readonly string[] s_oidNames =
        {
            "SHA", "SHA1", "System.Security.Cryptography.SHA1", "System.Security.Cryptography.SHA1CryptoServiceProvider",
            "System.Security.Cryptography.SHA1Cng", "System.Security.Cryptography.SHA1Managed",
            "SHA256", "System.Security.Cryptography.SHA256", "System.Security.Cryptography.SHA256CryptoServiceProvider",
            "System.Security.Cryptography.SHA256Cng", "System.Security.Cryptography.SHA256Managed",
            "SHA384", "System.Security.Cryptography.SHA384", "System.Security.Cryptography.SHA384CryptoServiceProvider",
            "System.Security.Cryptography.SHA384Cng", "System.Security.Cryptography.SHA384Managed",
            "SHA512", "System.Security.Cryptography.SHA512", "System.Security.Cryptography.SHA512CryptoServiceProvider",
            "System.Security.Cryptography.SHA512Cng", "System.Security.Cryptography.SHA512Managed",
            "RIPEMD160", "System.Security.Cryptography.RIPEMD160", "System.Security.Cryptography.RIPEMD160Managed",
            "MD5", "System.Security.Cryptography.MD5", "System.Security.Cryptography.MD5CryptoServiceProvider",
            "System.Security.Cryptography.MD5Managed", "TripleDESKeyWrap", "RC2", "System.Security.Cryptography.RC2CryptoServiceProvider",
            "DES", "System.Security.Cryptography.DESCryptoServiceProvider", "TripleDES", "System.Security.Cryptography.TripleDESCryptoServiceProvider",
        };

        internal static string Canonical(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            bool known = false;
            foreach (string oidName in s_oidNames)
                if (string.Equals(oidName, name, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
            if (!known) return null;
            foreach (string canonical in new[] { "SHA256", "SHA384", "SHA512", "SHA1", "MD5" })
                if (name.Equals(canonical, StringComparison.OrdinalIgnoreCase)) return canonical;
            return name;
        }

        internal static string AlgorithmName(HashAlgorithm hash) => hash switch
        {
            SHA1 => "SHA1",
            SHA256 => "SHA256",
            _ => hash.GetType().ToString(), // HashAlgorithm.ToAlgorithmName
        };
    }

    internal static unsafe class RsaDigest
    {
        internal static byte[] Hash(HashAlgorithmName hashAlgorithm, ReadOnlySpan<byte> data)
        {
            switch (hashAlgorithm.Name)
            {
                case "SHA1": return SHA1.HashData(data);
                case "SHA256": return SHA256.HashData(data);
                default: throw new CryptographicException("'" + hashAlgorithm.Name + "' is not a known hash algorithm.");
            }
        }
    }

}
#endif
