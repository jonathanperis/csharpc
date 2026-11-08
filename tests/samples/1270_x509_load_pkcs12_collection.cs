// X509CertificateLoader.LoadPkcs12Collection / LoadPkcs12CollectionFromFile: every certificate of a PFX (a leaf with its
// private key and a CA), in the file's order, from bytes, from a span and from a file; the wrong password and a missing file.
using System.Security.Cryptography.X509Certificates;

const string PfxBase64 = "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";
const string Password = "csharpc";

static void Try(string what, Func<object> action, bool typeOnly = false)
{
    try { Console.WriteLine(what + ": " + action()); }
    catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name + (typeOnly ? "" : ": " + e.Message)); }
}

static string Describe(X509Certificate2Collection collection)
{
    var parts = new List<string>();
    foreach (X509Certificate2 cert in collection) parts.Add(cert.Subject + " key=" + cert.HasPrivateKey);
    return collection.Count + " [" + string.Join("; ", parts) + "]";
}

byte[] pfx = Convert.FromBase64String(PfxBase64);
Try("bytes", () => Describe(X509CertificateLoader.LoadPkcs12Collection(pfx, Password)));
Try("span", () => Describe(X509CertificateLoader.LoadPkcs12Collection(pfx.AsSpan(), Password.AsSpan())));
Try("wrong password", () => Describe(X509CertificateLoader.LoadPkcs12Collection(pfx, "nope")));
Try("null data", () => Describe(X509CertificateLoader.LoadPkcs12Collection((byte[])null!, Password)));
Try("single certificate", () => X509CertificateLoader.LoadPkcs12(pfx, Password).Subject);

string path = Path.Combine(Path.GetTempPath(), "csharpc-pfx-collection-" + Environment.ProcessId + ".pfx");
File.WriteAllBytes(path, pfx);
try
{
    Try("file", () => Describe(X509CertificateLoader.LoadPkcs12CollectionFromFile(path, Password)));
    Try("file, wrong password", () => Describe(X509CertificateLoader.LoadPkcs12CollectionFromFile(path, "nope")));
    Try("missing file", () => Describe(X509CertificateLoader.LoadPkcs12CollectionFromFile(path + ".missing", Password)), typeOnly: true);
    Try("null path", () => Describe(X509CertificateLoader.LoadPkcs12CollectionFromFile(null!, Password)));
}
finally { File.Delete(path); }
