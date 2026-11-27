// Thread.GetCurrentProcessorId(): the CPU the calling thread runs on right now (sched_getcpu on Linux). Striped
// counters and per-processor caches (DiagnosticSource, pools) take it modulo their size. Other platforms answer 0, a
// valid index for any size: the value is advisory there too (the thread can move the next instant).
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Threading.Thread")]
    internal static class ThreadMembers
    {
        /// <summary>
        /// Thread.OptimalMaxSpinWaitsPerSpinIteration (internal upstream; the framework's SpinWait reads it): the most pauses one
        /// SpinOnce spins. .NET measures its normalized pause on the machine; csharpc's Thread.SpinWait is one PAUSE/YIELD per
        /// iteration, and 8 keeps a spin step at a few hundred cycles, the order .NET's normalization aims for.
        /// </summary>
        internal static int OptimalMaxSpinWaitsPerSpinIteration => 8;

        /// <summary>
        /// Thread.CurrentOSThreadId (internal upstream; EventWrittenEventArgs.OSThreadId reads it): the kernel's id of the calling thread, as the
        /// runtime's PAL gives it: gettid on Linux, pthread_threadid_np on macOS, GetCurrentThreadId on Windows. WASI has one thread: 1.
        /// </summary>
        internal static unsafe ulong CurrentOSThreadId
        {
            get
            {
#if CSHARPC_TARGET_LINUX
                return (ulong)gettid();
#elif CSHARPC_TARGET_MACOS
                ulong id = 0;
                pthread_threadid_np(0, &id);
                return id;
#elif CSHARPC_TARGET_WINDOWS
                return GetCurrentThreadId();
#else
                return 1;
#endif
            }
        }

#if CSHARPC_TARGET_LINUX
        [DllImport("libc")] private static extern int gettid();
#elif CSHARPC_TARGET_MACOS
        [DllImport("libc")] private static extern unsafe int pthread_threadid_np(nint thread, ulong* id);
#elif CSHARPC_TARGET_WINDOWS
        [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
#endif

#if CSHARPC_TARGET_LINUX
        [DllImport("libc")] private static extern int sched_getcpu();

        public static int GetCurrentProcessorId()
        {
            int cpu = sched_getcpu();
            return cpu >= 0 ? cpu : 0;
        }
#else
        public static int GetCurrentProcessorId() => 0;
#endif
    }
}
