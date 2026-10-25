// Aes.Create(), its transforms, CryptoStream and the one-shot APIs over the OS cipher (OpenSSL on Linux,
// CommonCrypto on macOS), with a fixed key and IV so the ciphertext is the same everywhere
// (docs-standard-security-encrypting-data-aesencrypt).
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

byte[] key = new byte[16], iv = new byte[16];
for (int i = 0; i < 16; i++) { key[i] = (byte)(i + 1); iv[i] = (byte)(0xA0 + i); }
byte[] plain = Encoding.UTF8.GetBytes("Hello, AES! The quick brown fox jumps over the lazy dog.");

using var aes = Aes.Create();
Console.WriteLine($"defaults: {aes.KeySize} {aes.BlockSize} {aes.Mode} {aes.Padding} {aes.FeedbackSize} legal keys {string.Join(",", Array.ConvertAll(aes.LegalKeySizes, k => $"{k.MinSize}-{k.MaxSize}/{k.SkipSize}"))}");
Console.WriteLine($"random key/IV lengths: {aes.Key.Length} {aes.IV.Length}");
aes.Key = key;
aes.IV = iv;

// CryptoStream with a StreamWriter, as the docs' sample.
var encrypted = new MemoryStream();
using (var crypto = new CryptoStream(encrypted, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
using (var writer = new StreamWriter(crypto))
    writer.Write("Hello World!");
Console.WriteLine($"CBC/PKCS7 via CryptoStream: {Convert.ToHexString(encrypted.ToArray())}");
encrypted.Position = 0;
using (var crypto = new CryptoStream(encrypted, aes.CreateDecryptor(), CryptoStreamMode.Read))
using (var reader = new StreamReader(crypto))
    Console.WriteLine($"decrypted: {reader.ReadToEnd()}");

// One-shots in every mode and padding.
Console.WriteLine($"EncryptCbc: {Convert.ToHexString(aes.EncryptCbc(plain, iv))}");
Console.WriteLine($"EncryptEcb: {Convert.ToHexString(aes.EncryptEcb(plain, PaddingMode.PKCS7))}");
Console.WriteLine($"EncryptCfb8: {Convert.ToHexString(aes.EncryptCfb(plain, iv, PaddingMode.None, 8))}");
Console.WriteLine($"EncryptCfb128: {Convert.ToHexString(aes.EncryptCfb(plain, iv, PaddingMode.PKCS7, 128))}");
foreach (var padding in new[] { PaddingMode.Zeros, PaddingMode.ANSIX923, PaddingMode.PKCS7 })
{
    byte[] c = aes.EncryptCbc(plain.AsSpan(0, 20), iv, padding);
    byte[] p = aes.DecryptCbc(c, iv, padding);
    Console.WriteLine($"{padding}: {c.Length} bytes, round trip {Encoding.UTF8.GetString(p).TrimEnd('\0')}");
}
Console.WriteLine($"GetCiphertextLengthCbc: {aes.GetCiphertextLengthCbc(20)} {aes.GetCiphertextLengthCbc(32, PaddingMode.None)}");

// AES-256 through TransformBlock/TransformFinalBlock.
aes.KeySize = 256;
byte[] key256 = new byte[32];
for (int i = 0; i < 32; i++) key256[i] = (byte)(255 - i);
using (var transform = aes.CreateEncryptor(key256, iv))
{
    byte[] output = new byte[64];
    int n = transform.TransformBlock(plain, 0, 32, output, 0);
    byte[] last = transform.TransformFinalBlock(plain, 32, plain.Length - 32);
    Console.WriteLine($"AES-256 blocks: {n} + {last.Length}: {Convert.ToHexString(output, 0, n)}{Convert.ToHexString(last)}");
    Console.WriteLine($"transform: {transform.InputBlockSize} {transform.OutputBlockSize} {transform.CanTransformMultipleBlocks} {transform.CanReuseTransform}");
}

// Errors are .NET's. (A fixed key: KeySize = 256 above made a random one, which decrypts the zero block
// with valid padding once in 256 runs.)
aes.Key = key256;
try { aes.Key = new byte[10]; }
catch (CryptographicException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { aes.DecryptCbc(new byte[16], iv); }
catch (CryptographicException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { aes.IV = new byte[8]; }
catch (CryptographicException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

// Random numbers and fixed-time comparison.
Console.WriteLine($"RandomNumberGenerator: {RandomNumberGenerator.GetBytes(8).Length} {RandomNumberGenerator.GetInt32(1, 2)} {CryptographicOperations.FixedTimeEquals(key, key)} {CryptographicOperations.FixedTimeEquals(key, iv)}");
// PBKDF2 (RFC 6070 test vector) and HMAC (RFC 4231 test case 2).
Console.WriteLine($"PBKDF2-SHA1: {Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(Encoding.ASCII.GetBytes("password"), Encoding.ASCII.GetBytes("salt"), 4096, HashAlgorithmName.SHA1, 20))}");
using (var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, Encoding.ASCII.GetBytes("Jefe")))
{
    hmac.AppendData(Encoding.ASCII.GetBytes("what do ya want "));
    hmac.AppendData(Encoding.ASCII.GetBytes("for nothing?"));
    Console.WriteLine($"HMAC-SHA256: {Convert.ToHexString(hmac.GetHashAndReset())}");
}
