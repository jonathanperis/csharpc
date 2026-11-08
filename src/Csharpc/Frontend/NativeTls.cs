namespace Csharpc.Frontend;

/// <summary>
/// The opt-in "native TLS" build mode for Windows and macOS (CSHARPC_WINDOWS_NATIVE_TLS=1 / CSHARPC_MACOS_NATIVE_TLS=1, never
/// the default; docs/adr/0022-tls.md, "Windows without OpenSSL" and "macOS without OpenSSL"): the corelib's OpenSSL-backed shadows for X.509, RSA and
/// SslStream are not mapped, so System.Net.Security and System.Security.Cryptography (and the facades in
/// front of them) are compiled from the shared framework's IL, whose own P/Invokes bind SSPI, CNG and crypt32 (Windows) or the
/// Apple crypto shim linked from the NativeAOT runtime pack (macOS).
/// It is the general IL-first mechanism (<see cref="IlFirst"/>, docs/adr/0023-framework-il-first.md) applied to a
/// hand-picked family of types: the hash, HMAC, AES and Negotiate shadows stay, so both assemblies are mixed.
/// </summary>
public static class NativeTls
{
    /// <summary>
    /// Whether the mode is on for a target: CSHARPC_WINDOWS_NATIVE_TLS=1 on Windows (SSPI, CNG, crypt32), CSHARPC_MACOS_NATIVE_TLS=1 on
    /// macOS (the runtime pack's Apple crypto shim over Security.framework; docs/adr/0022-tls.md, "macOS without OpenSSL").
    /// </summary>
    public static bool Enabled(bool windows, bool darwin = false) =>
        windows && Environment.GetEnvironmentVariable("CSHARPC_WINDOWS_NATIVE_TLS") is "1"
        || darwin && Environment.GetEnvironmentVariable("CSHARPC_MACOS_NATIVE_TLS") is "1";

    /// <summary>
    /// Real types whose shadows the mode drops: the OpenSSL-backed X.509 / RSA / SslStream family. Whole namespaces by
    /// prefix, single types by name.
    /// </summary>
    private static readonly string[] s_prefixes =
    [
        "System.Security.Cryptography.X509Certificates.",
        "System.Net.Security.Ssl",
        "System.Net.Security.AuthenticatedStream",
        "System.Security.Cryptography.RSA",
        "System.Security.Cryptography.Oid",
        "System.Security.Cryptography.Asymmetric",
        "System.Security.Cryptography.Cng",
        "System.Security.Cryptography.Csp",
        "System.Security.Cryptography.PbeParameters",
        "System.Security.Cryptography.AsnEncodedData",
        "System.Security.Cryptography.ECDsa",
        "System.Security.Cryptography.DSA",
        "System.Security.Cryptography.MLDsa",
        "System.Security.Cryptography.SlhDsa",
        "System.Security.Cryptography.CompositeMLDsa",
    ];

    /// <summary>Set once by the driver from <see cref="Enabled"/>: the mode is on for this compilation (the shims and the OpenSSL define depend on it).</summary>
    public static bool On { get; set; }

    /// <summary>Registers the family with the IL-first mechanism; the driver calls it for a Windows or macOS target with the mode on.</summary>
    public static void Apply() => IlFirst.UsePartial("native-tls", s_prefixes, ["System.Security.Cryptography", "System.Net.Security"]);
}
