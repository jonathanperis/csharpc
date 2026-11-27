// ECDsa and ECDiffieHellman as the framework's IL serves them over the OpenSSL contract (docs/il-first/System.Security.Cryptography.md):
// key generation on named curves, ExportParameters and ExportExplicitParameters of an ordinary (provider) key, re-import from the explicit
// curve, signatures across the two keys, key agreement, and the argument errors. Keys are random: only what the curve fixes is printed.
using System;
using System.Security.Cryptography;
using System.Text;

class Program
{
    static string Hex(byte[]? b) => b == null ? "null" : Convert.ToHexString(b);

    static void Try(string what, Action a)
    {
        try { a(); Console.WriteLine(what + ": ok"); }
        catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static void Main()
    {
        byte[] data = Encoding.ASCII.GetBytes("explicit curves");
        foreach (var curve in new[] { ECCurve.NamedCurves.nistP256, ECCurve.NamedCurves.nistP384, ECCurve.NamedCurves.nistP521 })
        {
            using var ecdsa = ECDsa.Create(curve);
            Console.WriteLine(curve.Oid.FriendlyName + " key size " + ecdsa.KeySize + " signature " + ecdsa.GetMaxSignatureSize(DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

            ECParameters named = ecdsa.ExportParameters(true);
            Console.WriteLine("  named: " + named.Curve.IsNamed + " " + named.Curve.Oid.Value + " Q " + named.Q.X!.Length + "/" + named.Q.Y!.Length + " D " + named.D!.Length);

            ECParameters explicitParameters = ecdsa.ExportExplicitParameters(true);
            ECCurve c = explicitParameters.Curve;
            Console.WriteLine("  explicit: " + c.IsExplicit + " " + c.CurveType + " " + c.IsPrime);
            Console.WriteLine("  prime " + Hex(c.Prime));
            Console.WriteLine("  a " + Hex(c.A));
            Console.WriteLine("  b " + Hex(c.B));
            Console.WriteLine("  G " + Hex(c.G.X) + " " + Hex(c.G.Y));
            Console.WriteLine("  order " + Hex(c.Order) + " cofactor " + Hex(c.Cofactor));
            Console.WriteLine("  seed " + Hex(c.Seed));
            Console.WriteLine("  same key: " + (Hex(explicitParameters.D) == Hex(named.D)) + " " + (Hex(explicitParameters.Q.X) == Hex(named.Q.X)));

            using var fromExplicit = ECDsa.Create();
            fromExplicit.ImportParameters(explicitParameters);
            byte[] signature = ecdsa.SignData(data, HashAlgorithmName.SHA256);
            Console.WriteLine("  cross verify: " + fromExplicit.VerifyData(data, signature, HashAlgorithmName.SHA256)
                + " " + ecdsa.VerifyData(data, fromExplicit.SignData(data, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256)
                + " tampered " + fromExplicit.VerifyData(Encoding.ASCII.GetBytes("other"), signature, HashAlgorithmName.SHA256));
            Console.WriteLine("  public only: " + Hex(fromExplicit.ExportExplicitParameters(false).D) + " " + fromExplicit.ExportExplicitParameters(false).Curve.IsExplicit);
        }

        using (var alice = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
        using (var bob = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
        {
            byte[] ab = alice.DeriveKeyFromHash(bob.PublicKey, HashAlgorithmName.SHA256);
            byte[] ba = bob.DeriveKeyFromHash(alice.PublicKey, HashAlgorithmName.SHA256);
            Console.WriteLine("ECDH agree " + (Hex(ab) == Hex(ba)) + " " + ab.Length + " raw " + alice.DeriveRawSecretAgreement(bob.PublicKey).Length);
            ECParameters e = alice.ExportExplicitParameters(false);
            Console.WriteLine("ECDH explicit " + e.Curve.IsExplicit + " " + e.Curve.CurveType + " " + Hex(e.Curve.Order));
            byte[] spki = alice.ExportSubjectPublicKeyInfo();
            using var imported = ECDiffieHellman.Create();
            imported.ImportSubjectPublicKeyInfo(spki, out int read);
            Console.WriteLine("SPKI " + spki.Length + " " + read + " " + imported.KeySize);
        }

        Try("bad curve", () => ECDsa.Create(new ECCurve { CurveType = ECCurve.ECCurveType.PrimeShortWeierstrass }));
        Try("unknown named curve", () => ECDsa.Create(ECCurve.CreateFromValue("1.2.3.4")));
        Try("mismatched key", () =>
        {
            using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var p = k.ExportParameters(false);
            p.Q.X![0] ^= 1;
            k.ImportParameters(p);
        });
    }
}
