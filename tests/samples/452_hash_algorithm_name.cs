// HashAlgorithmName and RSAParameters: plain value types of System.Security.Cryptography that Roslyn
// passes around (checksums, strong-name keys) without necessarily hashing anything.
using System.Security.Cryptography;

HashAlgorithmName[] names = [HashAlgorithmName.MD5, HashAlgorithmName.SHA1, HashAlgorithmName.SHA256, HashAlgorithmName.SHA384,
    HashAlgorithmName.SHA512, HashAlgorithmName.SHA3_256, HashAlgorithmName.SHA3_384, HashAlgorithmName.SHA3_512];
foreach (var name in names) Console.WriteLine($"{name} {name.Name} {name == HashAlgorithmName.SHA256} {name.Equals((object)HashAlgorithmName.SHA1)}");

var custom = new HashAlgorithmName("SHA256");
var none = default(HashAlgorithmName);
Console.WriteLine($"{custom == HashAlgorithmName.SHA256} {custom != HashAlgorithmName.SHA256} {custom.GetHashCode() == "SHA256".GetHashCode()}");
Console.WriteLine($"[{none}] {none.Name is null} {none.GetHashCode()} {none == new HashAlgorithmName(null)} {none.Equals("SHA1")}");
Console.WriteLine(new HashAlgorithmName("sha256") == HashAlgorithmName.SHA256);

foreach (var oid in new[] { "1.2.840.113549.2.5", "1.3.14.3.2.26", "2.16.840.1.101.3.4.2.1", "2.16.840.1.101.3.4.2.2",
             "2.16.840.1.101.3.4.2.3", "2.16.840.1.101.3.4.2.8", "2.16.840.1.101.3.4.2.9", "2.16.840.1.101.3.4.2.10", "1.2.3" })
{
    var found = HashAlgorithmName.TryFromOid(oid, out var byOid);
    Console.WriteLine($"{oid}: {found} {byOid}");
}
Console.WriteLine(HashAlgorithmName.FromOid("2.16.840.1.101.3.4.2.1"));
try { HashAlgorithmName.FromOid("1.2.3"); }
catch (CryptographicException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { HashAlgorithmName.TryFromOid(null!, out _); }
catch (ArgumentNullException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

var counts = new Dictionary<HashAlgorithmName, int>();
foreach (var name in new[] { HashAlgorithmName.SHA1, HashAlgorithmName.SHA256, new HashAlgorithmName("SHA1") })
    counts[name] = counts.GetValueOrDefault(name) + 1;
foreach (var (name, count) in counts) Console.WriteLine($"{name}: {count}");

var key = new RSAParameters { Exponent = [1, 0, 1], Modulus = new byte[256] };
var copy = key;
copy.D = [9];
Console.WriteLine($"{key.Exponent.Length} {key.Modulus.Length} {key.D is null} {copy.D.Length} {key.P is null} {key.InverseQ is null}");
object boxed = key;
Console.WriteLine(((RSAParameters)boxed).Exponent[2]);
