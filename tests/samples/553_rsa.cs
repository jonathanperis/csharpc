// RSA over the OS's crypto library: generated keys (only their shape and round trips are printed),
// RSAParameters import/export, PKCS#1 signatures with a fixed key (deterministic, so the bytes are
// printed), PSS and OAEP round trips, the signature (de)formatters, and .NET's argument errors.
using System;
using System.Security.Cryptography;
using System.Text;

var fixedKey = new RSAParameters
{
    Modulus = Convert.FromHexString("B0B24024FECB21C5F6788403377C1ACA792E012A9C163392A5E4614C3109F758CDB7B71A196651B4D58D4ACE059FD29CAE7607B363D0FF1516BBEA1875749359770D62EB8BC35B6CF30F5E45CA4342A743D9DE4798ACF24C795E6C3C1F31ED88C4E2EC17E9D2CD329D9369C55487FF3BD3134F1748F86EDE91D011142E3D4B7F"),
    Exponent = new byte[] { 1, 0, 1 },
    D = Convert.FromHexString("8AEF2DA6A93D79C9D9B9FB3B38467A1E5858470C84FCA4029C344E1905BA2B6605237E72A42422FCC8A9B776F54F2CE08C013C9EDE4E7833D1D36207675B897B91AAB1893787759ADFE7932B1A40469C4DBD248EF534D196014D0FEA89D8F6E30C84C0DE38911E035C1E452B7FE287678133B717C9F79B666832B7B791C98921"),
    P = Convert.FromHexString("D90F43368995C814B139291F8C95A0F98B2FF5A1C1ECD23E69960ED9D8D4A8CB3B9A2547B4FFFCB45516AAF243B94ED068F37DF4660D012D3198FEFB4CE754D1"),
    Q = Convert.FromHexString("D065404AB7213C13C6FFA63ADD915488D02DF051561A713EF4C45982CBB98374725E50C06E5FEF285CEAC4875C1C4423B1F6B89CBF8FAE06E2925EF1A6BBEF4F"),
    DP = Convert.FromHexString("BA3988FDEA6ED21946AF76E3B576CE842A717C394AAAC6FA7B161E29B30948C40C9FC46A5014EC173F855226C609DF5F991B86ADBF3C5B0D0E276863EC67F071"),
    DQ = Convert.FromHexString("A2AEB81EDB91354E172651C5585C708A91419B7E5362370FB792647227C9BDA634F60DC1FB282711F25F4F667FF08CFCF49BAF08A6BE6A5373A2623DF1347B1F"),
    InverseQ = Convert.FromHexString("14573D93018711F1904884BC70E7DE56E2E43603D89DDBD3A1795929A3EB29B4BFA227947986330F30D188917359234F16BC08BCBDFB51D10A91337B6827ED91"),
};
byte[] data = Encoding.UTF8.GetBytes("Hello, from csharpc!");

// A generated key: sizes, and what survives an export/import round trip.
using (RSA rsa = RSA.Create())
{
    Console.WriteLine($"default key {rsa.KeySize} bits, max output {rsa.GetMaxOutputSize()}, {rsa.SignatureAlgorithm}/{rsa.KeyExchangeAlgorithm}");
    Console.WriteLine($"legal sizes: {string.Join(", ", Array.ConvertAll(rsa.LegalKeySizes, k => $"{k.MinSize}-{k.MaxSize} step {k.SkipSize}"))}");
    RSAParameters pub = rsa.ExportParameters(false), priv = rsa.ExportParameters(true);
    Console.WriteLine($"public: modulus {pub.Modulus!.Length}, exponent {Convert.ToHexString(pub.Exponent!)}, private parts {pub.D is null && pub.P is null}");
    Console.WriteLine($"private: D {priv.D!.Length}, P {priv.P!.Length}, Q {priv.Q!.Length}, DP {priv.DP!.Length}, DQ {priv.DQ!.Length}, InverseQ {priv.InverseQ!.Length}");
    byte[] signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    using RSA verifier = RSA.Create(pub);
    Console.WriteLine($"PSS signature {signature.Length} bytes, verified by the public key: {verifier.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)}");
    signature[10] ^= 1;
    Console.WriteLine($"tampered: {verifier.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)}");
    foreach (var padding in new[] { RSAEncryptionPadding.Pkcs1, RSAEncryptionPadding.OaepSHA1, RSAEncryptionPadding.OaepSHA256 })
    {
        byte[] encrypted = verifier.Encrypt(data, padding);
        Console.WriteLine($"{padding}: {encrypted.Length} bytes -> {Encoding.UTF8.GetString(rsa.Decrypt(encrypted, padding))}");
    }
}
using (RSA small = RSA.Create(1024))
    Console.WriteLine($"Create(1024): {small.KeySize} bits, modulus {small.ExportParameters(false).Modulus!.Length} bytes");

// A fixed key: PKCS#1 v1.5 signatures are deterministic.
using (RSA rsa = RSA.Create(fixedKey))
{
    Console.WriteLine($"imported key {rsa.KeySize} bits, exported D matches: {rsa.ExportParameters(true).D!.AsSpan().SequenceEqual(fixedKey.D)}");
    foreach (var hash in new[] { HashAlgorithmName.SHA1, HashAlgorithmName.SHA256, HashAlgorithmName.SHA384, HashAlgorithmName.SHA512, HashAlgorithmName.MD5 })
    {
        byte[] signature = rsa.SignData(data, hash, RSASignaturePadding.Pkcs1);
        Console.WriteLine($"{hash.Name}: {Convert.ToHexString(signature)[..32]}... verifies {rsa.VerifyData(data, signature, hash, RSASignaturePadding.Pkcs1)}");
    }
    byte[] digest = SHA256.HashData(data);
    var formatter = new RSAPKCS1SignatureFormatter(rsa);
    formatter.SetHashAlgorithm("sha256");
    byte[] formatted = formatter.CreateSignature(digest);
    Console.WriteLine($"formatter = SignHash: {formatted.AsSpan().SequenceEqual(rsa.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))}");
    var deformatter = new RSAPKCS1SignatureDeformatter(RSA.Create(new RSAParameters { Modulus = fixedKey.Modulus, Exponent = fixedKey.Exponent }));
    deformatter.SetHashAlgorithm(nameof(SHA256));
    Console.WriteLine($"deformatter: {deformatter.VerifySignature(digest, formatted)}, wrong hash: {deformatter.VerifySignature(SHA256.HashData(digest), formatted)}");
    Console.WriteLine($"offset/count: {rsa.VerifyData(data, 0, 5, rsa.SignData(data, 0, 5, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)}");
    try { new RSAPKCS1SignatureFormatter(rsa).CreateSignature(digest); }
    catch (CryptographicUnexpectedOperationException e) { Console.WriteLine($"no algorithm: {e.Message}"); }
    var unknown = new RSAPKCS1SignatureFormatter(rsa);
    unknown.SetHashAlgorithm("Whirlpool");
    try { unknown.CreateSignature(digest); }
    catch (CryptographicUnexpectedOperationException e) { Console.WriteLine($"unknown algorithm: {e.Message}"); }
    var noKey = new RSAPKCS1SignatureFormatter();
    noKey.SetHashAlgorithm("SHA1");
    try { noKey.CreateSignature(digest); }
    catch (CryptographicUnexpectedOperationException e) { Console.WriteLine($"no key: {e.Message}"); }
    try { new RSAPKCS1SignatureDeformatter().VerifySignature(digest, formatted); }
    catch (CryptographicUnexpectedOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    try { rsa.Decrypt(new byte[5], RSAEncryptionPadding.Pkcs1); }
    catch (CryptographicException e) { Console.WriteLine($"decrypt: {e.Message}"); }
    try { rsa.SignData(data, default, RSASignaturePadding.Pkcs1); }
    catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    try { rsa.SignData(data, new HashAlgorithmName("FOO"), RSASignaturePadding.Pkcs1); }
    catch (CryptographicException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}

// Paddings, invalid parameters, key sizes and disposal.
Console.WriteLine($"paddings: {RSASignaturePadding.Pkcs1} {RSASignaturePadding.Pss} {RSAEncryptionPadding.OaepSHA512} {RSAEncryptionPadding.CreateOaep(HashAlgorithmName.SHA256) == RSAEncryptionPadding.OaepSHA256} {RSASignaturePadding.Pss.Equals(RSASignaturePadding.Pkcs1)}");
try { RSA.Create(new RSAParameters { Modulus = fixedKey.Modulus }); }
catch (CryptographicException e) { Console.WriteLine($"no exponent: {e.Message}"); }
try { RSA.Create(new RSAParameters { Modulus = fixedKey.Modulus, Exponent = fixedKey.Exponent, P = fixedKey.P }); }
catch (CryptographicException e) { Console.WriteLine($"inconsistent: {e.Message}"); }
try { RSA.Create(1000).Dispose(); Console.WriteLine("1000 bits accepted"); RSA.Create(513); }
catch (CryptographicException e) { Console.WriteLine($"513 bits: {e.Message}"); }
var disposed = RSA.Create();
disposed.Dispose();
try { disposed.ExportParameters(false); }
catch (ObjectDisposedException e) { Console.WriteLine($"disposed: {e.Message}"); }
