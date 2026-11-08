// A program that has NegotiateAuthentication, X509Certificate2.CreateFromEncryptedPemFile, SHA3_256.Create, AppDomain.DomainUnload,
// Process.WaitForExitAsync and BigInteger.GetByteCount in code it does not run (a database driver's GSS / SCRAM-PLUS / Kerberos
// paths) compiles: the members exist, so no "has no implementation in the csharpc runtime yet" error stops the build. (The
// authentication itself is an explicit NotSupportedException from the constructor when it does run: csharpc has no
// GSSAPI / SSPI binding yet, a difference from .NET that the sample does not exercise.)
using System.Diagnostics;
using System.Net.Security;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

static async Task<byte[]?> Authenticate(string host, byte[] challenge, CancellationToken token)
{
    var options = new NegotiateAuthenticationClientOptions { TargetName = "postgres/" + host };
    using var negotiate = new NegotiateAuthentication(options);
    byte[]? blob = negotiate.GetOutgoingBlob(challenge, out NegotiateAuthenticationStatusCode status);
    if (status == NegotiateAuthenticationStatusCode.Completed && negotiate.IsAuthenticated && negotiate.IsEncrypted && negotiate.IsSigned)
    {
        var writer = new System.Buffers.ArrayBufferWriter<byte>();
        negotiate.Wrap(blob!, writer, true, out bool encrypted);
        negotiate.Unwrap(writer.WrittenSpan, writer, out bool wasEncrypted);
        var buffer = new byte[16];
        negotiate.UnwrapInPlace(buffer, out int offset, out int length, out bool inPlaceEncrypted);
    }
    using var kinit = Process.Start("klist")!;
    await kinit.WaitForExitAsync(token);
    return blob;
}

static X509Certificate2 LoadClientCertificate(string certFile, string password, string keyFile) =>
    X509Certificate2.CreateFromEncryptedPemFile(certFile, password, keyFile);

static byte[] ChannelBindingHash(byte[] certificate) => SHA3_256.Create().ComputeHash(certificate);

static int Digits(BigInteger value) => value.GetByteCount() + value.GetByteCount(true);

static void Subscribe() => AppDomain.CurrentDomain.DomainUnload += (_, _) => { };

// Nothing above runs: the arguments of the program are never that many.
if (args.Length > 1000)
{
    await Authenticate("host", Array.Empty<byte>(), default);
    LoadClientCertificate("a", "b", "c");
    ChannelBindingHash(Array.Empty<byte>());
    Digits(BigInteger.One);
    Subscribe();
}
Console.WriteLine("compiled");
