// SHA-1 / SHA-256 through HashAlgorithm, the static HashData helpers and IncrementalHash (Roslyn
// checksums source text and PE content with them). Digests are fixed by FIPS 180-4.
using System.Security.Cryptography;
using System.Text;

string[] inputs = ["", "abc", "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", new string('a', 1000)];
foreach (var input in inputs)
{
    var bytes = Encoding.UTF8.GetBytes(input);
    Console.WriteLine($"{input.Length}: {Convert.ToHexString(SHA1.HashData(bytes))} {Convert.ToHexString(SHA256.HashData(bytes))}");
}

using (var sha1 = SHA1.Create())
{
    var data = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog");
    Console.WriteLine($"{sha1.HashSize} {Convert.ToHexString(sha1.ComputeHash(data))}");
    Console.WriteLine(Convert.ToHexString(sha1.ComputeHash(data, 4, 5)));
    Console.WriteLine(Convert.ToHexString(sha1.ComputeHash(new MemoryStream(data))));
    Console.WriteLine(Convert.ToHexString(sha1.Hash!));
}
using (var sha256 = SHA256.Create())
{
    var block = new byte[200];
    for (var i = 0; i < block.Length; i++) block[i] = (byte)i;
    sha256.TransformBlock(block, 0, 64, null, 0);
    sha256.TransformBlock(block, 64, 70, block, 64);
    sha256.TransformFinalBlock(block, 134, 66);
    Console.WriteLine($"{sha256.HashSize} {Convert.ToHexString(sha256.Hash!)} {Convert.ToHexString(SHA256.HashData(block))}");
}

using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
Console.WriteLine($"{incremental.AlgorithmName} {incremental.HashLengthInBytes}");
incremental.AppendData(Encoding.ASCII.GetBytes("ab"));
incremental.AppendData(Encoding.ASCII.GetBytes("xcx"), 1, 1);
Console.WriteLine(Convert.ToHexString(incremental.GetHashAndReset()));
incremental.AppendData("abc"u8);
Console.WriteLine(Convert.ToHexString(incremental.GetHashAndReset()));
using var incrementalSha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
Console.WriteLine($"{incrementalSha1.HashLengthInBytes} {Convert.ToHexString(incrementalSha1.GetHashAndReset())}");
try { IncrementalHash.CreateHash(new HashAlgorithmName("FOO")); }
catch (CryptographicException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { IncrementalHash.CreateHash(new HashAlgorithmName("")); }
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { SHA1.HashData((byte[])null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
