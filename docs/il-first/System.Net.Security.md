# System.Net.Security (and the rest of System.Security.Cryptography)

A library served from the framework's IL: its row of the per-library table of [ADR 0023](../adr/0023-framework-il-first.md)
(columns explained there, under "Measurements"). Parts 2 and 3 of the crypto flip land together: on Linux, X.509, RSA, `Oid`,
`AsymmetricAlgorithm`, ECC, DSA, PKCS#7/OCSP and `SslStream`/`NegotiateAuthentication` move to the framework's IL in one step, because
the corelib's `SslStream` copy reached into its X.509 copy's internals and the framework's `ECDsa` needs the `AsymmetricAlgorithm` and
`Oid` members the copies lacked. Part 1 (hashes, HMAC, symmetric ciphers, RNG, KDFs) is [System.Security.Cryptography.md](System.Security.Cryptography.md).

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| System.Security.Cryptography parts 2 and 3 and System.Net.Security (Linux): X.509 (`X509Certificate2`, loaders, chains, stores, extensions, `CertificateRequest`), OCSP, PKCS#7/#12, `Oid`, `AsymmetricAlgorithm`, RSA, ECDsa/ECDH (explicit curves), DSA, X25519, ML-KEM/ML-DSA/SLH-DSA where OpenSSL has them, `SslStream` (client, server, SNI, ALPN, `SslStreamCertificateContext`, `CipherSuitesPolicy`, custom trust), `NegotiateAuthentication` (GSS-API); both assemblies are now the framework's IL whole on Linux | the X.509, RSA, SslStream, https and Negotiate samples and their neighbours (36 samples: 553, 1100-1103, 1231, 1262, 1266, 1270-1273, 625, 1250, 1160, 846, 621, 603, the part-1 crypto samples, the Uri samples, 1492 and the new 1520-1522), byte-identical to real .NET at -O0 and -O2 (72 runs), 9 via IL; 1101_sslstream_loopback unchanged. New samples 1520 (a CertificateRequest-made CA/intermediate/leaf chain, custom-root chain building, ECDSA TLS 1.2/1.3 with `SslStreamCertificateContext`, SNI-selected certificates, pinned cipher suites, name mismatch and untrusted root with .NET's exception messages), 1521 (`Oid` lookups with OpenSSL's names, `X500DistinguishedNameBuilder`, PEM and PKCS#12 round trips, PSS, RSA paddings, encrypted PKCS#8, ECDsa formats, ECDH, errors) and 1522 (Negotiate/Kerberos client and server over GSS), plus 1492 (ECC explicit parameters), all failing to compile before the flip (CSLLVM0001/0002). `tests/slicer.sh` 10 of 10 | `tools/il-first/bench/tls_handshake_throughput.cs` 2 005 672 to 4 515 016 B (2.25x); samples 553 413 024 to 959 808 B (2.3x), 1100 1 084 176 to 3 550 024 B (3.3x), 1101 2 085 376 to 4 579 480 B (2.2x), 1102 2 069 576 to 4 561 976 B (2.2x), 1103 4 839 280 to 7 073 568 B (1.5x), 1266 989 728 to 2 260 448 B (2.3x): the framework's X.509 and TLS stacks (chain engine, ASN.1 readers, PAL, `SafeHandle` marshalling) instead of the copies; `tests/size` programs unchanged (none reaches TLS or X.509) | bench 41.4 to 96.0 s user; samples 553 8.2 to 18.4 s, 1100 22.3 to 68.4 s, 1101 41.8 to 96.5 s, 1103 97.5 to 137.7 s | `tls_handshake_throughput` (200 TLS 1.3 and 200 TLS 1.2 handshakes, 256 MB in 16 KB writes, 20 000 64-byte round trips), median of 7 interleaved runs on a loaded shared host: copies 14 551 ms, IL **4 242 ms (0.29x)**: the framework's span BIOs and record handling instead of the copies' per-call buffering; checksum equal | `Net/SslStream.cs`, `Net/Tls.cs`, `Net/Negotiate.cs`, `Crypto/OpenSsl.cs`, `Crypto/Rsa.cs`, `Crypto/X509Certificates.cs`, `Crypto/X509Chain.cs`, `Crypto/X509Asn.cs`, `Cryptography.cs` (5 101 lines) no longer compiled on Linux, kept for macOS, Windows and WASI (#449); `CSHARPC_OPENSSL` no longer defined for Linux; contract added: `CryptoNative.Ssl.cs`, `CryptoNative.Bio.cs`, `NetSecurityNative.cs` (1 810 lines) on top of part 2's contract files | **Flipped on Linux.** macOS, Windows and WASI keep the copies (#449). General fix on the way: a native-contract export adapts integer widths, pointers and absent results to the P/Invoke's declared signature (the framework declares the shim's `void CryptoNative_ErrClearError` as returning `ulong`, `SafeHandle`s as `nint`). Security review below | `tls_handshake_throughput`: 4 515 072 / 6 129 792 B; compile cpu 96.6 / 28.4 s; run **4 455 / 4 712 ms (0.95x)** |

## The TLS and GSS contracts below the framework's IL (security review)

On Linux the framework's `System.Net.Security` IL reaches OpenSSL through `libSystem.Security.Cryptography.Native.OpenSsl`
(`Interop.Ssl`, `Interop.Crypto`, entry points `CryptoNative_Ssl*` and the TLS BIOs) and GSS-API through `libSystem.Net.Security.Native`
(`Interop.NetSecurityNative`, entry points `NetSecurityNative_*`). As for part 1, csharpc links neither shim (moving the runtime boundary is
the owner's decision, [21-ownership.md](../21-ownership.md)): their P/Invokes bind to C# contracts, `corelib/Crypto/CryptoNative.Ssl.cs`
(pal_ssl.c), `corelib/Crypto/CryptoNative.Bio.cs` (the managed-span and socket-replay BIOs of pal_bio.c) and
`corelib/Net/NetSecurityNative.cs` (pal_gssapi.c), ported function by function from the .NET 11 GA sources of the pinned framework. The
X.509, chain, OCSP, PKCS#7, ASN.1 and key entry points (part 2) are `corelib/Crypto/CryptoNative.{X509,X509Info,Asn1,Pkcs7,EvpPkey,Rsa,Ec,
EcImportExport,Dsa,X25519,Kem,Pqc,Misc,BioFile}.cs`. A P/Invoke into either shim whose entry point the contracts do not export is an
explicit "not supported by csharpc yet" diagnostic at compile time (CSLLVM0008), never a library search at run time. What was checked:

- **Certificate validation is exactly .NET's.** Nothing in the contracts accepts or rejects a certificate. The handshake's verify callback
  is the one the framework's IL passes (`SslCtxSetCertVerifyCallback` registers it with `SSL_CTX_set_cert_verify_callback`, unchanged);
  that callback builds the chain with the framework's `X509Chain` (IL) over the part-2 contract, and host-name matching is the framework's
  `X509Certificate2.MatchesHostname`/`CheckX509Hostname` (`X509_check_host` with `X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS`, as the shim).
  `SslSetVerifyPeer` sets `SSL_VERIFY_PEER` (plus `FAIL_IF_NO_PEER_CERT` when asked) with no callback of its own, as the shim; the
  client-certificate callback only pauses the handshake (`-1`, `SSL_ERROR_WANT_X509_LOOKUP`) for the framework to choose. Chain
  verification flags, revocation defaults and the custom-trust handling are the framework's IL.
- **SSL_CTX defaults are the shim's.** `TLS_method()`; `SSL_OP_NO_COMPRESSION | SSL_OP_CIPHER_SERVER_PREFERENCE`;
  `SSL_OP_ALLOW_CLIENT_RENEGOTIATION` on OpenSSL 3 (the framework polices `AllowRenegotiation`); the shim's default cipher string
  (`DOTNET_DEFAULT_CIPHERSTRING`, ECDHE with AES-GCM and AES-CBC-SHA2 only, byte for byte) unless `openssl.cnf` names a `CipherString`,
  detected as the shim detects it (`DetectCiphersuiteConfiguration`: the default, `ALL` and `RSA` cardinalities and `SSL_CTX_config
  ("system_default")`); protocol options computed from `SslProtocols` as the shim computes them, with OpenSSL's min/max reset only when
  protocols are set explicitly; security level 0 only for `EncryptionPolicy.AllowNoEncryption`/`NoEncryption`; session cache, OCSP
  status request (opportunistic, `TLSEXT_STATUSTYPE_ocsp`), ALPN (the select callback's argument is the shim's empty string), keylog,
  post-handshake auth and the TLS 1.3 renegotiation path (`SSL_verify_client_post_handshake`) as there.
- **Every native buffer is bounded by the caller's length.** The managed-span BIO copies at most the read window's remaining bytes and
  the write window's remaining capacity; output past the window goes to a spill buffer grown by doubling with the shim's overflow check
  before each size computation, and `BioDrainSpill` copies at most the caller's `dstLen`. The window and spill helpers accept only BIOs of
  the managed-span type (`BIO_method_type` against the index `BIO_get_new_index` gave this method), so a socket-replay BIO, whose context is
  smaller, is never read as one; the socket-replay accessors (`BioPeekTlsFrame`, `BioGetReplayPrefix`) are checked against their own type
  the same way (the shim reads their context unchecked). `BioPeekTlsFrame` reads at most `5 + 2^14 + 2048` bytes and refuses a record
  larger than its buffer before reading it. `SslGetFinished`/`SslGetPeerFinished` refuse a negative count (the shim would pass it on as a
  huge `size_t`); `SslStapleOcsp` copies exactly `len` bytes into an OpenSSL allocation and refuses a null buffer or a non-positive length
  (asserts in the shim); `GetDefaultSignatureAlgorithms` writes at most `*count` entries. GSS: input tokens and messages go to GSS with
  the caller's length; negative channel-binding sizes return `GSS_S_BAD_BINDINGS` before they become a `size_t`, as the shim; negative
  message lengths (asserts in the shim) are passed as empty buffers.
- **The OpenSSL error queue is handled as the shim handles it**: cleared before each operation whose failure the framework reports from
  the queue (`SslCreate`, `SslRead`/`SslWrite`/`SslHandshake`/`SslEncrypt`/`SslDecrypt`, `SslShutdown`, the certificate and key setters,
  `SslSetTlsExtHostName`, `SslSetSigalgs`, `SslCtxSetCiphers`, the BIO constructors ...), left alone where the shim leaves it (`SslGetError`,
  so the framework's `SslException` carries OpenSSL's reason), cleared after a tolerated failure (the OCSP status request, a malformed stapled
  response, the cipher detection). `errno` is saved around `SSL_get_error` and `SSL_peek` exactly where the shim saves it, so the
  framework's `SetLastError` P/Invokes read the failing call's `errno` on `SSL_ERROR_SYSCALL`.
- **No new parsing of untrusted data in C#.** TLS records, certificates, stapled OCSP responses (`d2i_OCSP_RESPONSE`, bounded by the
  length OpenSSL reports) and GSS tokens are parsed by OpenSSL and GSS, as under .NET. The one byte-level read is `BioPeekTlsFrame`'s
  5-byte record header, for its 16-bit length only, bounded as above.
- **GSS library loading** is the portable shim's: `libgssapi_krb5.so.2` is loaded on `NetSecurityNative_EnsureGssInitialized` and every
  function and OID is resolved then; a missing library answers `-1` (the framework reports it as .NET does), a library missing a function
  ends the process with the shim's message, as the shim's `abort()`.
- **Version handling.** OpenSSL 3 only (`libssl.so.3`/`libcrypto.so.3`); `SSL_get1_peer_certificate`, `SSL_CTX_set_ciphersuites` and
  `SSL_get_pending_cipher` exist in every 3.x, so the shim's `API_EXISTS` checks for them are constant here.

What the contracts do differently from the shim, by design: argument checks the shim makes only with `assert` return its failure value
instead of reaching OpenSSL or GSS; `GetDefaultSignatureAlgorithms` frees the two probe contexts (the shim leaks them); the BIO type check on
the socket-replay accessors. None of these changes what a program sees.

**Limits, as explicit diagnostics.** ENGINE-backed keys (`SafeEvpPKeyHandle.OpenPrivateKeyFromEngine`/`OpenPublicKeyFromEngine`) throw
`PlatformNotSupportedException` (OpenSSL 3 deprecates ENGINE, 4 removes it; the provider route, `OpenKeyFromProvider`, works); exporting
the parameters of a key wrapped from a legacy OpenSSL `EC_KEY` (`ECDsaOpenSsl(IntPtr)`, `ECDiffieHellmanOpenSsl` over such a handle) throws
`PlatformNotSupportedException` instead of the shim's legacy `EC_KEY` walk (keys csharpc creates or imports are provider keys and export
normally); `DOTNET_OPENSSL_MEMORY_DEBUG` (the shim's allocation tracking) throws `PlatformNotSupportedException` when set. macOS,
Windows and WASI keep the corelib's copies (#449). The pack's shims are not linked (`CSHARPC_LINUX_NATIVE_TLS=1` still links them, opt-in).
