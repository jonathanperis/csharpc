# System.Security.Cryptography

A library served from the framework's IL: its row of the per-library table of [ADR 0023](../adr/0023-framework-il-first.md)
(columns explained there, under "Measurements").

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| System.Security.Cryptography, part 1 (Linux): hashes (`SHA1`/`SHA256`/`SHA384`/`SHA512`/`MD5`, SHA-3, SHAKE, `IncrementalHash`, `HashAlgorithm`), HMAC (every `HMAC*`, KMAC), `SymmetricAlgorithm`, `Aes`, `TripleDES`, `CryptoStream`, `AesGcm`/`AesCcm`/`ChaCha20Poly1305`, `RandomNumberGenerator`, `Rfc2898DeriveBytes`, `HKDF`, `CryptographicOperations`, `HashAlgorithmName` (a mixed assembly: X.509, RSA and the other asymmetric types are still the corelib's, parts 2 and 3) | TODO | TODO | TODO | `tools/il-first/bench/crypto_hashing_throughput.cs`: TODO | TODO | **Flipped on Linux.** macOS, Windows and WASI keep the corelib's copies (#449): the framework's IL there calls Apple's and Windows' crypto, which would change the backend. | TODO |

## The OpenSSL contract below the framework's IL (security review)

On Linux the framework's `System.Security.Cryptography` IL reaches OpenSSL through `libSystem.Security.Cryptography.Native.OpenSsl`
(`Interop.Crypto.*`, entry points `CryptoNative_*`). csharpc does not link that shim (moving the runtime boundary is the owner's decision,
[21-ownership.md](../21-ownership.md)); its P/Invokes bind to a C# contract over OpenSSL 3's `libcrypto.so.3` and `libssl.so.3`,
`corelib/Crypto/CryptoNative*.cs`, the same mechanism as the file system's and the sockets' `libSystem.Native` contracts. The contract is
the shim's C (dotnet/runtime `src/native/libs/System.Security.Cryptography.Native`, the .NET 11 GA source of the pinned framework),
function by function. The framework's IL above it (the hash providers, `SafeHandle` reference counting, argument validation, the
`CryptographicException` mapping, the one-shot and streaming APIs) runs unchanged. A P/Invoke into the shim whose entry point the contract
does not export is an explicit "not supported by csharpc yet" diagnostic at compile time (CSLLVM0008, the call ends the program with it),
never a library search that fails at run time. What was checked:

- **No new parsing of untrusted data in C#.** Every decoder is OpenSSL's, called exactly as the shim calls it; the contract moves pointers
  and lengths. The only text it builds is the KMAC/HMAC/KDF parameter names (constants) and, for the OCSP cache (part 2), a file name from
  two hashes.
- **Every native buffer is bounded by the caller's length.** Where the shim's signature carries a length, the contract checks it before
  the call (negative lengths are rejected before they become a `size_t`; a null buffer with a non-zero length is refused), and passes it to
  OpenSSL as the bound (`EVP_MAC_final`, `EVP_DigestFinalXOF`, `HMAC_Final`'s in/out length, `EVP_KDF_derive`, `ERR_error_string_n`). Where
  the shim's signature has no length (`EvpDigestFinalEx`, the cipher updates, `i2d_*` encoders), the framework sizes the buffer from the
  value OpenSSL reported for it (`EvpMdSize`, the block size, the `*DerSize` call before each encode), as with the shim; the contract
  does not widen any of those contracts.
- **The OpenSSL error queue is cleared exactly where the shim clears it** (`ERR_clear_error` before each operation whose failure the
  framework reports from the queue, after the legacy provider load, after a tolerated failure), so the framework's
  `CreateOpenSslCryptographicException` reads the same error, with the same text, as on .NET. Allocation failures OpenSSL does not report
  itself are pushed as `ERR_R_MALLOC_FAILURE`, as the shim pushes them (the framework turns them into `OutOfMemoryException`).
- **Key material.** HMAC keys copied for OpenSSL 3.0.0-3.0.2 (whose `EVP_MAC_init` cannot re-use the key on reset) are freed with
  `CRYPTO_clear_free`, as there; the zero-length XOF workaround cleanses its one-byte buffer. Nothing else copies key material.
- **Version handling.** OpenSSL 3 only (`libcrypto.so.3`, as the corelib's own bindings and every distribution .NET 11 supports); later
  3.x functions (`EVP_DigestSqueeze`, RC2 and ChaCha20-Poly1305 when compiled out) are looked up by name and treated as missing when absent,
  as the shim's `API_EXISTS` does; MD5 is fetched with `-fips` so a FIPS-enforcing system still hashes with it; initialization is the
  shim's `OPENSSL_init_ssl` (configuration first, again without it if `openssl.cnf` is malformed, never unloaded at exit).

What the contract does differently from the shim, by design: argument checks the shim makes only with `assert` (null contexts, negative
lengths) return the shim's failure value instead of reaching OpenSSL.
