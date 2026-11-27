// X.509, RSA, ECDSA and Oid members that the framework's IL provides on Linux (docs/il-first/System.Net.Security.md): Oid friendly
// names, X500DistinguishedNameBuilder and the name formats, a self-signed certificate with fixed validity in the past (so its time
// status is deterministic), PEM and PKCS#12 round trips with a password and PBE parameters, keys exported and imported as PKCS#8,
// SubjectPublicKeyInfo and encrypted PKCS#8, RSA padding modes, ECDsa signature formats, and the exceptions bad input raises.
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

Console.WriteLine("== oid");
foreach (string name in new[] { "sha256", "RSA", "ECC", "nistP256", "secP384r1", "Server Authentication", "Subject Alternative Name" })
{
    try
    {
        Oid oid = Oid.FromFriendlyName(name, OidGroup.All);
        Console.WriteLine($"{name} -> {oid.Value} ({oid.FriendlyName})");
    }
    catch (Exception e) { Console.WriteLine($"{name} -> {e.GetType().Name}: {e.Message}"); }
}
foreach (string value in new[] { "1.2.840.113549.1.1.11", "2.5.29.17", "1.3.6.1.5.5.7.3.2", "1.2.3.4.5" })
{
    string result;
    try { result = $"'{Oid.FromOidValue(value, OidGroup.All).FriendlyName}'"; }
    catch (Exception e) { result = e.GetType().Name + ": " + e.Message; }
    Console.WriteLine($"{value} -> {result}");
}
try { Oid.FromFriendlyName("no such name", OidGroup.All); }
catch (Exception e) { Console.WriteLine($"unknown -> {e.GetType().Name}: {e.Message}"); }

Console.WriteLine("== names");
var nameBuilder = new X500DistinguishedNameBuilder();
nameBuilder.AddCommonName("csharpc, \"quoted\"");
nameBuilder.AddOrganizationName("csharpc");
nameBuilder.AddCountryOrRegion("BR");
nameBuilder.AddEmailAddress("test@example.com");
X500DistinguishedName dn = nameBuilder.Build();
foreach (X500DistinguishedNameFlags flags in new[] { X500DistinguishedNameFlags.None, X500DistinguishedNameFlags.Reversed, X500DistinguishedNameFlags.UseSemicolons, X500DistinguishedNameFlags.UseNewLines })
    Console.WriteLine($"{flags}: {dn.Decode(flags).Replace("\n", "|")}");
foreach (X500RelativeDistinguishedName rdn in dn.EnumerateRelativeDistinguishedNames())
    Console.WriteLine($"  {rdn.GetSingleElementType().Value} = {rdn.GetSingleElementValue()}");
Console.WriteLine("raw: " + Convert.ToHexString(dn.RawData));

Console.WriteLine("== certificate");
using RSA rsa = RSA.Create(2048);
var req = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA384, RSASignaturePadding.Pss);
req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
var notBefore = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
using X509Certificate2 cert = req.CreateSelfSigned(notBefore, notBefore.AddYears(1));
Console.WriteLine($"subject: {cert.Subject}");
Console.WriteLine($"simple name: {cert.GetNameInfo(X509NameType.SimpleName, false)}; email: {cert.GetNameInfo(X509NameType.EmailName, false)}");
Console.WriteLine($"valid {cert.NotBefore.ToUniversalTime():u} .. {cert.NotAfter.ToUniversalTime():u}; sig {cert.SignatureAlgorithm.Value} {cert.SignatureAlgorithm.FriendlyName}");
Console.WriteLine($"key {cert.PublicKey.Oid.Value}, {cert.GetRSAPublicKey()!.KeySize} bits, private={cert.HasPrivateKey}");
using (var chain = new X509Chain())
{
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.VerificationTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    chain.ChainPolicy.CustomTrustStore.Add(cert);
    Console.WriteLine($"chain in 2030: {chain.Build(cert)} [{string.Join(",", chain.ChainStatus.Select(s => s.Status))}]");
    chain.ChainPolicy.VerificationTime = new DateTime(2001, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    Console.WriteLine($"chain in 2001: {chain.Build(cert)} [{string.Join(",", chain.ChainStatus.Select(s => s.Status))}]");
}

string pem = cert.ExportCertificatePem();
using (X509Certificate2 fromPem = X509Certificate2.CreateFromPem(pem))
    Console.WriteLine($"pem round trip: {pem.Split('\n')[0]} equal={fromPem.RawData.AsSpan().SequenceEqual(cert.RawData)} thumbprint equal={fromPem.Thumbprint == cert.Thumbprint}");

byte[] pfx = cert.Export(X509ContentType.Pfx, "secret");
using (X509Certificate2 fromPfx = X509CertificateLoader.LoadPkcs12(pfx, "secret", X509KeyStorageFlags.Exportable))
{
    Console.WriteLine($"pfx round trip: private={fromPfx.HasPrivateKey} same={fromPfx.Equals(cert)} content={X509Certificate2.GetCertContentType(pfx)}");
    byte[] data = Encoding.UTF8.GetBytes("signed by the certificate's key");
    byte[] sig = fromPfx.GetRSAPrivateKey()!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    Console.WriteLine($"pss signature {sig.Length} bytes, verifies={cert.GetRSAPublicKey()!.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)}");
}
try { X509CertificateLoader.LoadPkcs12(pfx, "wrong"); }
catch (Exception e) { Console.WriteLine($"wrong password: {e.GetType().Name}: {e.Message}"); }
try { X509CertificateLoader.LoadCertificate(new byte[] { 0x30, 0x03, 0x02, 0x01 }); }
catch (Exception e) { Console.WriteLine($"truncated der: {e.GetType().Name}: {e.Message}"); }

Console.WriteLine("== rsa");
byte[] message = Encoding.UTF8.GetBytes("csharpc");
foreach (RSAEncryptionPadding padding in new[] { RSAEncryptionPadding.Pkcs1, RSAEncryptionPadding.OaepSHA1, RSAEncryptionPadding.OaepSHA256, RSAEncryptionPadding.OaepSHA512 })
{
    byte[] encrypted = rsa.Encrypt(message, padding);
    Console.WriteLine($"{padding}: {encrypted.Length} bytes, decrypts to '{Encoding.UTF8.GetString(rsa.Decrypt(encrypted, padding))}'");
}
byte[] pkcs8 = rsa.ExportPkcs8PrivateKey();
byte[] spki = rsa.ExportSubjectPublicKeyInfo();
byte[] encryptedPkcs8 = rsa.ExportEncryptedPkcs8PrivateKey("pw", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000));
using (RSA imported = RSA.Create())
{
    imported.ImportEncryptedPkcs8PrivateKey("pw", encryptedPkcs8, out int read);
    Console.WriteLine($"encrypted pkcs8: read all={read == encryptedPkcs8.Length}, same key={imported.ExportPkcs8PrivateKey().AsSpan().SequenceEqual(pkcs8)}");
    imported.ImportSubjectPublicKeyInfo(spki, out _);
    Console.WriteLine($"spki import: {imported.KeySize} bits, public only: {Throws(() => imported.ExportParameters(true))}");
}
string privatePem = rsa.ExportRSAPrivateKeyPem();
Console.WriteLine("rsa pem: " + privatePem.Split('\n')[0]);
using (RSA small = RSA.Create(1024))
{
    try { small.Encrypt(new byte[200], RSAEncryptionPadding.OaepSHA256); Console.WriteLine("too long: no exception"); }
    catch (Exception e) { Console.WriteLine($"too long: {e.GetType().Name}: {e.Message}"); }
}
try { using RSA bad = RSA.Create(); bad.ImportPkcs8PrivateKey(new byte[] { 1, 2, 3 }, out _); }
catch (Exception e) { Console.WriteLine($"bad pkcs8: {e.GetType().Name}: {e.Message}"); }

Console.WriteLine("== ecdsa");
using ECDsa ec = ECDsa.Create(ECCurve.NamedCurves.nistP384);
byte[] ieee = ec.SignData(message, HashAlgorithmName.SHA384);
byte[] der = ec.SignData(message, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);
Console.WriteLine($"key {ec.KeySize} bits, ieee {ieee.Length} bytes, der starts 0x{der[0]:x2}, verify ieee={ec.VerifyData(message, ieee, HashAlgorithmName.SHA384)} der={ec.VerifyData(message, der, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence)}");
ECParameters p = ec.ExportParameters(false);
Console.WriteLine($"curve {p.Curve.Oid.FriendlyName} q.x {p.Q.X!.Length} bytes, d null={p.D is null}");
using (ECDiffieHellman a = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256), b = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
    Console.WriteLine("ecdh agrees: " + a.DeriveKeyFromHash(b.PublicKey, HashAlgorithmName.SHA256).AsSpan().SequenceEqual(b.DeriveKeyFromHash(a.PublicKey, HashAlgorithmName.SHA256)));
try { ECDsa.Create(ECCurve.CreateFromValue("1.2.3.4")); }
catch (Exception e) { Console.WriteLine($"unknown curve: {e.GetType().Name}: {e.Message}"); }

static string Throws(Action a)
{
    try { a(); return "no exception"; }
    catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
}
