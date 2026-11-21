# System.Security.Cryptography

A library served from the framework's IL: its row of the per-library table of [ADR 0023](../adr/0023-framework-il-first.md)
(columns explained there, under "Measurements").

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| System.Security.Cryptography, part 1 (Linux): hashes (`SHA1`/`SHA256`/`SHA384`/`SHA512`/`MD5`, SHA-3, SHAKE, `IncrementalHash`, `HashAlgorithm`), HMAC (every `HMAC*`, KMAC), `SymmetricAlgorithm`, `Aes`, `TripleDES`, `CryptoStream`, `AesGcm`/`AesCcm`/`ChaCha20Poly1305`, `RandomNumberGenerator`, `Rfc2898DeriveBytes`, `HKDF`, `CryptographicOperations`, `HashAlgorithmName` (a mixed assembly: X.509, RSA and the other asymmetric types are still the corelib's, parts 2 and 3) | the hash, HMAC, AES, SHA-3 and hash-name samples (467, 721, 538, 452, 1264) and their neighbours (1250, 625, 603, 846, 621, 1160) plus the X.509, RSA, SslStream and https samples (553, 1100, 1101, 1102, 1103, 1231, 1262, 1266, 1270, 1272, 1273), 24 samples byte-identical to real .NET at -O0 and -O2 (48 runs), 7 via IL; on wasm32 the hash samples (and 603) pass at -O0 and -O2 (10 runs) on the kept copies. New samples 1490 (HMAC instances, `IncrementalHash.CreateHMAC`, SHA-3 HMAC, SHAKE, KMAC, HKDF, PBKDF2, `CryptographicOperations`, errors) and 1491 (AES modes and paddings, transforms, `CryptoStream`, `TripleDES`, `AesGcm`/`AesCcm`/`ChaCha20Poly1305`, key wrap, errors), both failing to compile before the flip (CSLLVM0001/0002). `tests/slicer.sh` 10 of 10 | the throughput program 831 608 to 900 784 B (+8.3 %: the framework's hash providers, `SafeHandle` marshalling and the OpenSSL contract); samples 467 318 272 to 331 888 B (+4.3 %), 538 993 088 to 1 055 512 B (+6.3 %), 1264 406 360 to 441 432 B (+8.6 %); `tests/size` programs unchanged except json, 1 706 024 to 1 724 920 B (+18 896 B: System.Text.Json calls `RandomNumberGenerator`, now the framework's over OpenSSL as under .NET; budget raised), wasm32 budgets unchanged | the throughput program 17.4 to 21.7 s user; samples within 2 % | `tools/il-first/bench/crypto_hashing_throughput.cs` (one-shot and incremental SHA-1/256/384/512 and MD5, HMAC-SHA256, AES-CBC/ECB and `CryptoStream`, PBKDF2, `RandomNumberGenerator`), median of 9 interleaved runs on a loaded shared host: shadow 3 138 ms, IL **1 461 ms (0.47x)**: OpenSSL's SHA/AES (SHA-NI, AES-NI) instead of managed hashing; checksum equal | the Linux branch of `Crypto/AesNative.cs` (68 lines) deleted; `Hashing.cs`, `Crypto/Sha3.cs`, `Crypto/Pbkdf2.cs`, `Crypto/SymmetricCrypto.cs`, `Crypto/RandomNumberGenerator.cs`, `Crypto/AesNative.cs` and `Crypto/HashAlgorithmName.cs` (6 267 lines) are no longer compiled on Linux and kept for macOS, Windows and WASI (#449); 1 167 lines added for the contract (`Crypto/CryptoNative.cs`, `Crypto/CryptoNative.Evp.cs`) | **Flipped on Linux.** macOS, Windows and WASI keep the corelib's copies (#449): the framework's IL there calls Apple's and Windows' crypto, which would change the backend. General fixes on the way: C# 11 ref fields lowered from IL (laid out as pointers; `ldfld`/`stfld` move the address; the framework's `ConcurrencyBlock.Scope` needs it). Security: below | `crypto_hashing_throughput`: 900 840 / 1 965 504 B; compile cpu 26.5 / 23.4 s; run **1 740 / 1 662 ms (1.05x)** |

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
