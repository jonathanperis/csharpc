// The shim's remaining helpers: IsSignatureAlgorithmAvailable (openssl.c) and the allocation tracking of memory_debug.c (dotnet/runtime,
// .NET 11 GA). See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern IntPtr EVP_SIGNATURE_fetch(IntPtr ctx, byte* algorithm, byte* properties);
        [DllImport(LibCrypto)] private static extern void EVP_SIGNATURE_free(IntPtr signature);

        [RuntimeExport("CryptoNative_IsSignatureAlgorithmAvailable", Trigger + "CryptoNative_IsSignatureAlgorithmAvailable")]
        public static int IsSignatureAlgorithmAvailable(byte* algorithm)
        {
            // Message signing (OpenSSL 3.5) is what makes the algorithm usable, as in the shim.
            if (!HaveMessageApi()) return 0;
            IntPtr sigAlg = EVP_SIGNATURE_fetch(IntPtr.Zero, algorithm, null);
            if (sigAlg == IntPtr.Zero) return 0;
            EVP_SIGNATURE_free(sigAlg);
            return 1;
        }

        // The shim tracks OpenSSL's allocations only when DOTNET_OPENSSL_MEMORY_DEBUG enables it (it installs its own allocator before
        // OpenSSL starts). csharpc does not: without the variable these are the shim's no-ops; with it, an explicit refusal.
        private static void RefuseMemoryDebug()
        {
            string value = Environment.GetEnvironmentVariable("DOTNET_OPENSSL_MEMORY_DEBUG");
            if (!string.IsNullOrEmpty(value) && value != "0")
                throw new PlatformNotSupportedException("DOTNET_OPENSSL_MEMORY_DEBUG (OpenSSL allocation tracking) is not supported by csharpc yet.");
        }

        [RuntimeExport("CryptoNative_GetMemoryUse", Trigger + "CryptoNative_GetMemoryUse")]
        public static void GetMemoryUse(ulong* totalUsed, ulong* allocationCount)
        {
            RefuseMemoryDebug();
            *totalUsed = 0;
            *allocationCount = 0;
        }

        [RuntimeExport("CryptoNative_EnableMemoryTracking", Trigger + "CryptoNative_EnableMemoryTracking")]
        public static void EnableMemoryTracking(int enable) => RefuseMemoryDebug();

        [RuntimeExport("CryptoNative_ForEachTrackedAllocation", Trigger + "CryptoNative_ForEachTrackedAllocation")]
        public static void ForEachTrackedAllocation(IntPtr callback, IntPtr ctx) => RefuseMemoryDebug();
    }
}
#endif
