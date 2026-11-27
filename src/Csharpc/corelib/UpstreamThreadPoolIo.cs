// Kernel32's thread-pool I/O objects for the Windows CoreLib's overlapped file I/O (#424). SafeFileHandle binds an asynchronous handle with
// ThreadPoolBoundHandle, which, on the Windows thread pool's path NativeAOT takes there (the slicer folds ThreadPool.UseWindowsThreadPool to
// true), calls CreateThreadpoolIo with its [UnmanagedCallersOnly] completion callback, StartThreadpoolIo before each operation,
// CancelThreadpoolIo when one failed without queueing a completion, and CloseThreadpoolIo when the handle is released. Windows would run that
// callback on its own pool's threads, which csharpc's runtime does not attach to its GC; this contract runs it on csharpc's threads instead.
// Each P/Invoke of CoreLib's IL to those four entry points binds here by name ([RuntimeExport] with a "kernel32.dll!" trigger,
// ModuleCodeGen.PInvoke), the same way libSystem.Native's entry points bind to corelib/IO/SystemNative.cs.
//
// One I/O completion port serves every bound handle; a background csharpc thread dequeues its packets and queues each completion to csharpc's
// thread pool, so a callback that blocks (a continuation waiting on another read) never stops the dequeueing. The callback receives what
// Windows passes: no callback instance, the context given to CreateThreadpoolIo, the OVERLAPPED, NO_ERROR or the operation's Win32 error, the
// bytes transferred, and the I/O object.
//
// Security: an I/O object is native memory owned by its reference count: one for CreateThreadpoolIo's caller (dropped by CloseThreadpoolIo),
// one per started operation (dropped by its completion or by CancelThreadpoolIo), so it is freed exactly once, after the last completion that
// can name it. The completion key is the object's address, which only this file hands out.
#if CSHARPC_TARGET_WINDOWS
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CsharpcCoreLib
{
    /// <summary>
    /// ThreadPoolCallbackWrapper (the slice's references are redirected here): .NET marks a Windows thread-pool thread as a pool thread on
    /// entry and resets its name, priority and background state on exit. Here the completions run as csharpc thread-pool work items, on
    /// csharpc's pool threads, which treat them as any other work item.
    /// </summary>
    public struct UpstreamThreadPoolCallbackWrapper
    {
        public static UpstreamThreadPoolCallbackWrapper Enter() => default;

        public void Exit(bool resetThread) { }
    }

    public static unsafe class UpstreamThreadPoolIo
    {
        private const string Trigger = "kernel32.dll!";
        private const uint Infinite = 0xFFFFFFFF;

        [DllImport("kernel32")] private static extern nint CreateIoCompletionPort(nint fileHandle, nint existingCompletionPort, nuint completionKey, uint numberOfConcurrentThreads);
        [DllImport("kernel32")] private static extern int GetQueuedCompletionStatus(nint completionPort, uint* numberOfBytes, nuint* completionKey, nint* overlapped, uint milliseconds);
        [DllImport("kernel32")] private static extern int GetLastError();
        [DllImport("kernel32")] private static extern void SetLastError(int error);

        private struct IoObject
        {
            public delegate* unmanaged<nint, nint, nint, uint, nuint, nint, void> Callback;
            public nint Context;
            public int References;
        }

        private sealed class Completion
        {
            public IoObject* Io;
            public nint Overlapped;
            public uint Result;
            public nuint Bytes;
        }

        private static readonly object s_lock = new object();
        private static nint s_port;
        private static readonly WaitCallback s_run = Run;

        private static nint Port()
        {
            nint port = Volatile.Read(ref s_port);
            if (port != 0) return port;
            lock (s_lock)
            {
                if (s_port != 0) return s_port;
                port = CreateIoCompletionPort(-1, 0, 0, 0);
                if (port == 0) return 0;
                var poller = new Thread(Poll) { IsBackground = true };
                poller.Start();
                Volatile.Write(ref s_port, port);
                return port;
            }
        }

        [RuntimeExport("CreateThreadpoolIo", Trigger + "CreateThreadpoolIo")]
        public static nint CreateThreadpoolIo(nint fl, delegate* unmanaged<nint, nint, nint, uint, nuint, nint, void> pfnio, nint pv, nint pcbe)
        {
            nint port = Port();
            if (port == 0) return 0; // CreateIoCompletionPort's error stays the thread's last error
            var io = (IoObject*)NativeMemory.Alloc((nuint)sizeof(IoObject));
            io->Callback = pfnio;
            io->Context = pv;
            io->References = 1;
            if (CreateIoCompletionPort(fl, port, (nuint)io, 0) == 0)
            {
                // An invalid handle (ERROR_INVALID_HANDLE) or one already bound to a port (ERROR_INVALID_PARAMETER): ThreadPoolBoundHandle
                // turns both into .NET's ArgumentExceptions.
                int error = GetLastError();
                NativeMemory.Free(io);
                SetLastError(error);
                return 0;
            }
            return (nint)io;
        }

        [RuntimeExport("StartThreadpoolIo", Trigger + "StartThreadpoolIo")]
        public static void StartThreadpoolIo(nint pio) => Interlocked.Increment(ref ((IoObject*)pio)->References);

        [RuntimeExport("CancelThreadpoolIo", Trigger + "CancelThreadpoolIo")]
        public static void CancelThreadpoolIo(nint pio) => Release((IoObject*)pio);

        [RuntimeExport("CloseThreadpoolIo", Trigger + "CloseThreadpoolIo")]
        public static void CloseThreadpoolIo(nint pio) => Release((IoObject*)pio);

        private static void Release(IoObject* io)
        {
            if (Interlocked.Decrement(ref io->References) == 0) NativeMemory.Free(io);
        }

        private static void Poll()
        {
            nint port = s_port;
            for (;;)
            {
                uint bytes;
                nuint key;
                nint overlapped;
                uint result = 0;
                if (GetQueuedCompletionStatus(port, &bytes, &key, &overlapped, Infinite) == 0)
                {
                    // FALSE with an OVERLAPPED: the operation failed (end of file, cancelled...), its error is the result. Without one the
                    // dequeue itself failed and there is no packet.
                    if (overlapped == 0) continue;
                    result = (uint)GetLastError();
                }
                if (key == 0) continue;
                ThreadPool.UnsafeQueueUserWorkItem(s_run, new Completion { Io = (IoObject*)key, Overlapped = overlapped, Result = result, Bytes = bytes });
            }
        }

        private static void Run(object state)
        {
            var completion = (Completion)state;
            IoObject* io = completion.Io;
            try
            {
                io->Callback(0, io->Context, completion.Overlapped, completion.Result, completion.Bytes, (nint)io);
            }
            finally
            {
                Release(io);
            }
        }
    }
}
#endif
