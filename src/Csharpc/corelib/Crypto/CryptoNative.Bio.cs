// The shim's TLS BIOs over libcrypto: the managed-span BIO and the socket-replay BIO of pal_bio.c (dotnet/runtime, .NET 11 GA), which the
// framework's SslStream (SslStreamPal.Unix, TlsSocketSession) plugs into its SSL objects. The file and memory BIOs are in CryptoNative.BioFile.cs.
// See CryptoNative.cs for the contract and CryptoNative.Ssl.cs for the SSL functions that drive these BIOs.
//
// Security (docs/il-first/System.Net.Security.md): every copy is bounded by the window the framework passed (ptr, length) or by the length
// OpenSSL asked for; the spill buffer grows with an overflow check before each size computation, as in the shim; the window and spill helpers
// accept only BIOs of the managed-span type (BIO_method_type against the index this BIO_METHOD registered), so a socket-replay BIO, whose context
// is smaller, is never read as a managed-span one. The only bytes looked at are the 5-byte TLS record header in BioPeekTlsFrame, read for its
// length alone and checked against the buffer before any copy (the record itself is parsed by OpenSSL, as under .NET).
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        // ---- libcrypto BIO_METHOD API (OpenSSL 3) ---------------------------------------------------------------------------
        [DllImport(LibCrypto)] private static extern int BIO_get_new_index();
        [DllImport(LibCrypto)] private static extern IntPtr BIO_meth_new(int type, byte* name);
        [DllImport(LibCrypto)] private static extern void BIO_meth_free(IntPtr biom);
        [DllImport(LibCrypto)] private static extern int BIO_meth_set_write(IntPtr biom, delegate* unmanaged<IntPtr, byte*, int, int> write);
        [DllImport(LibCrypto)] private static extern int BIO_meth_set_read(IntPtr biom, delegate* unmanaged<IntPtr, byte*, int, int> read);
        [DllImport(LibCrypto)] private static extern int BIO_meth_set_ctrl(IntPtr biom, delegate* unmanaged<IntPtr, int, long, void*, long> ctrl);
        [DllImport(LibCrypto)] private static extern int BIO_meth_set_create(IntPtr biom, delegate* unmanaged<IntPtr, int> create);
        [DllImport(LibCrypto)] private static extern int BIO_meth_set_destroy(IntPtr biom, delegate* unmanaged<IntPtr, int> destroy);
        [DllImport(LibCrypto)] private static extern void* BIO_get_data(IntPtr a);
        [DllImport(LibCrypto)] private static extern void BIO_set_data(IntPtr a, void* ptr);
        [DllImport(LibCrypto)] private static extern void BIO_set_init(IntPtr a, int init);
        [DllImport(LibCrypto)] private static extern void BIO_set_flags(IntPtr b, int flags);
        [DllImport(LibCrypto)] private static extern void BIO_clear_flags(IntPtr b, int flags);
        [DllImport(LibCrypto)] private static extern int BIO_method_type(IntPtr b);
        [DllImport(LibCrypto)] internal static extern int BIO_up_ref(IntPtr a);

        [DllImport("libc", EntryPoint = "recv")] private static extern nint libc_recv(int fd, void* buf, nuint len, int flags);
        [DllImport("libc", EntryPoint = "send")] private static extern nint libc_send(int fd, void* buf, nuint len, int flags);

        private const int BIO_TYPE_SOURCE_SINK = 0x0400;
        private const int BIO_CTRL_FLUSH = 11;
        private const int BIO_FLAGS_READ = 0x01, BIO_FLAGS_WRITE = 0x02, BIO_FLAGS_RWS = 0x07, BIO_FLAGS_SHOULD_RETRY = 0x08;
        private const int EINTR = 4, EBADF = 9, EAGAIN = 11 /* = EWOULDBLOCK on Linux */, MSG_NOSIGNAL = 0x4000;
        private const int ManagedSpanSpillInitial = 4096;

        // The BIO_clear_retry_flags / BIO_set_retry_read / BIO_set_retry_write macros.
        private static void BioClearRetryFlags(IntPtr b) => BIO_clear_flags(b, BIO_FLAGS_RWS | BIO_FLAGS_SHOULD_RETRY);
        private static void BioSetRetryRead(IntPtr b) => BIO_set_flags(b, BIO_FLAGS_READ | BIO_FLAGS_SHOULD_RETRY);
        private static void BioSetRetryWrite(IntPtr b) => BIO_set_flags(b, BIO_FLAGS_WRITE | BIO_FLAGS_SHOULD_RETRY);

        private static int Errno => Marshal.GetLastSystemError();

        // ---- managed-span BIO ---------------------------------------------------------------------------------------------
        // The caller's read window (input ciphertext), write window (output ciphertext) and the heap spill for output past the window.
        [StructLayout(LayoutKind.Sequential)]
        private struct ManagedSpanBioCtx
        {
            public byte* ReadPtr;
            public int ReadLen;
            public int ReadPos;
            public byte* WritePtr;
            public int WriteCapacity;
            public int WritePos;
            public byte* SpillBuf;
            public int SpillCapacity;
            public int SpillLen;
        }

        private static readonly object s_bioMethodLock = new object();
        private static IntPtr s_managedSpanBioMethod, s_socketReplayBioMethod;
        private static volatile bool s_managedSpanBioInitDone, s_socketReplayBioInitDone;
        private static int s_managedSpanBioType, s_socketReplayBioType;

        // Only BIOs of the managed-span type: any other context (the socket-replay one is smaller) would be read and written past its end.
        private static bool TryGetManagedSpanBioCtx(IntPtr bio, out ManagedSpanBioCtx* ctx)
        {
            ctx = null;
            if (bio == IntPtr.Zero || BIO_method_type(bio) != s_managedSpanBioType) return false;
            ctx = (ManagedSpanBioCtx*)BIO_get_data(bio);
            return ctx != null;
        }

        [UnmanagedCallersOnly]
        private static int ManagedSpanBioRead(IntPtr bio, byte* buf, int len)
        {
            if (bio == IntPtr.Zero || buf == null || len <= 0) return 0;
            BioClearRetryFlags(bio);
            var ctx = (ManagedSpanBioCtx*)BIO_get_data(bio);
            if (ctx == null) return -1;
            int available = ctx->ReadLen - ctx->ReadPos;
            if (available <= 0 || ctx->ReadPtr == null)
            {
                BioSetRetryRead(bio);
                return -1;
            }
            int toCopy = len < available ? len : available;
            Buffer.MemoryCopy(ctx->ReadPtr + ctx->ReadPos, buf, toCopy, toCopy);
            ctx->ReadPos += toCopy;
            return toCopy;
        }

        private static bool ManagedSpanBioGrowSpill(ManagedSpanBioCtx* ctx, int needed)
        {
            if (ctx->SpillCapacity >= needed) return true;
            int newCap = ctx->SpillCapacity > 0 ? ctx->SpillCapacity : ManagedSpanSpillInitial;
            while (newCap < needed)
            {
                if (newCap > int.MaxValue / 2)
                {
                    newCap = needed;
                    break;
                }
                newCap *= 2;
            }
            var newBuf = (byte*)NativeMemory.Realloc(ctx->SpillBuf, (nuint)newCap);
            if (newBuf == null) return false;
            ctx->SpillBuf = newBuf;
            ctx->SpillCapacity = newCap;
            return true;
        }

        [UnmanagedCallersOnly]
        private static int ManagedSpanBioWrite(IntPtr bio, byte* buf, int len)
        {
            if (bio == IntPtr.Zero || buf == null || len < 0) return 0;
            BioClearRetryFlags(bio);
            if (len == 0) return 0;
            var ctx = (ManagedSpanBioCtx*)BIO_get_data(bio);
            if (ctx == null) return -1;
            int remaining = len;
            byte* src = buf;
            if (ctx->WritePtr != null)
            {
                int windowAvail = ctx->WriteCapacity - ctx->WritePos;
                if (windowAvail > 0)
                {
                    int toCopy = remaining < windowAvail ? remaining : windowAvail;
                    Buffer.MemoryCopy(src, ctx->WritePtr + ctx->WritePos, toCopy, toCopy);
                    ctx->WritePos += toCopy;
                    src += toCopy;
                    remaining -= toCopy;
                }
            }
            if (remaining > 0)
            {
                // Both non-negative: the sum must fit before it sizes the buffer.
                if (remaining > int.MaxValue - ctx->SpillLen) return -1;
                int needed = ctx->SpillLen + remaining;
                if (!ManagedSpanBioGrowSpill(ctx, needed)) return -1;
                Buffer.MemoryCopy(src, ctx->SpillBuf + ctx->SpillLen, remaining, remaining);
                ctx->SpillLen += remaining;
            }
            return len;
        }

        // Only BIO_CTRL_FLUSH answers 1; 0 for PUSH/POP, the kTLS probes and everything else (no BIO_CTRL_RESET), as in the shim.
        [UnmanagedCallersOnly]
        private static long SpanOrReplayBioCtrl(IntPtr bio, int cmd, long num, void* ptr) => cmd == BIO_CTRL_FLUSH ? 1 : 0;

        [UnmanagedCallersOnly]
        private static int ManagedSpanBioCreate(IntPtr bio)
        {
            var ctx = (ManagedSpanBioCtx*)NativeMemory.AllocZeroed((nuint)sizeof(ManagedSpanBioCtx));
            if (ctx == null) return 0;
            BIO_set_data(bio, ctx);
            BIO_set_init(bio, 1);
            return 1;
        }

        [UnmanagedCallersOnly]
        private static int ManagedSpanBioDestroy(IntPtr bio)
        {
            if (bio == IntPtr.Zero) return 0;
            var ctx = (ManagedSpanBioCtx*)BIO_get_data(bio);
            if (ctx != null)
            {
                NativeMemory.Free(ctx->SpillBuf);
                NativeMemory.Free(ctx);
                BIO_set_data(bio, null);
            }
            BIO_set_init(bio, 0);
            return 1;
        }

        // pthread_once in the shim: built once; a failure leaves the method null for good, as there.
        private static IntPtr GetManagedSpanBioMethod()
        {
            if (!s_managedSpanBioInitDone)
                lock (s_bioMethodLock)
                    if (!s_managedSpanBioInitDone)
                    {
                        int index = BIO_get_new_index();
                        if (index != -1)
                        {
                            IntPtr method;
                            fixed (byte* name = "dotnet-managed-span\0"u8)
                                method = BIO_meth_new(index | BIO_TYPE_SOURCE_SINK, name);
                            if (method != IntPtr.Zero)
                            {
                                if (BIO_meth_set_write(method, &ManagedSpanBioWrite) == 0 || BIO_meth_set_read(method, &ManagedSpanBioRead) == 0
                                    || BIO_meth_set_ctrl(method, &SpanOrReplayBioCtrl) == 0 || BIO_meth_set_create(method, &ManagedSpanBioCreate) == 0
                                    || BIO_meth_set_destroy(method, &ManagedSpanBioDestroy) == 0)
                                    BIO_meth_free(method);
                                else
                                {
                                    s_managedSpanBioType = index | BIO_TYPE_SOURCE_SINK;
                                    s_managedSpanBioMethod = method;
                                }
                            }
                        }
                        s_managedSpanBioInitDone = true;
                    }
            return s_managedSpanBioMethod;
        }

        [RuntimeExport("CryptoNative_BioNewManagedSpan", Trigger + "CryptoNative_BioNewManagedSpan")]
        public static IntPtr BioNewManagedSpan()
        {
            ERR_clear_error();
            IntPtr method = GetManagedSpanBioMethod();
            return method == IntPtr.Zero ? IntPtr.Zero : BIO_new(method);
        }

        // Not exported by the shim: SslHandshake/SslEncrypt/SslDecrypt set the windows around each SSL call.
        internal static void BioSetReadWindow(IntPtr bio, byte* ptr, int len)
        {
            if (!TryGetManagedSpanBioCtx(bio, out var ctx)) return;
            ctx->ReadPtr = ptr;
            ctx->ReadLen = ptr != null ? len : 0;
            ctx->ReadPos = 0;
        }

        internal static void BioClearReadWindow(IntPtr bio, int* leftoverLength)
        {
            if (leftoverLength != null) *leftoverLength = 0;
            if (!TryGetManagedSpanBioCtx(bio, out var ctx)) return;
            if (leftoverLength != null) *leftoverLength = ctx->ReadLen - ctx->ReadPos;
            ctx->ReadPtr = null;
            ctx->ReadLen = 0;
            ctx->ReadPos = 0;
        }

        internal static void BioSetWriteWindow(IntPtr bio, byte* ptr, int capacity)
        {
            if (!TryGetManagedSpanBioCtx(bio, out var ctx)) return;
            ctx->WritePtr = ptr;
            ctx->WriteCapacity = ptr != null ? capacity : 0;
            ctx->WritePos = 0;
        }

        [RuntimeExport("CryptoNative_BioGetWriteResult", Trigger + "CryptoNative_BioGetWriteResult")]
        public static void BioGetWriteResult(IntPtr bio, int* writtenToWindow, int* spillLen)
        {
            if (writtenToWindow != null) *writtenToWindow = 0;
            if (spillLen != null) *spillLen = 0;
            if (!TryGetManagedSpanBioCtx(bio, out var ctx)) return;
            if (writtenToWindow != null) *writtenToWindow = ctx->WritePos;
            if (spillLen != null) *spillLen = ctx->SpillLen;
        }

        [RuntimeExport("CryptoNative_BioDrainSpill", Trigger + "CryptoNative_BioDrainSpill")]
        public static int BioDrainSpill(IntPtr bio, byte* dst, int dstLen)
        {
            if (dst == null || dstLen <= 0 || !TryGetManagedSpanBioCtx(bio, out var ctx)) return 0;
            if (ctx->SpillLen == 0) return 0;
            int toCopy = dstLen < ctx->SpillLen ? dstLen : ctx->SpillLen;
            Buffer.MemoryCopy(ctx->SpillBuf, dst, dstLen, toCopy);
            int remaining = ctx->SpillLen - toCopy;
            if (remaining > 0) Buffer.MemoryCopy(ctx->SpillBuf + toCopy, ctx->SpillBuf, ctx->SpillCapacity, remaining);
            ctx->SpillLen = remaining;
            return toCopy;
        }

        // ---- socket BIO with a replayable prefix ---------------------------------------------------------------------------
        // The deferred-server path: the ClientHello already peeked off the socket is read first, then recv() on the fd; writes go to
        // send(). The BIO does not own the fd (SafeSocketHandle does).
        [StructLayout(LayoutKind.Sequential)]
        private struct SocketReplayBioCtx
        {
            public byte* Prefix;
            public int PrefixLen;
            public int PrefixCap;
            public int PrefixPos;
            public int Fd;
        }

        [UnmanagedCallersOnly]
        private static int SocketReplayBioRead(IntPtr bio, byte* buf, int len)
        {
            if (bio == IntPtr.Zero || buf == null || len <= 0) return 0;
            BioClearRetryFlags(bio);
            var ctx = (SocketReplayBioCtx*)BIO_get_data(bio);
            if (ctx == null)
            {
                Marshal.SetLastSystemError(EBADF);
                return -1;
            }
            // The prefix stays allocated for the BIO's lifetime (BioGetReplayPrefix exposes it).
            int available = ctx->PrefixLen - ctx->PrefixPos;
            if (available > 0)
            {
                int toCopy = len < available ? len : available;
                Buffer.MemoryCopy(ctx->Prefix + ctx->PrefixPos, buf, toCopy, toCopy);
                ctx->PrefixPos += toCopy;
                return toCopy;
            }
            if (ctx->Fd < 0)
            {
                Marshal.SetLastSystemError(EBADF);
                return -1;
            }
            nint n;
            do n = libc_recv(ctx->Fd, buf, (nuint)len, 0);
            while (n < 0 && Errno == EINTR);
            if (n > 0) return (int)n;
            // Clean close: 0 without retry flags (SSL_ERROR_ZERO_RETURN / SYSCALL).
            if (n == 0) return 0;
            if (Errno == EAGAIN) BioSetRetryRead(bio);
            return -1;
        }

        [UnmanagedCallersOnly]
        private static int SocketReplayBioWrite(IntPtr bio, byte* buf, int len)
        {
            if (bio == IntPtr.Zero || buf == null || len < 0) return 0;
            BioClearRetryFlags(bio);
            if (len == 0) return 0;
            var ctx = (SocketReplayBioCtx*)BIO_get_data(bio);
            if (ctx == null || ctx->Fd < 0)
            {
                // A deterministic errno for the SSL_ERROR_SYSCALL path, not a stale one.
                Marshal.SetLastSystemError(EBADF);
                return -1;
            }
            nint n;
            do n = libc_send(ctx->Fd, buf, (nuint)len, MSG_NOSIGNAL);
            while (n < 0 && Errno == EINTR);
            if (n > 0) return (int)n;
            if (n == 0)
            {
                // Nothing accepted and no error: retry rather than surface an earlier errno.
                BioSetRetryWrite(bio);
                return -1;
            }
            if (Errno == EAGAIN) BioSetRetryWrite(bio);
            return -1;
        }

        [UnmanagedCallersOnly]
        private static int SocketReplayBioCreate(IntPtr bio)
        {
            var ctx = (SocketReplayBioCtx*)NativeMemory.AllocZeroed((nuint)sizeof(SocketReplayBioCtx));
            if (ctx == null) return 0;
            ctx->Fd = -1;
            BIO_set_data(bio, ctx);
            BIO_set_init(bio, 1);
            return 1;
        }

        [UnmanagedCallersOnly]
        private static int SocketReplayBioDestroy(IntPtr bio)
        {
            if (bio == IntPtr.Zero) return 0;
            var ctx = (SocketReplayBioCtx*)BIO_get_data(bio);
            if (ctx != null)
            {
                NativeMemory.Free(ctx->Prefix);
                NativeMemory.Free(ctx);
                BIO_set_data(bio, null);
            }
            BIO_set_init(bio, 0);
            return 1;
        }

        private static IntPtr GetSocketReplayBioMethod()
        {
            if (!s_socketReplayBioInitDone)
                lock (s_bioMethodLock)
                    if (!s_socketReplayBioInitDone)
                    {
                        int index = BIO_get_new_index();
                        if (index != -1)
                        {
                            IntPtr method;
                            fixed (byte* name = "dotnet-socket-replay\0"u8)
                                method = BIO_meth_new(index | BIO_TYPE_SOURCE_SINK, name);
                            if (method != IntPtr.Zero)
                            {
                                if (BIO_meth_set_write(method, &SocketReplayBioWrite) == 0 || BIO_meth_set_read(method, &SocketReplayBioRead) == 0
                                    || BIO_meth_set_ctrl(method, &SpanOrReplayBioCtrl) == 0 || BIO_meth_set_create(method, &SocketReplayBioCreate) == 0
                                    || BIO_meth_set_destroy(method, &SocketReplayBioDestroy) == 0)
                                    BIO_meth_free(method);
                                else
                                {
                                    s_socketReplayBioType = index | BIO_TYPE_SOURCE_SINK;
                                    s_socketReplayBioMethod = method;
                                }
                            }
                        }
                        s_socketReplayBioInitDone = true;
                    }
            return s_socketReplayBioMethod;
        }

        // The socket-replay BIO's context, only for BIOs of that type (the shim reads BIO_get_data unchecked; here a BIO of another type is
        // refused rather than read past its context).
        private static SocketReplayBioCtx* GetSocketReplayBioCtx(IntPtr bio)
        {
            if (s_socketReplayBioType == 0 || BIO_method_type(bio) != s_socketReplayBioType) return null;
            return (SocketReplayBioCtx*)BIO_get_data(bio);
        }

        [RuntimeExport("CryptoNative_BioNewSocketReplay", Trigger + "CryptoNative_BioNewSocketReplay")]
        public static IntPtr BioNewSocketReplay(nint fd, byte* prefix, int prefixLen)
        {
            ERR_clear_error();
            if (fd < 0 || prefixLen < 0 || (prefixLen > 0 && prefix == null)) return IntPtr.Zero;
            IntPtr method = GetSocketReplayBioMethod();
            if (method == IntPtr.Zero) return IntPtr.Zero;
            IntPtr bio = BIO_new(method);
            if (bio == IntPtr.Zero) return IntPtr.Zero;
            var ctx = (SocketReplayBioCtx*)BIO_get_data(bio);
            ctx->Fd = (int)fd;
            if (prefixLen > 0)
            {
                var copy = (byte*)NativeMemory.Alloc((nuint)prefixLen);
                if (copy == null)
                {
                    BIO_free(bio);
                    return IntPtr.Zero;
                }
                Buffer.MemoryCopy(prefix, copy, prefixLen, prefixLen);
                ctx->Prefix = copy;
                ctx->PrefixLen = prefixLen;
                ctx->PrefixCap = prefixLen;
            }
            return bio;
        }

        // Reads the socket into the BIO's peek buffer until one whole TLS record (5-byte header + fragment) is there: 1 when it is
        // (*outPtr/*outLen wrap the buffer), 0 when the socket would block, -1 on error, EOF or a record larger than the buffer.
        [RuntimeExport("CryptoNative_BioPeekTlsFrame", Trigger + "CryptoNative_BioPeekTlsFrame")]
        public static int BioPeekTlsFrame(IntPtr bio, byte** outPtr, int* outLen)
        {
            ERR_clear_error();
            if (bio == IntPtr.Zero || outPtr == null || outLen == null) return -1;
            var ctx = GetSocketReplayBioCtx(bio);
            if (ctx == null || ctx->Fd < 0) return -1;
            // 5-byte header + 2^14 payload + 2048 of encryption overhead.
            const int MaxTlsRecord = 5 + (1 << 14) + 2048;
            if (ctx->Prefix == null)
            {
                ctx->Prefix = (byte*)NativeMemory.Alloc(MaxTlsRecord);
                if (ctx->Prefix == null) return -1;
                ctx->PrefixCap = MaxTlsRecord;
                ctx->PrefixLen = 0;
                ctx->PrefixPos = 0;
            }
            else if (ctx->PrefixCap < MaxTlsRecord)
                return -1;
            for (;;)
            {
                int need;
                if (ctx->PrefixLen < 5)
                    need = 5;
                else
                {
                    int fragmentLen = (ctx->Prefix[3] << 8) | ctx->Prefix[4];
                    need = 5 + fragmentLen;
                    if (need > ctx->PrefixCap) return -1;
                    if (ctx->PrefixLen >= need) break;
                }
                int want = need - ctx->PrefixLen;
                nint n;
                do n = libc_recv(ctx->Fd, ctx->Prefix + ctx->PrefixLen, (nuint)want, 0);
                while (n < 0 && Errno == EINTR);
                if (n > 0)
                {
                    ctx->PrefixLen += (int)n;
                    continue;
                }
                if (n == 0) return -1;
                if (Errno == EAGAIN) return 0;
                return -1;
            }
            *outPtr = ctx->Prefix;
            *outLen = ctx->PrefixLen;
            return 1;
        }

        [RuntimeExport("CryptoNative_BioGetReplayPrefix", Trigger + "CryptoNative_BioGetReplayPrefix")]
        public static int BioGetReplayPrefix(IntPtr bio, byte** outPtr, int* outLen)
        {
            if (bio == IntPtr.Zero || outPtr == null || outLen == null) return -1;
            var ctx = GetSocketReplayBioCtx(bio);
            if (ctx == null) return -1;
            if (ctx->Prefix == null || ctx->PrefixLen <= 0)
            {
                *outPtr = null;
                *outLen = 0;
                return 0;
            }
            *outPtr = ctx->Prefix;
            *outLen = ctx->PrefixLen;
            return 1;
        }
    }
}
#endif
