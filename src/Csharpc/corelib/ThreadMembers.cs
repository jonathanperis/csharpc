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
