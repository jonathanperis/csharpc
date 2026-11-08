// SHA-3 (FIPS 202): SHA3_256, SHA3_384 and SHA3_512 (IsSupported, Create, one-shot HashData, incremental use) and the same
// algorithms through IncrementalHash and HMAC, over messages around the 136, 104 and 72-byte block boundaries.
using System.Security.Cryptography;
using System.Text;

Console.WriteLine($"supported: {SHA3_256.IsSupported} {SHA3_384.IsSupported} {SHA3_512.IsSupported}");

static byte[] Message(int length)
{
    var data = new byte[length];
    for (int i = 0; i < length; i++) data[i] = (byte)(i * 7 + 3);
    return data;
}

byte[] abc = Encoding.ASCII.GetBytes("abc");
Console.WriteLine(Convert.ToHexString(SHA3_256.HashData(Array.Empty<byte>())));
Console.WriteLine(Convert.ToHexString(SHA3_256.HashData(abc)));
Console.WriteLine(Convert.ToHexString(SHA3_384.HashData(abc)));
Console.WriteLine(Convert.ToHexString(SHA3_512.HashData(abc)));

foreach (int length in new[] { 0, 1, 71, 72, 73, 103, 104, 105, 135, 136, 137, 200, 271, 272, 273, 1000 })
{
    byte[] data = Message(length);
    Console.WriteLine($"{length}: {Convert.ToHexString(SHA3_256.HashData(data))[..16]} {Convert.ToHexString(SHA3_384.HashData(data))[..16]} {Convert.ToHexString(SHA3_512.HashData(data))[..16]}");
}

using (SHA3_256 sha = SHA3_256.Create())
{
    byte[] data = Message(500);
    Console.WriteLine($"create: {sha.HashSize} {sha.GetType().BaseType?.Name}");
    byte[] once = sha.ComputeHash(data);
    Console.WriteLine(Convert.ToHexString(once));
    Console.WriteLine("again: " + Convert.ToHexString(sha.ComputeHash(data)).Equals(Convert.ToHexString(once)));
    // Blocks of different sizes, one at a time.
    sha.Initialize();
    sha.TransformBlock(data, 0, 100, null, 0);
    sha.TransformBlock(data, 100, 36, null, 0);
    sha.TransformBlock(data, 136, 200, null, 0);
    sha.TransformFinalBlock(data, 336, 164);
    Console.WriteLine("blocks: " + Convert.ToHexString(sha.Hash!).Equals(Convert.ToHexString(once)));
    Console.WriteLine("stream: " + Convert.ToHexString(sha.ComputeHash(new MemoryStream(data))).Equals(Convert.ToHexString(once)));
}
using (SHA3_512 sha = SHA3_512.Create()) Console.WriteLine($"create 512: {sha.HashSize} {Convert.ToHexString(sha.ComputeHash(abc))[..32]}");
using (SHA3_384 sha = SHA3_384.Create()) Console.WriteLine($"create 384: {sha.HashSize} {Convert.ToHexString(sha.ComputeHash(abc))[..32]}");

Span<byte> destination = stackalloc byte[32];
Console.WriteLine($"span: {SHA3_256.HashData(abc, destination)} {Convert.ToHexString(destination)[..16]}");
Console.WriteLine($"try: {SHA3_256.TryHashData(abc, destination[..31], out int written)} {written} {SHA3_256.TryHashData(abc, destination, out written)} {written}");
try { SHA3_256.HashData((byte[])null!); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { SHA3_256.HashData(abc, destination[..10]); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
Console.WriteLine($"consts: {SHA3_256.HashSizeInBits} {SHA3_256.HashSizeInBytes} {SHA3_384.HashSizeInBytes} {SHA3_512.HashSizeInBytes}");

foreach (var name in new[] { HashAlgorithmName.SHA3_256, HashAlgorithmName.SHA3_384, HashAlgorithmName.SHA3_512 })
{
    using var hash = IncrementalHash.CreateHash(name);
    hash.AppendData(Message(300));
    hash.AppendData(abc);
    Console.WriteLine($"incremental {name}: {hash.HashLengthInBytes} {Convert.ToHexString(hash.GetHashAndReset())[..24]}");
    hash.AppendData(abc);
    Console.WriteLine($"  reset: {Convert.ToHexString(hash.GetHashAndReset())[..24]}");

    // HMAC pads the key to the algorithm's block size: 136, 104 and 72 bytes.
    foreach (int keyLength in new[] { 5, 72, 104, 136, 200 })
    {
        using var hmac = IncrementalHash.CreateHMAC(name, Message(keyLength));
        hmac.AppendData(abc);
        Console.WriteLine($"  hmac key {keyLength}: {Convert.ToHexString(hmac.GetHashAndReset())[..24]}");
    }
}
