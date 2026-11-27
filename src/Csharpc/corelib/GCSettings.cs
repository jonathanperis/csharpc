// System.Runtime.GCSettings (#97): what .NET reports for a workstation GC. The modes are kept and reported back; csharpc's collector
// does not change its behaviour for them (no low-latency or large-object-heap compaction modes of its own).
#nullable disable
using System;
using System.Runtime;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.GCSettings")]
    public static class GCSettingsImpl
    {
        private static GCLatencyMode s_latency = GCLatencyMode.Interactive;
        private static GCLargeObjectHeapCompactionMode s_lohMode = GCLargeObjectHeapCompactionMode.Default;
        private static int s_lohModeSetAt;

        public static bool IsServerGC => false;

        public static GCLatencyMode LatencyMode
        {
            get => s_latency;
            set
            {
                // NoGCRegion is entered with GC.TryStartNoGCRegion, never set.
                if (value < GCLatencyMode.Batch || value > GCLatencyMode.SustainedLowLatency)
                    throw new ArgumentOutOfRangeException("value", "Enum value was out of legal range.");
                s_latency = value;
            }
        }

        // CompactOnce applies to the next blocking full collection, after which .NET reads Default again.
        public static GCLargeObjectHeapCompactionMode LargeObjectHeapCompactionMode
        {
            get => s_lohMode == GCLargeObjectHeapCompactionMode.CompactOnce && GC.CollectionCount(2) > s_lohModeSetAt
                ? GCLargeObjectHeapCompactionMode.Default : s_lohMode;
            set
            {
                if (value < GCLargeObjectHeapCompactionMode.Default || value > GCLargeObjectHeapCompactionMode.CompactOnce)
                    throw new ArgumentOutOfRangeException("value", "Enum value was out of legal range.");
                s_lohMode = value;
                s_lohModeSetAt = GC.CollectionCount(2);
            }
        }
    }
}
