namespace Csharpc.Frontend;

/// <summary>
/// The "native TLS" build mode (docs/adr/0022-tls.md, "Windows without OpenSSL" and "macOS without OpenSSL"): always on for macOS, as in
/// NativeAOT (#144); opt-in on Windows (CSHARPC_WINDOWS_NATIVE_TLS=1) and glibc Linux (CSHARPC_LINUX_NATIVE_TLS=1). The corelib's OpenSSL-backed shadows for X.509, RSA and
/// SslStream are not mapped, so System.Net.Security and System.Security.Cryptography (and the facades in
/// front of them) are compiled from the shared framework's IL, whose own P/Invokes bind SSPI, CNG and crypt32 (Windows), the
/// Apple crypto shim (macOS) or the OpenSSL shim (Linux) and libSystem.Native, linked from the NativeAOT runtime pack.
/// It is the general IL-first mechanism (<see cref="IlFirst"/>, docs/adr/0023-framework-il-first.md) applied to a
/// hand-picked family of types: the hash, HMAC, AES and Negotiate shadows stay, so both assemblies are mixed.
/// </summary>
public static class NativeTls
{
    /// <summary>
    /// Whether the mode is on for a target: CSHARPC_WINDOWS_NATIVE_TLS=1 on Windows (SSPI, CNG, crypt32); always on macOS (the runtime
    /// pack's Apple crypto shim over Security.framework, what NativeAOT links: no Homebrew OpenSSL; docs/adr/0022-tls.md, "macOS without OpenSSL"),
    /// CSHARPC_LINUX_NATIVE_TLS=1 on glibc Linux (the pack's OpenSSL shim, which loads libssl on first use; "Linux over the pack's shims").
    /// </summary>
    public static bool Enabled(bool windows, bool darwin = false, bool linux = false) =>
        windows && Environment.GetEnvironmentVariable("CSHARPC_WINDOWS_NATIVE_TLS") is "1"
        || darwin
        || linux && Environment.GetEnvironmentVariable("CSHARPC_LINUX_NATIVE_TLS") is "1";

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

    /// <summary>Registers the family with the IL-first mechanism; the driver calls it for a target with the mode on.</summary>
    public static void Apply() => IlFirst.UsePartial("native-tls", s_prefixes, Family);

    /// <summary>The two mixed assemblies of the mode.</summary>
    public static readonly string[] Family = ["System.Security.Cryptography", "System.Net.Security"];
}
