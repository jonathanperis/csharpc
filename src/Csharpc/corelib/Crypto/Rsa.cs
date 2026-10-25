// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Security.Cryptography/src/
// System/Security/Cryptography/AsymmetricAlgorithm.cs, RSA.cs, RSASignaturePadding.cs, RSAEncryptionPadding.cs,
// AsymmetricSignature(De)Formatter.cs, RSAPKCS1Signature(De)Formatter.cs and Common/.../RSAOpenSsl.cs;
// see THIRD-PARTY-NOTICES.md.
// RSA as .NET runs it on Linux: the abstract surface ported (argument checks in .NET's order), and
// RSA.Create()'s key over OpenSSL's EVP_PKEY interface (libcrypto 3) — key generation, RSAParameters
// export/import, PKCS#1 / PSS signatures and PKCS#1 / OAEP encryption, the same calls .NET's native shim
// makes. The DER key formats (PKCS#1, PKCS#8, SPKI, PEM, XML) are not ported: those members aren't
// there, a compile-time error. macOS (Security.framework), Windows (CNG) and WASI have no implementation
// yet: RSA.Create() isn't there either.
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
#if CSHARPC_TARGET_LINUX
        public static new RSA Create() => new RsaOpenSsl(2048);

        public static RSA Create(int keySizeInBits)
        {
            RSA rsa = Create();
            try
            {
                rsa.KeySize = keySizeInBits;
                return rsa;
            }
            catch
            {
                rsa.Dispose();
                throw;
            }
        }

        public static RSA Create(RSAParameters parameters)
        {
            RSA rsa = Create();
            try
            {
                rsa.ImportParameters(parameters);
                return rsa;
            }
            catch
            {
                rsa.Dispose();
                throw;
            }
        }
#endif

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
#if CSHARPC_TARGET_LINUX
            IntPtr md = RsaOpenSsl.Digest(hashAlgorithm.Name);
            byte* output = stackalloc byte[64];
            uint length;
            fixed (byte* input = data)
                if (RsaOpenSsl.EVP_Digest(input, (nuint)data.Length, output, &length, md, IntPtr.Zero) != 1) throw new CryptographicException();
            return new ReadOnlySpan<byte>(output, (int)length).ToArray();
#else
            switch (hashAlgorithm.Name)
            {
                case "SHA1": return SHA1.HashData(data);
                case "SHA256": return SHA256.HashData(data);
                default: throw new CryptographicException("'" + hashAlgorithm.Name + "' is not a known hash algorithm.");
            }
#endif
        }
    }

#if CSHARPC_TARGET_LINUX
    // RSAOpenSsl (Common/.../RSAOpenSsl.cs) with the native shim's EVP_PKEY calls inlined. The key is generated
    // on first use, so setting KeySize or importing parameters after Create() costs no key generation.
    internal sealed unsafe class RsaOpenSsl : RSA
    {
        private const string Lib = "libcrypto.so.3";
        private const int EVP_PKEY_PUBLIC_KEY = 0x86, EVP_PKEY_KEYPAIR = 0x87;
        private const int RSA_PKCS1_PADDING = 1, RSA_PKCS1_OAEP_PADDING = 4, RSA_PKCS1_PSS_PADDING = 6, RSA_PSS_SALTLEN_DIGEST = -1;

        [DllImport(Lib)] private static extern IntPtr EVP_PKEY_CTX_new_from_name(IntPtr libctx, byte* name, IntPtr propq);
        [DllImport(Lib)] private static extern IntPtr EVP_PKEY_CTX_new(IntPtr pkey, IntPtr engine);
        [DllImport(Lib)] private static extern void EVP_PKEY_CTX_free(IntPtr ctx);
        [DllImport(Lib)] private static extern int EVP_PKEY_keygen_init(IntPtr ctx);
        [DllImport(Lib)] private static extern int EVP_PKEY_CTX_set_rsa_keygen_bits(IntPtr ctx, int bits);
        [DllImport(Lib)] private static extern int EVP_PKEY_generate(IntPtr ctx, IntPtr* pkey);
        [DllImport(Lib)] private static extern int EVP_PKEY_fromdata_init(IntPtr ctx);
        [DllImport(Lib)] private static extern int EVP_PKEY_fromdata(IntPtr ctx, IntPtr* pkey, int selection, IntPtr parameters);
        [DllImport(Lib)] private static extern void EVP_PKEY_free(IntPtr pkey);
        [DllImport(Lib)] private static extern int EVP_PKEY_get_bits(IntPtr pkey);
        [DllImport(Lib)] private static extern int EVP_PKEY_get_size(IntPtr pkey);
        [DllImport(Lib)] private static extern int EVP_PKEY_get_bn_param(IntPtr pkey, byte* name, IntPtr* bn);
        [DllImport(Lib)] private static extern IntPtr OSSL_PARAM_BLD_new();
        [DllImport(Lib)] private static extern void OSSL_PARAM_BLD_free(IntPtr bld);
        [DllImport(Lib)] private static extern int OSSL_PARAM_BLD_push_BN(IntPtr bld, byte* key, IntPtr bn);
        [DllImport(Lib)] private static extern IntPtr OSSL_PARAM_BLD_to_param(IntPtr bld);
        [DllImport(Lib)] private static extern void OSSL_PARAM_free(IntPtr parameters);
        [DllImport(Lib)] private static extern IntPtr BN_bin2bn(byte* s, int len, IntPtr ret);
        [DllImport(Lib)] private static extern int BN_bn2binpad(IntPtr a, byte* to, int tolen);
        [DllImport(Lib)] private static extern int BN_num_bits(IntPtr a);
        [DllImport(Lib)] private static extern void BN_clear_free(IntPtr a);
        [DllImport(Lib)] private static extern int EVP_PKEY_sign_init(IntPtr ctx);
        [DllImport(Lib)] private static extern int EVP_PKEY_sign(IntPtr ctx, byte* sig, nuint* siglen, byte* tbs, nuint tbslen);
        [DllImport(Lib)] private static extern int EVP_PKEY_verify_init(IntPtr ctx);
        [DllImport(Lib)] private static extern int EVP_PKEY_verify(IntPtr ctx, byte* sig, nuint siglen, byte* tbs, nuint tbslen);
        [DllImport(Lib)] private static extern int EVP_PKEY_encrypt_init(IntPtr ctx);
        [DllImport(Lib)] private static extern int EVP_PKEY_encrypt(IntPtr ctx, byte* output, nuint* outlen, byte* input, nuint inlen);
        [DllImport(Lib)] private static extern int EVP_PKEY_decrypt_init(IntPtr ctx);
        [DllImport(Lib)] private static extern int EVP_PKEY_decrypt(IntPtr ctx, byte* output, nuint* outlen, byte* input, nuint inlen);
        [DllImport(Lib)] private static extern int EVP_PKEY_CTX_set_rsa_padding(IntPtr ctx, int pad);
        [DllImport(Lib)] private static extern int EVP_PKEY_CTX_set_signature_md(IntPtr ctx, IntPtr md);
        [DllImport(Lib)] private static extern int EVP_PKEY_CTX_set_rsa_pss_saltlen(IntPtr ctx, int len);
        [DllImport(Lib)] private static extern int EVP_PKEY_CTX_set_rsa_oaep_md(IntPtr ctx, IntPtr md);
        [DllImport(Lib)] private static extern int EVP_PKEY_CTX_set_rsa_mgf1_md(IntPtr ctx, IntPtr md);
        [DllImport(Lib)] private static extern void ERR_clear_error();
        [DllImport(Lib)] private static extern IntPtr EVP_md5();
        [DllImport(Lib)] private static extern IntPtr EVP_sha1();
        [DllImport(Lib)] private static extern IntPtr EVP_sha256();
        [DllImport(Lib)] private static extern IntPtr EVP_sha384();
        [DllImport(Lib)] private static extern IntPtr EVP_sha512();
        [DllImport(Lib)] private static extern IntPtr EVP_sha3_256();
        [DllImport(Lib)] private static extern IntPtr EVP_sha3_384();
        [DllImport(Lib)] private static extern IntPtr EVP_sha3_512();
        [DllImport(Lib)] internal static extern int EVP_Digest(byte* data, nuint count, byte* md, uint* size, IntPtr type, IntPtr engine);

        private IntPtr _key;
        private bool _disposed;

        public RsaOpenSsl(int keySize) => base.KeySize = keySize;

        // Interop.Crypto.HashAlgorithmToEvp.
        internal static IntPtr Digest(string name) => name switch
        {
            "SHA1" => EVP_sha1(),
            "SHA256" => EVP_sha256(),
            "SHA384" => EVP_sha384(),
            "SHA512" => EVP_sha512(),
            "MD5" => EVP_md5(),
            "SHA3-256" => EVP_sha3_256(),
            "SHA3-384" => EVP_sha3_384(),
            "SHA3-512" => EVP_sha3_512(),
            _ => throw new CryptographicException("'" + name + "' is not a known hash algorithm."),
        };

        public override int KeySize
        {
            set
            {
                if (KeySize == value) return;
                base.KeySize = value;
                ThrowIfDisposed();
                FreeKey();
            }
        }

        public override KeySizes[] LegalKeySizes => new[] { new KeySizes(512, 16384, 8) };

        // RSA.Create() wraps an RSAOpenSsl in .NET; the wrapped key names itself in the exception.
        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException("System.Security.Cryptography.RSAOpenSsl");
        }

        private void FreeKey()
        {
            if (_key != IntPtr.Zero) EVP_PKEY_free(_key);
            _key = IntPtr.Zero;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FreeKey();
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        private IntPtr GetKey()
        {
            ThrowIfDisposed();
            if (_key == IntPtr.Zero) _key = GenerateKey(KeySize);
            return _key;
        }

        private static IntPtr GenerateKey(int bits)
        {
            IntPtr ctx;
            fixed (byte* name = "RSA\0"u8) ctx = EVP_PKEY_CTX_new_from_name(IntPtr.Zero, name, IntPtr.Zero);
            if (ctx == IntPtr.Zero) throw Error();
            try
            {
                IntPtr pkey = IntPtr.Zero;
                if (EVP_PKEY_keygen_init(ctx) != 1 || EVP_PKEY_CTX_set_rsa_keygen_bits(ctx, bits) != 1 || EVP_PKEY_generate(ctx, &pkey) != 1)
                    throw Error();
                return pkey;
            }
            finally
            {
                EVP_PKEY_CTX_free(ctx);
            }
        }

        private static CryptographicException Error()
        {
            ERR_clear_error();
            return new CryptographicException();
        }

        private int KeySizeBytes(IntPtr key) => EVP_PKEY_get_size(key);

        // ---- RSAParameters (RSAKeyFormatHelper's PKCS#1 reading: the modulus and exponent as minimal
        // big-endian integers, D padded to the modulus length, the CRT values to half of it) ----

        public override RSAParameters ExportParameters(bool includePrivateParameters)
        {
            IntPtr key = GetKey();
            var parameters = new RSAParameters
            {
                Modulus = ExportBn(key, "n\0"u8, 0),
                Exponent = ExportBn(key, "e\0"u8, 0),
            };
            if (includePrivateParameters)
            {
                int modulusLength = parameters.Modulus.Length, halfModulus = (modulusLength + 1) / 2;
                parameters.D = ExportBn(key, "d\0"u8, modulusLength);
                parameters.P = ExportBn(key, "rsa-factor1\0"u8, halfModulus);
                parameters.Q = ExportBn(key, "rsa-factor2\0"u8, halfModulus);
                parameters.DP = ExportBn(key, "rsa-exponent1\0"u8, halfModulus);
                parameters.DQ = ExportBn(key, "rsa-exponent2\0"u8, halfModulus);
                parameters.InverseQ = ExportBn(key, "rsa-coefficient1\0"u8, halfModulus);
            }
            return parameters;
        }

        private static byte[] ExportBn(IntPtr key, ReadOnlySpan<byte> name, int length)
        {
            IntPtr bn = IntPtr.Zero;
            fixed (byte* n = name)
            {
                if (EVP_PKEY_get_bn_param(key, n, &bn) != 1)
                {
                    ERR_clear_error();
                    throw new CryptographicException("The key does not permit being exported.");
                }
            }
            try
            {
                if (length == 0) length = (BN_num_bits(bn) + 7) / 8;
                byte[] result = new byte[length];
                fixed (byte* p = result)
                    if (BN_bn2binpad(bn, p, length) != length) throw Error();
                return result;
            }
            finally
            {
                BN_clear_free(bn);
            }
        }

        public override void ImportParameters(RSAParameters parameters)
        {
            if (parameters.Modulus == null || parameters.Exponent == null || !HasConsistentPrivateKey(ref parameters))
                throw new CryptographicException("Value was invalid.");
            ThrowIfDisposed();

            bool hasPrivate = parameters.D != null;
            IntPtr builder = OSSL_PARAM_BLD_new();
            IntPtr[] numbers = new IntPtr[8];
            int count = 0;
            IntPtr ossl = IntPtr.Zero, ctx = IntPtr.Zero, pkey = IntPtr.Zero;
            try
            {
                if (builder == IntPtr.Zero) throw Error();
                Push(builder, "n\0"u8, parameters.Modulus, numbers, ref count);
                Push(builder, "e\0"u8, parameters.Exponent, numbers, ref count);
                if (hasPrivate)
                {
                    Push(builder, "d\0"u8, parameters.D, numbers, ref count);
                    Push(builder, "rsa-factor1\0"u8, parameters.P, numbers, ref count);
                    Push(builder, "rsa-factor2\0"u8, parameters.Q, numbers, ref count);
                    Push(builder, "rsa-exponent1\0"u8, parameters.DP, numbers, ref count);
                    Push(builder, "rsa-exponent2\0"u8, parameters.DQ, numbers, ref count);
                    Push(builder, "rsa-coefficient1\0"u8, parameters.InverseQ, numbers, ref count);
                }
                ossl = OSSL_PARAM_BLD_to_param(builder);
                fixed (byte* name = "RSA\0"u8) ctx = EVP_PKEY_CTX_new_from_name(IntPtr.Zero, name, IntPtr.Zero);
                if (ossl == IntPtr.Zero || ctx == IntPtr.Zero || EVP_PKEY_fromdata_init(ctx) != 1
                    || EVP_PKEY_fromdata(ctx, &pkey, hasPrivate ? EVP_PKEY_KEYPAIR : EVP_PKEY_PUBLIC_KEY, ossl) != 1)
                    throw Error();
            }
            finally
            {
                if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
                if (ossl != IntPtr.Zero) OSSL_PARAM_free(ossl);
                if (builder != IntPtr.Zero) OSSL_PARAM_BLD_free(builder);
                for (int i = 0; i < count; i++) BN_clear_free(numbers[i]);
            }
            FreeKey();
            _key = pkey;
            // Like ForceSetKeySize: an imported key's size is trusted even outside LegalKeySizes.
            KeySizeValue = EVP_PKEY_get_bits(pkey);
        }

        private static void Push(IntPtr builder, ReadOnlySpan<byte> name, byte[] value, IntPtr[] numbers, ref int count)
        {
            IntPtr bn;
            fixed (byte* p = value) bn = BN_bin2bn(p, value.Length, IntPtr.Zero);
            if (bn == IntPtr.Zero) throw Error();
            numbers[count++] = bn;
            fixed (byte* n = name)
                if (OSSL_PARAM_BLD_push_BN(builder, n, bn) != 1) throw Error();
        }

        private static bool HasConsistentPrivateKey(ref RSAParameters parameters)
        {
            if (parameters.D == null)
                return parameters.P == null && parameters.DP == null && parameters.Q == null && parameters.DQ == null && parameters.InverseQ == null;
            return parameters.P != null && parameters.DP != null && parameters.Q != null && parameters.DQ != null && parameters.InverseQ != null;
        }

        // ---- Signatures (CryptoNative_RsaSignHash / RsaVerifyHash) ----

        public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            ArgumentNullException.ThrowIfNull(hash);
            ArgumentException.ThrowIfNullOrEmpty(hashAlgorithm.Name, nameof(hashAlgorithm));
            ArgumentNullException.ThrowIfNull(padding);
            ThrowIfDisposed();

            IntPtr key = GetKey();
            IntPtr ctx = SignatureContext(key, padding, hashAlgorithm, sign: true);
            try
            {
                byte[] signature = new byte[KeySizeBytes(key)];
                nuint length = (nuint)signature.Length;
                fixed (byte* s = signature)
                fixed (byte* h = hash)
                    if (EVP_PKEY_sign(ctx, s, &length, h, (nuint)hash.Length) != 1) throw Error();
                if ((int)length != signature.Length) throw new CryptographicException();
                return signature;
            }
            finally
            {
                EVP_PKEY_CTX_free(ctx);
            }
        }

        public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            ArgumentNullException.ThrowIfNull(hash);
            ArgumentNullException.ThrowIfNull(signature);
            ArgumentException.ThrowIfNullOrEmpty(hashAlgorithm.Name, nameof(hashAlgorithm));
            ValidatePadding(padding);
            ThrowIfDisposed();

            IntPtr key = GetKey();
            IntPtr ctx = SignatureContext(key, padding, hashAlgorithm, sign: false);
            try
            {
                int result;
                fixed (byte* s = signature)
                fixed (byte* h = hash)
                    result = EVP_PKEY_verify(ctx, s, (nuint)signature.Length, h, (nuint)hash.Length);
                if (result == 1) return true;
                ERR_clear_error();
                return false;
            }
            finally
            {
                EVP_PKEY_CTX_free(ctx);
            }
        }

        private static IntPtr SignatureContext(IntPtr key, RSASignaturePadding padding, HashAlgorithmName hashAlgorithm, bool sign)
        {
            IntPtr md = Digest(hashAlgorithm.Name);
            int mode = padding.Mode switch
            {
                RSASignaturePaddingMode.Pkcs1 => RSA_PKCS1_PADDING,
                RSASignaturePaddingMode.Pss => RSA_PKCS1_PSS_PADDING,
                _ => throw new CryptographicException("Specified padding mode is not valid for this algorithm."),
            };
            IntPtr ctx = EVP_PKEY_CTX_new(key, IntPtr.Zero);
            if (ctx == IntPtr.Zero) throw Error();
            if ((sign ? EVP_PKEY_sign_init(ctx) : EVP_PKEY_verify_init(ctx)) != 1
                || EVP_PKEY_CTX_set_rsa_padding(ctx, mode) != 1
                || (mode == RSA_PKCS1_PSS_PADDING && EVP_PKEY_CTX_set_rsa_pss_saltlen(ctx, RSA_PSS_SALTLEN_DIGEST) != 1)
                || EVP_PKEY_CTX_set_signature_md(ctx, md) != 1)
            {
                EVP_PKEY_CTX_free(ctx);
                throw Error();
            }
            return ctx;
        }

        private static void ValidatePadding(RSASignaturePadding padding)
        {
            ArgumentNullException.ThrowIfNull(padding);
            if (padding.Mode != RSASignaturePaddingMode.Pkcs1 && padding.Mode != RSASignaturePaddingMode.Pss)
                throw new CryptographicException("Specified padding mode is not valid for this algorithm.");
        }

        // ---- Encryption (CryptoNative_RsaEncrypt / RsaDecrypt) ----

        private static void ValidatePadding(RSAEncryptionPadding padding)
        {
            ArgumentNullException.ThrowIfNull(padding);
            if (padding.Mode != RSAEncryptionPaddingMode.Oaep && padding != RSAEncryptionPadding.Pkcs1)
                throw new CryptographicException("Specified padding mode is not valid for this algorithm.");
            // The platform's hashing must support the OAEP digest (HashProviderDispenser.HashSupported).
            if (padding.Mode == RSAEncryptionPaddingMode.Oaep && padding.OaepHashAlgorithm.Name is string name
                && name is not ("MD5" or "SHA1" or "SHA256" or "SHA384" or "SHA512" or "SHA3-256" or "SHA3-384" or "SHA3-512"))
                throw new PlatformNotSupportedException();
        }

        public override byte[] Encrypt(byte[] data, RSAEncryptionPadding padding)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(padding);
            ValidatePadding(padding);
            IntPtr key = GetKey();
            byte[] buffer = new byte[KeySizeBytes(key)];
            int written = Crypt(key, data, buffer, padding, encrypt: true);
            if (written != buffer.Length) throw new CryptographicException();
            return buffer;
        }

        public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(padding);
            ValidatePadding(padding);
            IntPtr key = GetKey();
            int rsaSize = KeySizeBytes(key);
            if (data.Length != rsaSize) throw new CryptographicException("The length of the data to decrypt is not valid for the size of this key.");
            byte[] buffer = new byte[rsaSize];
            int written = Crypt(key, data, buffer, padding, encrypt: false);
            byte[] result = new ReadOnlySpan<byte>(buffer, 0, written).ToArray();
            CryptographicOperations.ZeroMemory(buffer);
            return result;
        }

        private static int Crypt(IntPtr key, byte[] data, byte[] destination, RSAEncryptionPadding padding, bool encrypt)
        {
            IntPtr ctx = EVP_PKEY_CTX_new(key, IntPtr.Zero);
            if (ctx == IntPtr.Zero) throw Error();
            try
            {
                bool oaep = padding.Mode == RSAEncryptionPaddingMode.Oaep;
                IntPtr md = oaep ? Digest(padding.OaepHashAlgorithm.Name) : IntPtr.Zero;
                if ((encrypt ? EVP_PKEY_encrypt_init(ctx) : EVP_PKEY_decrypt_init(ctx)) != 1
                    || EVP_PKEY_CTX_set_rsa_padding(ctx, oaep ? RSA_PKCS1_OAEP_PADDING : RSA_PKCS1_PADDING) != 1
                    || (oaep && (EVP_PKEY_CTX_set_rsa_oaep_md(ctx, md) != 1 || EVP_PKEY_CTX_set_rsa_mgf1_md(ctx, md) != 1)))
                    throw Error();
                nuint length = (nuint)destination.Length;
                int result;
                fixed (byte* output = destination)
                fixed (byte* input = data)
                    result = encrypt
                        ? EVP_PKEY_encrypt(ctx, output, &length, input, (nuint)data.Length)
                        : EVP_PKEY_decrypt(ctx, output, &length, input, (nuint)data.Length);
                if (result != 1) throw Error();
                return (int)length;
            }
            finally
            {
                EVP_PKEY_CTX_free(ctx);
            }
        }
    }
#endif
}
