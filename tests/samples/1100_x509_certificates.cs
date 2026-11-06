// X.509 certificates over OpenSSL on Linux (docs/adr/0022-tls.md): a fixed self-signed RSA certificate (CN=localhost, SAN
// DNS:localhost IP:127.0.0.1) loaded from a PKCS#12 blob (X509CertificateLoader and the obsolete constructors), from DER and
// from PEM (certificate and private key): subject and issuer as .NET prints them, serial number, thumbprint and other hashes,
// validity (UTC), key algorithms, name info, equality, the RSA keys (a signature made with the private key verifies with the
// public one), content-type detection, the error types and messages, certificate collections and X509Chain (UntrustedRoot of
// a self-signed certificate, trusted through CustomRootTrust or AllowUnknownCertificateAuthority).
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

const string PfxBase64 = "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";
const string CertPem = """
-----BEGIN CERTIFICATE-----
MIIDnzCCAoegAwIBAgIUbu0PR9SfskftQcq7sDizhgJZFbIwDQYJKoZIhvcNAQEL
BQAwOTELMAkGA1UEBhMCQlIxFjAUBgNVBAoMDWNzaGFycGMgdGVzdHMxEjAQBgNV
BAMMCWxvY2FsaG9zdDAgFw0yNjExMDYwNzQ0MjVaGA8yMTI2MTAxMzA3NDQyNVow
OTELMAkGA1UEBhMCQlIxFjAUBgNVBAoMDWNzaGFycGMgdGVzdHMxEjAQBgNVBAMM
CWxvY2FsaG9zdDCCASIwDQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAN4nrCcx
ckuMZB6U2/WUy7AFpOGQAeui+iB0o2gtEqYgDMmfpOVpQ+K7z137IUJB9Q6sN2h7
ubiHvRXRNnqAnivzZl9zp//6mg6HSZCEJl1WZrMdM2DNqjHmMGMsGXilGmW+Hj+G
lEQLDmXW9sceLjITCn499zB02C+QvhnCMCAvbSyOQuD1wQFbVy2wwAQUkki4OlyX
hdT1hZdVrUSbpzWLAwnZuNY5J7FZ6pEAn1+zZ8uvLnMyY07QCsMsFcZcTH1T8HeZ
x7IaNSkVxAwbG2YTj7txXxGatrby9VHxLH25JjptyRZem3wa2gjNmC91AsmQVnDy
2acHDOfKP7eBi2MCAwEAAaOBnDCBmTAdBgNVHQ4EFgQUUCWHYtlg/XDSyoeQTh9p
fXluvDAwHwYDVR0jBBgwFoAUUCWHYtlg/XDSyoeQTh9pfXluvDAwGgYDVR0RBBMw
EYIJbG9jYWxob3N0hwR/AAABMAwGA1UdEwEB/wQCMAAwDgYDVR0PAQH/BAQDAgWg
MB0GA1UdJQQWMBQGCCsGAQUFBwMBBggrBgEFBQcDAjANBgkqhkiG9w0BAQsFAAOC
AQEA23HEdU+sJqAnM0sql9EQC3xU1cqh9Nyt5ZkHs8Rlyemm0DZB0QQuyZKjZ5yd
pnWP1KVV6UgXU1RTgB52wUTW8OUT3bquJuI+dRrINwLfEILaXD8Sxa9kKNF4A+TJ
Lrc9izQx+cipnd/QcJc7SwzLP2m/3130naJV/QKU2O9Ql8+y9cvs6Qb4DBFIJo0A
9FbLUpsKTuan4UQ41QC9NTI+QuoG6nKiqnXYJFgtyPh5XUDg0sAd6Is518rS7gmg
DeTDBfIMJV2HqnfOAfgwpXptU0QfSMOT/6tdNWLomeg58znhEeTffoAs9y4VVt06
l+BCAVAwomd2on7KKpLZ8UpIrw==
-----END CERTIFICATE-----
""";
const string KeyPem = """
-----BEGIN PRIVATE KEY-----
MIIEvAIBADANBgkqhkiG9w0BAQEFAASCBKYwggSiAgEAAoIBAQDeJ6wnMXJLjGQe
lNv1lMuwBaThkAHrovogdKNoLRKmIAzJn6TlaUPiu89d+yFCQfUOrDdoe7m4h70V
0TZ6gJ4r82Zfc6f/+poOh0mQhCZdVmazHTNgzaox5jBjLBl4pRplvh4/hpRECw5l
1vbHHi4yEwp+PfcwdNgvkL4ZwjAgL20sjkLg9cEBW1ctsMAEFJJIuDpcl4XU9YWX
Va1Em6c1iwMJ2bjWOSexWeqRAJ9fs2fLry5zMmNO0ArDLBXGXEx9U/B3mceyGjUp
FcQMGxtmE4+7cV8Rmra28vVR8Sx9uSY6bckWXpt8GtoIzZgvdQLJkFZw8tmnBwzn
yj+3gYtjAgMBAAECggEAAWIDRC5ttcFxcVd7NVLa+3K17VLax9MmadLs/8Jko3fO
jttQQr6nbj2M853QJYUzfdZRpfo7wic1AxzBSBuNhRNHzEkq7LGc/VCLIa+wJB10
khue8ve96ki+xlz6DhbqJ92YxDmDbSN7JZsLw0IFQ+F75AwLvybaSRTxDQsjydZY
rB10j0eCcemm0E13bZcsZO+hWkay21VIFVRhDXCnN7kC5kVeoaebjHZDkpAl7Pkp
DcPXGTvmv6HvXFjOOXp4viMZ40JXbkyNl1AwarjNxkMOwRKu4NfSPZW+cCck74kU
uGXvkvNen2MXN+ZwXUhmUxJeTJnOKt31zMsryp6FKQKBgQD57amKx8R61m1YRk+D
he9PXQFK0j42amgAmp7OIv4dyRIABBVUoGAe0OHeAYL9j+q9KbLqp3F1JcC7mkdP
SgbcdZvLJ6ne/7+LHBuYwqNum4V4I1Rb04BI2RN4qMvE0QcB/Px1Dgs8q9X7yJor
YFc4ZEVnBddixk9jyNfraZLwtQKBgQDjjUijy7HSFGXQvB0vOBznaa6gaVtRjDuX
IWsPWt/4aLaYyzVoMwIk88VVGnBEUvEzF2YiEnwQIUekJZeHtFgblvuDqb0I9LPX
dzn8wbGxgFBQ03vMpYErz7f35kctCaYO31m9I6xbreAVmSLLQv9wEKZwxHqTO3qc
fGBRFtfStwKBgCg6piCtmSXAoCcmYiQkiem3ab7Waqctp4H5YXiCz26R7/DKt3hA
vFgZfVHT4nbYFIvQS3lT7IJTylsoMTRFESepUqN3xfMcLYPByXtkURQ7ulclMK37
NFoFfwnb+bi7gKpEQ4q/8PUaH76AgrhasI9mLHmxygKfMyY3rPG6s6lFAoGAHI3+
L4/AILyuiiMRBnvkJKX+Y+LUsscGM1DePm8I7AM35rSZXxoHituMR8GplOlTmzW0
e1IWf2+trcuJ2F2pMM0hgsQqFJWge8laXX1w7vVLHtlrD6M1Mmo+i7058Uzkqbkd
ab6wQlDvbcZpt1gQ9d8hA8WkHBW6rShubb6QFsECgYBkeIAFdmsmErXjd0HhjDQU
5mQHkhbczqIcL52WPe9jr1ihoUZJAlHrYI+xVy2YpJ2FAsr2SOrWP5gYrSC728RS
iFzNhPHT0y8KfsPjMuTettjzuyzg2ajED5pE7THTJ43w/IO/jLeVW57VPOgzcXNU
JufM4rXn1yD6/5tyNkSkDQ==
-----END PRIVATE KEY-----
""";

byte[] pfx = Convert.FromBase64String(PfxBase64);
#pragma warning disable SYSLIB0057
X509Certificate2 cert = X509CertificateLoader.LoadPkcs12(pfx, "csharpc");
Console.WriteLine("subject: " + cert.Subject);
Console.WriteLine("issuer: " + cert.Issuer);
Console.WriteLine("subject name: " + cert.SubjectName.Name + " | " + cert.SubjectName.Decode(X500DistinguishedNameFlags.None));
Console.WriteLine("serial: " + cert.SerialNumber + " " + cert.GetSerialNumberString() + " " + Convert.ToHexString(cert.GetSerialNumber()) + " " + cert.SerialNumberBytes.Length);
Console.WriteLine("thumbprint: " + cert.Thumbprint + " " + cert.GetCertHashString() + " " + cert.GetCertHashString(HashAlgorithmName.SHA256));
Console.WriteLine("hash: " + Convert.ToHexString(cert.GetCertHash()) + " " + cert.GetCertHash(HashAlgorithmName.SHA384).Length);
Console.WriteLine("not before: " + cert.NotBefore.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") + " kind " + cert.NotBefore.Kind);
Console.WriteLine("not after: " + cert.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss"));
Console.WriteLine("has private key: " + cert.HasPrivateKey + ", version " + cert.Version + ", archived " + cert.Archived + ", friendly '" + cert.FriendlyName + "'");
Console.WriteLine("signature: " + cert.SignatureAlgorithm.FriendlyName + " " + cert.SignatureAlgorithm.Value);
Console.WriteLine("public key: " + cert.PublicKey.Oid.FriendlyName + " " + cert.PublicKey.Oid.Value + " " + cert.PublicKey.EncodedKeyValue.RawData.Length + " " + Convert.ToHexString(cert.PublicKey.EncodedParameters.RawData));
Console.WriteLine("key algorithm: " + cert.GetKeyAlgorithm() + " " + cert.GetKeyAlgorithmParametersString() + " " + cert.GetPublicKeyString().Substring(0, 20) + " " + cert.GetPublicKey().Length);
Console.WriteLine("name info: " + cert.GetNameInfo(X509NameType.SimpleName, false) + " " + cert.GetNameInfo(X509NameType.DnsName, false) + " " + cert.GetNameInfo(X509NameType.DnsFromAlternativeName, false) + " " + cert.GetNameInfo(X509NameType.SimpleName, true));
Console.WriteLine("format: " + cert.GetFormat() + ", raw " + cert.RawData.Length + " " + cert.GetRawCertData().Length + " " + cert.RawDataMemory.Length + " " + cert.GetRawCertDataString().Length);
Console.WriteLine("type: " + cert.GetType() + " " + new X509Certificate(cert.RawData).GetType());
Console.WriteLine("content type: " + X509Certificate2.GetCertContentType(pfx) + " " + X509Certificate2.GetCertContentType(cert.RawData));

// the same certificate from other sources
X509Certificate2 fromDer = X509CertificateLoader.LoadCertificate(cert.RawData);
X509Certificate2 fromPem = X509Certificate2.CreateFromPem(CertPem, KeyPem);
X509Certificate2 fromCtor = new X509Certificate2(pfx, "csharpc");
X509Certificate2 fromPemOnly = X509Certificate2.CreateFromPem(CertPem);
Console.WriteLine("der: private key " + fromDer.HasPrivateKey + ", equals " + fromDer.Equals(cert) + ", hash codes " + (fromDer.GetHashCode() == cert.GetHashCode()));
Console.WriteLine("pem: private key " + fromPem.HasPrivateKey + ", equals " + fromPem.Equals(cert) + ", " + fromPem.Thumbprint);
Console.WriteLine("pem without key: private key " + fromPemOnly.HasPrivateKey + ", " + fromPemOnly.Subject);
Console.WriteLine("ctor: " + fromCtor.Subject + " " + fromCtor.HasPrivateKey);
Console.WriteLine("exported cert: " + cert.Export(X509ContentType.Cert).Length + " " + cert.ExportCertificatePem().Split('\n')[0] + " " + new X509Certificate(cert).Equals(cert) + " " + cert.Equals((object)"x"));
string tmp = Path.Combine(Path.GetTempPath(), "csharpc_x509_1100.pfx");
File.WriteAllBytes(tmp, pfx);
Console.WriteLine("from file: " + X509CertificateLoader.LoadPkcs12FromFile(tmp, "csharpc").Subject + " " + new X509Certificate2(tmp, "csharpc").Thumbprint + " " + X509Certificate2.GetCertContentType(tmp));
File.Delete(tmp);

// the keys
using (RSA priv = cert.GetRSAPrivateKey()!)
using (RSA pub = cert.GetRSAPublicKey()!)
{
    byte[] data = Encoding.UTF8.GetBytes("csharpc and certificates");
    byte[] sig = priv.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    Console.WriteLine("rsa: " + priv.KeySize + " " + pub.KeySize + " signature " + sig.Length + " verifies " + pub.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
        + " tampered " + pub.VerifyData(data, new byte[sig.Length], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    byte[] cipher = pub.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
    Console.WriteLine("rsa encrypt: " + cipher.Length + " " + Encoding.UTF8.GetString(priv.Decrypt(cipher, RSAEncryptionPadding.OaepSHA256)));
    Console.WriteLine("public parameters equal: " + priv.ExportParameters(false).Modulus!.AsSpan().SequenceEqual(pub.ExportParameters(false).Modulus));
}
Console.WriteLine("no private key for the loader: " + (fromDer.GetRSAPrivateKey() == null));

// certificate collections
var collection = new X509Certificate2Collection();
collection.Add(cert);
collection.AddRange(new[] { fromDer, fromPem });
Console.WriteLine("collection: " + collection.Count + " " + collection.Contains(fromCtor) + " " + collection.IndexOf(fromPem) + " " + collection[0].Subject);
foreach (X509Certificate2 c in collection) Console.Write(c.HasPrivateKey ? "K" : "-");
Console.WriteLine();
Console.WriteLine("find: " + collection.Find(X509FindType.FindByThumbprint, cert.Thumbprint.ToLowerInvariant(), false).Count
    + " " + collection.Find(X509FindType.FindBySubjectName, "localhost", false).Count + " " + collection.Find(X509FindType.FindBySubjectName, "nowhere", false).Count
    + " " + collection.Find(X509FindType.FindBySerialNumber, cert.SerialNumber, false).Count);
collection.Remove(fromDer);
Console.WriteLine("after remove: " + collection.Count);

// chains
using (var chain = new X509Chain())
{
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    Console.WriteLine("chain: " + chain.Build(cert) + " elements " + chain.ChainElements.Count);
    foreach (X509ChainStatus s in chain.ChainStatus) Console.WriteLine("  status " + s.Status + " | " + s.StatusInformation);
    foreach (X509ChainElement e in chain.ChainElements)
    {
        Console.WriteLine("  element " + e.Certificate.Subject + " " + e.ChainElementStatus.Length);
        foreach (X509ChainStatus s in e.ChainElementStatus) Console.WriteLine("    " + s.Status + " | " + s.StatusInformation);
    }
}
using (var chain = new X509Chain())
{
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    chain.ChainPolicy.CustomTrustStore.Add(fromDer);
    Console.WriteLine("custom root trust: " + chain.Build(cert) + " " + chain.ChainStatus.Length + " " + chain.ChainElements.Count);
}
using (var chain = new X509Chain())
{
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
    Console.WriteLine("allow unknown CA: " + chain.Build(cert) + " " + chain.ChainStatus.Length);
}
using (var chain = new X509Chain())
{
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    chain.ChainPolicy.CustomTrustStore.Add(fromDer);
    chain.ChainPolicy.VerificationTime = cert.NotAfter.AddDays(1);
    Console.WriteLine("expired: " + chain.Build(cert));
    foreach (X509ChainStatus s in chain.ChainStatus) Console.WriteLine("  status " + s.Status + " | " + s.StatusInformation);
}

// errors
void Try(string what, Action action)
{
    try { action(); Console.WriteLine(what + ": no exception"); }
    catch (Exception e) { Console.WriteLine(what + ": " + (e is CryptographicException ? "CryptographicException" : e.GetType().FullName) + ": " + e.Message); }
}
Try("wrong password", () => X509CertificateLoader.LoadPkcs12(pfx, "wrong"));
Try("garbage", () => X509CertificateLoader.LoadCertificate(new byte[] { 1, 2, 3 }));
Try("garbage ctor", () => new X509Certificate2(new byte[] { 1, 2, 3 }));
Try("missing file", () => new X509Certificate2("/nonexistent/csharpc.pfx"));
Try("null file name", () => new X509Certificate2((string)null!));
Try("null data", () => X509CertificateLoader.LoadCertificate((byte[])null!));
Try("bad key PEM", () => X509Certificate2.CreateFromPem(CertPem, "not a key"));
Try("disposed", () => { var c = X509CertificateLoader.LoadCertificate(cert.RawData); c.Dispose(); _ = c.Subject; });
Try("empty certificate", () => _ = new X509Certificate().Subject);
