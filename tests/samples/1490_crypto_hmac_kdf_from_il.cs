// The keyed hashes, extendable-output functions and key derivations of System.Security.Cryptography as the framework's IL
// serves them over the OpenSSL contract (docs/il-first/System.Security.Cryptography.md): HMAC instances and their
// incremental use, IncrementalHash.CreateHMAC, SHA-3 and its HMACs, SHAKE, KMAC, HKDF, PBKDF2 through Rfc2898DeriveBytes,
// CryptographicOperations, and the argument and state errors their callers see.
using System;
using System.Security.Cryptography;
using System.Text;

class Program
{
    static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b);

    static void Try(string what, Action a)
    {
        try { a(); Console.WriteLine(what + ": ok"); }
        catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static void Main()
    {
        byte[] key = Encoding.ASCII.GetBytes("key-0123456789abcdef");
        byte[] data = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog");

        // HMAC instances: one-shot, TransformBlock/TransformFinalBlock, Key replacement, Initialize.
        using (var h = new HMACSHA256(key))
        {
            Console.WriteLine("HMACSHA256 " + Hex(h.ComputeHash(data)) + " " + h.HashSize + " " + h.InputBlockSize);
            h.TransformBlock(data, 0, 10, null, 0);
            h.TransformFinalBlock(data, 10, data.Length - 10);
            Console.WriteLine("blocks     " + Hex(h.Hash!));
            h.Key = new byte[100];
            Console.WriteLine("long key   " + Hex(h.ComputeHash(data)) + " " + h.Key.Length);
        }
        using (var h = new HMACSHA1(key)) Console.WriteLine("HMACSHA1 " + Hex(h.ComputeHash(data)));
        using (var h = new HMACSHA384(key)) Console.WriteLine("HMACSHA384 " + Hex(h.ComputeHash(data)));
        using (var h = new HMACSHA512(key)) Console.WriteLine("HMACSHA512 " + Hex(h.ComputeHash(data)));
        using (var h = new HMACMD5(key)) Console.WriteLine("HMACMD5 " + Hex(h.ComputeHash(data)));
        using (var h = new HMACSHA256()) Console.WriteLine("random key " + h.Key.Length);
        Console.WriteLine("static " + Hex(HMACSHA512.HashData(key, data))[..32]);

        // IncrementalHash: HMAC, GetCurrentHash, and the hash-length getters.
        using (var inc = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key))
        {
            inc.AppendData(data, 0, 20);
            Console.WriteLine("current " + Hex(inc.GetCurrentHash()));
            inc.AppendData(data.AsSpan(20));
            Console.WriteLine("final   " + Hex(inc.GetHashAndReset()) + " " + inc.HashLengthInBytes + " " + inc.AlgorithmName.Name);
        }

        // SHA-3, SHAKE and KMAC, where the platform's OpenSSL has them (3.0 and later do).
        Console.WriteLine("SHA3 supported " + SHA3_256.IsSupported + " " + HMACSHA3_256.IsSupported + " " + Shake128.IsSupported + " " + Kmac128.IsSupported);
        if (SHA3_256.IsSupported) Console.WriteLine("SHA3_384 " + Hex(SHA3_384.HashData(data)));
        if (HMACSHA3_256.IsSupported) Console.WriteLine("HMACSHA3_256 " + Hex(HMACSHA3_256.HashData(key, data)));
        if (Shake128.IsSupported)
        {
            Console.WriteLine("SHAKE128 " + Hex(Shake128.HashData(data, 40)));
            using var shake = new Shake256();
            shake.AppendData(data);
            Console.WriteLine("SHAKE256 " + Hex(shake.GetCurrentHash(16)) + " " + Hex(shake.GetHashAndReset(8)));
        }
        if (Kmac128.IsSupported)
        {
            Console.WriteLine("KMAC128 " + Hex(Kmac128.HashData(key, data, 32, Encoding.ASCII.GetBytes("custom"))));
            Console.WriteLine("KMACXof256 " + Hex(KmacXof256.HashData(key, data, 20)));
        }

        // HKDF and PBKDF2.
        byte[] prk = HKDF.Extract(HashAlgorithmName.SHA256, key, Encoding.ASCII.GetBytes("salt"));
        Console.WriteLine("HKDF prk " + Hex(prk));
        Console.WriteLine("HKDF okm " + Hex(HKDF.Expand(HashAlgorithmName.SHA256, prk, 42, Encoding.ASCII.GetBytes("info"))));
        Console.WriteLine("HKDF key " + Hex(HKDF.DeriveKey(HashAlgorithmName.SHA512, key, 20)));
#pragma warning disable SYSLIB0060 // the instance API is obsolete, and still served
        using (var pbkdf2 = new Rfc2898DeriveBytes(key, Encoding.ASCII.GetBytes("saltsalt"), 1000, HashAlgorithmName.SHA256))
        {
            Console.WriteLine("Rfc2898 " + Hex(pbkdf2.GetBytes(20)) + " " + Hex(pbkdf2.GetBytes(12)) + " " + pbkdf2.IterationCount);
        }
#pragma warning restore SYSLIB0060
        Console.WriteLine("Pbkdf2 " + Hex(Rfc2898DeriveBytes.Pbkdf2("password", Encoding.ASCII.GetBytes("saltsalt"), 2, HashAlgorithmName.SHA1, 20)));

        // CryptographicOperations.
        Console.WriteLine("HashData " + Hex(CryptographicOperations.HashData(HashAlgorithmName.SHA384, data))[..24]);
        Console.WriteLine("HmacData " + Hex(CryptographicOperations.HmacData(HashAlgorithmName.MD5, key, data)));
        Console.WriteLine("FixedTimeEquals " + CryptographicOperations.FixedTimeEquals(prk, prk) + " " + CryptographicOperations.FixedTimeEquals(prk, key));

        // Errors.
        Try("HKDF too long", () => HKDF.Expand(HashAlgorithmName.SHA256, prk, 255 * 32 + 1));
        Try("HKDF short prk", () => HKDF.Expand(HashAlgorithmName.SHA256, new byte[4], 10));
        Try("unknown hash", () => IncrementalHash.CreateHash(new HashAlgorithmName("SHA0")));
        Try("empty hash name", () => IncrementalHash.CreateHMAC(new HashAlgorithmName(""), key));
        Try("Pbkdf2 iterations", () => Rfc2898DeriveBytes.Pbkdf2(key, key, 0, HashAlgorithmName.SHA256, 10));
        Try("Pbkdf2 MD5", () => Rfc2898DeriveBytes.Pbkdf2(key, key, 10, HashAlgorithmName.MD5, 10));
        Try("destination too small", () => HMACSHA256.HashData(key, data, new byte[10]));
        var disposed = new HMACSHA256(key);
        disposed.Dispose();
        Try("disposed", () => disposed.ComputeHash(data));
    }
}
