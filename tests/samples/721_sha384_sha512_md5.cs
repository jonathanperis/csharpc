// SHA-384, SHA-512 and MD5 (Roslyn's checksums and assembly hashes), through every API shape: Create,
// HashData, IncrementalHash, HMAC (128-byte blocks for the SHA-512 family), block boundaries.
using System;
using System.Security.Cryptography;
using System.Text;

byte[][] inputs =
{
    Array.Empty<byte>(),
    Encoding.ASCII.GetBytes("abc"),
    Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog"),
    new byte[111], new byte[112], new byte[127], new byte[128], new byte[129], new byte[1000],
};
for (int i = 3; i < inputs.Length; i++)
    for (int j = 0; j < inputs[i].Length; j++) inputs[i][j] = (byte)(j * 7 + i);

foreach (var input in inputs)
{
    Console.WriteLine($"len {input.Length}");
    Console.WriteLine($"  sha384 {Convert.ToHexString(SHA384.HashData(input))}");
    Console.WriteLine($"  sha512 {Convert.ToHexString(SHA512.HashData(input))}");
    Console.WriteLine($"  md5    {Convert.ToHexString(MD5.HashData(input))}");
}

using (var sha = SHA384.Create())
{
    Console.WriteLine($"SHA384.Create: {sha.HashSize} {Convert.ToHexString(sha.ComputeHash(inputs[2]))}");
    Console.WriteLine($"again: {Convert.ToHexString(sha.ComputeHash(inputs[1]))}");
}
using (var sha = SHA512.Create()) Console.WriteLine($"SHA512.Create: {sha.HashSize} {Convert.ToHexString(sha.ComputeHash(inputs[8], 3, 500))}");
using (var md5 = MD5.Create()) Console.WriteLine($"MD5.Create: {md5.HashSize} {Convert.ToHexString(md5.ComputeHash(inputs[8]))}");

foreach (var name in new[] { HashAlgorithmName.SHA384, HashAlgorithmName.SHA512, HashAlgorithmName.MD5 })
{
    using var incremental = IncrementalHash.CreateHash(name);
    for (int i = 0; i < inputs[8].Length; i += 37) incremental.AppendData(inputs[8], i, Math.Min(37, inputs[8].Length - i));
    Console.WriteLine($"incremental {name}: {incremental.HashLengthInBytes} {Convert.ToHexString(incremental.GetHashAndReset())}");
    using var hmac = IncrementalHash.CreateHMAC(name, Encoding.ASCII.GetBytes("key"));
    hmac.AppendData(inputs[2]);
    Console.WriteLine($"hmac {name}: {Convert.ToHexString(hmac.GetHashAndReset())}");
    using var longKey = IncrementalHash.CreateHMAC(name, inputs[7]);
    longKey.AppendData(inputs[1]);
    Console.WriteLine($"hmac long key {name}: {Convert.ToHexString(longKey.GetHashAndReset())}");
}
