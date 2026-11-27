// Hashing, HMAC, AES, PBKDF2 and random-number throughput, the program behind docs/il-first/System.Security.Cryptography.md: one-shot and
// incremental SHA-1/256/384/512 and MD5 over small and large inputs, HMAC-SHA256, AES-CBC/ECB and the CryptoStream path, PBKDF2 and
// RandomNumberGenerator. The checksum depends on the fixed inputs only (the random bytes are counted, not summed).
using System;
using System.IO;
using System.Security.Cryptography;

class Program
{
    static void Main()
    {
        long check = 0;
        var big = new byte[1 << 20];
        for (int i = 0; i < big.Length; i++) big[i] = (byte)(i * 31 + (i >> 7));
        Span<byte> digest = stackalloc byte[64];

        // Small messages: per-call overhead dominates.
        for (int i = 0; i < 200_000; i++)
        {
            var msg = big.AsSpan(i & 4095, 64);
            SHA256.HashData(msg, digest); check += digest[0];
            SHA1.HashData(msg, digest); check += digest[1];
            MD5.HashData(msg, digest); check += digest[2];
            HMACSHA256.HashData(big.AsSpan(0, 32), msg, digest); check += digest[3];
        }
        // Large messages: the hash function's speed.
        for (int i = 0; i < 64; i++)
        {
            check += SHA256.HashData(big)[i & 31];
            check += SHA512.HashData(big)[i & 63];
            check += SHA384.HashData(big)[i % 48];
        }
        using (var inc = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            for (int i = 0; i < 200_000; i++) inc.AppendData(big, i & 1023, 97);
            check += inc.GetHashAndReset()[5];
        }
        using (var h = SHA256.Create())
            for (int i = 0; i < 50_000; i++) check += h.ComputeHash(big, i & 511, 200)[7];

        // AES: one-shot CBC over 1 MiB, many small ECB blocks, and CryptoStream.
        using (var aes = Aes.Create())
        {
            aes.Key = big[..32];
            aes.IV = big[32..48];
            for (int i = 0; i < 32; i++)
            {
                var ct = aes.EncryptCbc(big, aes.IV);
                var pt = aes.DecryptCbc(ct, aes.IV);
                check += ct[i] + pt[i * 3];
            }
            for (int i = 0; i < 100_000; i++)
            {
                var ct = aes.EncryptEcb(big.AsSpan(i & 4095, 16), PaddingMode.None);
                check += ct[i & 15];
            }
            for (int i = 0; i < 16; i++)
            {
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    cs.Write(big, 0, 256 * 1024);
                check += ms.ToArray()[i];
            }
        }

        // PBKDF2 (the cost is the iteration count) and random numbers (counted, not summed).
        for (int i = 0; i < 20; i++)
            check += Rfc2898DeriveBytes.Pbkdf2(big.AsSpan(0, 16), big.AsSpan(16, 16), 10_000, HashAlgorithmName.SHA256, 32)[i];
        var random = new byte[4096];
        for (int i = 0; i < 20_000; i++) { RandomNumberGenerator.Fill(random); check += random.Length; }
        for (int i = 0; i < 200_000; i++) check += RandomNumberGenerator.GetInt32(1, 2);

        Console.WriteLine(check);
    }
}
