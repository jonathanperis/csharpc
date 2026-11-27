// The shim's file and memory BIOs over libcrypto: the first part of pal_bio.c (dotnet/runtime, .NET 11 GA). The managed-span and
// socket-replay BIOs that SslStream uses are in CryptoNative.Bio.cs. See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern int BIO_gets(IntPtr b, byte* buf, int size);
        [DllImport(LibCrypto)] private static extern int BIO_read(IntPtr b, void* data, int dlen);
        [DllImport(LibCrypto)] private static extern int BIO_write(IntPtr b, void* data, int dlen);

        [RuntimeExport("CryptoNative_CreateMemoryBio", Trigger + "CryptoNative_CreateMemoryBio")]
        public static IntPtr CreateMemoryBio()
        {
            ERR_clear_error();
            return BIO_new(BIO_s_mem());
        }

        [RuntimeExport("CryptoNative_BioNewFile", Trigger + "CryptoNative_BioNewFile")]
        public static IntPtr BioNewFile(byte* filename, byte* mode)
        {
            ERR_clear_error();
            return BIO_new_file(filename, mode);
        }

        [RuntimeExport("CryptoNative_BioDestroy", Trigger + "CryptoNative_BioDestroy")]
        public static int BioDestroy(IntPtr a) => BIO_free(a);

        // BIO_gets, BIO_read and BIO_write take the caller's length as their bound.
        [RuntimeExport("CryptoNative_BioGets", Trigger + "CryptoNative_BioGets")]
        public static int BioGets(IntPtr b, byte* buf, int size)
        {
            ERR_clear_error();
            return BIO_gets(b, buf, size);
        }

        [RuntimeExport("CryptoNative_BioRead", Trigger + "CryptoNative_BioRead")]
        public static int BioRead(IntPtr b, void* buf, int len)
        {
            ERR_clear_error();
            return BIO_read(b, buf, len);
        }

        [RuntimeExport("CryptoNative_BioWrite", Trigger + "CryptoNative_BioWrite")]
        public static int BioWrite(IntPtr b, void* buf, int len)
        {
            ERR_clear_error();
            return BIO_write(b, buf, len);
        }

        // BIO_get_mem_data(bio, NULL): BIO_ctrl(BIO_CTRL_INFO = 3).
        [RuntimeExport("CryptoNative_GetMemoryBioSize", Trigger + "CryptoNative_GetMemoryBioSize")]
        public static int GetMemoryBioSize(IntPtr bio) => (int)BIO_ctrl(bio, 3, 0, null);
    }
}
#endif
