# System.Net.Security (and the rest of System.Security.Cryptography)

A library served from the framework's IL: its row of the per-library table of [ADR 0023](../adr/0023-framework-il-first.md)
(columns explained there, under "Measurements"). Parts 2 and 3 of the crypto flip land together: on Linux, X.509, RSA, `Oid`,
`AsymmetricAlgorithm`, ECC, DSA, PKCS#7/OCSP and `SslStream`/`NegotiateAuthentication` move to the framework's IL in one step, because
the corelib's `SslStream` copy reached into its X.509 copy's internals and the framework's `ECDsa` needs the `AsymmetricAlgorithm` and
`Oid` members the copies lacked. Part 1 (hashes, HMAC, symmetric ciphers, RNG, KDFs) is [System.Security.Cryptography.md](System.Security.Cryptography.md).

ROW

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
