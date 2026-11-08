// X509Certificate2.CreateFromEncryptedPem / CreateFromEncryptedPemFile: a certificate with a PKCS#8 private key
// encrypted under a password (the PEM label ENCRYPTED PRIVATE KEY), from strings and from files, with the key in
// the certificate's file or in a separate one, and the errors of a wrong password or a missing key.
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
const string EncryptedKeyPem = """
-----BEGIN ENCRYPTED PRIVATE KEY-----
MIIFLTBXBgkqhkiG9w0BBQ0wSjApBgkqhkiG9w0BBQwwHAQIGXtDvbWAUgECAggA
MAwGCCqGSIb3DQIJBQAwHQYJYIZIAWUDBAEqBBAUm8btKKKdVlR6VoV9BbPgBIIE
0ChEIy8xxEVPik7FC6gSeolHbILcLvTb09ZoFUf8EoEFYHuMgD+RFeTZGPhx4fSq
VEwZ9OQCz86hAzE0uu9NtYR16Q7eNKn9hpqJUeH/ArHPaGhfr2zCiMdHyi/LLPNV
8rNdPaWUhZEkdKDqTNTalMqnKFIxtifm2GV7kHXEc5UAmJOT11py8hzNzB6JdZwd
HH4l0LX9bwMCm3rh8yPP+LEHqNi8yv8eq0RPPXGgyevNTzRiff11kfAzZwkCwpUw
7yVDdhpB4/iJF6iH/kK9Am079jVX3bzK+N8fv2pBtZrkl+GjUGse6actAX6erIkK
oZfOEMvlBU5zcq5i123MoJSXskgnJ5hDb/NPuFxwxMjSLFl/JuGBxDQ0IZn+RvlM
soJJGP1qYU0TLfdzJn4I5D4PEeexvhLFzNFn2uxFx9nw+KEBDMfDkN6v5FEgfUld
7n7/ex054qeujKpAxy2QCiCYacO6SKgPUtYWRWJh7XPZ+NFrAtx8V2rpJwG1EccA
sOa4r8XUM4mpDHagql/Vt8mzdtf2+8gCoL54HBUZMoimy0F2p7ossKzgF33xIFBT
hb9OPHPZJ1y/sbKRIomaeEGrAKXuHpPNlEy2/EDo6c9/53TnDdG00W30Z4y1o6o8
tNPU1f7hGG9SKVnZi1Igu7MxL55qTNutFD4kh0lqmYuMAYCRomjp0naAhXKsM7iN
VHZD+sINtSZIi8eGo6fZK7p52NZnBRDrra22fFFFt5nZYMHH8axU6d/xBlmYxPkL
NeKVCdHGDlJzlxzTgUeoBfaXlWWvZmuaRkNKNAxMn32vYyIgZzomID3t2fnOvs5Z
UXMmfJTzjMseqTsHgpLYIfcSHscKoJBoJabpFGKVGudpRoP4L8J1LXifbhwPs65A
bxI3e18QGwAvJ/jIt+MbldMyFmQhIS9qBG/4Zlw4n3y3nNv9QoYliQFYbcxa0Ham
N8abyRBLd33VNXsPVtBdmfInh5aA26OtTeLnfLxAvLImogba8l3CWZRAKbOGRiwX
njsLHicwWFc539wjx2mzptYM0qB8phELT5VhTG0zZmBJX0kitizq84ylqgzYK0hE
nGYGgx/M0U9uKv6+NDrH6x4ZdY7v1PGJUF3NhzssmAVvVxR5tethWt/oJOBZ6E1R
DVP/LP6H/FK1lnt6pG5OQmEtB1f/t280++PNJERjTCR7GJ/k4QFKZ/mvXS4fpWNS
wybnWzOIDvbyG5EjuQJyeViAtZgdcJ698ZJD+hRFpmattP/RUJPb55QZJQlLnNGT
hUG0PoW46G9HZOwq7oaUGjbYL/+DS3PAjZbcPAyK3CjHUqG07pmzEXlI5sab/7Sl
vRBG4euK5+DsXa2df7DEi1ElE4fCGIpCqIZGu97dgtfshnxM2IEjOMqw9QwsMZIw
184C7tVDYMhsfDqmxzZLYw2+yxmrte/sEroftr2f3BwexmSsP+QUM580P1e/xVv1
BMOqpuNw+jtAEERL+XWkF7GtIwuabCzd6ekUMXXTHwicVKF4ULyPcGj68KEQEbql
m4TtdtK238RJK4BU7ZzJLOB/zqdP3FNVWo8S8ELsp646LLEa22wEK9yMPCO83W8m
gOkgLrX5TweZ15etcQOwkvprFYSfWkj/N+k7KGkCRHAt
-----END ENCRYPTED PRIVATE KEY-----
""";
const string PlainKeyPem = """
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
const string Password = "csharpc";

static void Try(string what, Func<object> action, bool typeOnly = false)
{
    try { Console.WriteLine(what + ": " + action()); }
    catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name + (typeOnly ? "" : ": " + e.Message)); }
}

static string Describe(X509Certificate2 cert)
{
    string signed = "";
    using (RSA? key = cert.GetRSAPrivateKey())
    using (RSA? pub = cert.GetRSAPublicKey())
        if (key != null && pub != null)
        {
            byte[] data = { 1, 2, 3, 4 };
            signed = " signature verifies " + pub.VerifyData(data, key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    return cert.Subject + " private key " + cert.HasPrivateKey + signed;
}

Try("string", () => Describe(X509Certificate2.CreateFromEncryptedPem(CertPem, EncryptedKeyPem, Password)));
Try("spans", () => Describe(X509Certificate2.CreateFromEncryptedPem(CertPem.AsSpan(), EncryptedKeyPem.AsSpan(), Password.ToCharArray())));
Try("wrong password", () => Describe(X509Certificate2.CreateFromEncryptedPem(CertPem, EncryptedKeyPem, "nope")).Length > 0);
Try("empty password", () => Describe(X509Certificate2.CreateFromEncryptedPem(CertPem, EncryptedKeyPem, "")).Length > 0);
Try("plain key, password given", () => Describe(X509Certificate2.CreateFromEncryptedPem(CertPem, "not a key", Password)).Length > 0);
Try("unencrypted key, password given", () => Describe(X509Certificate2.CreateFromEncryptedPem(CertPem, PlainKeyPem, Password)).Length > 0);
Try("no certificate", () => Describe(X509Certificate2.CreateFromEncryptedPem("nothing", EncryptedKeyPem, Password)).Length > 0);

string directory = Path.Combine(Path.GetTempPath(), "csharpc-encrypted-pem-" + Environment.ProcessId);
Directory.CreateDirectory(directory);
try
{
    string certFile = Path.Combine(directory, "cert.pem");
    string keyFile = Path.Combine(directory, "key.pem");
    string bothFile = Path.Combine(directory, "both.pem");
    File.WriteAllText(certFile, CertPem);
    File.WriteAllText(keyFile, EncryptedKeyPem);
    File.WriteAllText(bothFile, CertPem + "\n" + EncryptedKeyPem);

    Try("file, separate key", () => Describe(X509Certificate2.CreateFromEncryptedPemFile(certFile, Password, keyFile)));
    Try("file, span password", () => Describe(X509Certificate2.CreateFromEncryptedPemFile(certFile, Password.AsSpan(), keyFile)));
    Try("file, key in the certificate's file", () => Describe(X509Certificate2.CreateFromEncryptedPemFile(bothFile, Password)));
    Try("file, no key anywhere", () => Describe(X509Certificate2.CreateFromEncryptedPemFile(certFile, Password)));
    Try("file, wrong password", () => Describe(X509Certificate2.CreateFromEncryptedPemFile(certFile, "nope", keyFile)).Length > 0);
    Try("file, missing certificate file", () => { X509Certificate2.CreateFromEncryptedPemFile(Path.Combine(directory, "missing.pem"), Password, keyFile); return 0; }, typeOnly: true);
    Try("file, missing key file", () => { X509Certificate2.CreateFromEncryptedPemFile(certFile, Password, Path.Combine(directory, "missing.pem")); return 0; }, typeOnly: true);
    Try("file, null path", () => Describe(X509Certificate2.CreateFromEncryptedPemFile(null!, Password, keyFile)).Length > 0);
    Try("plain file, key in the certificate's file", () => Describe(X509Certificate2.CreateFromPemFile(bothFile)));
}
finally { Directory.Delete(directory, true); }
