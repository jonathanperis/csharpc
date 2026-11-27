// The runtime side of System.Diagnostics.Tracing (#423). EventSource, EventListener, the counters and TraceLogging are the framework's IL
// (src/Csharpc/upstream-types/System.Diagnostics.Tracing.txt, docs/il-first/System.Diagnostics.Tracing.md); the runtime services that IL
// calls through QCalls are csharpc's, here, bound by name like the libSystem.Native contract ([RuntimeExport] with a "QCall!" trigger,
// ModuleCodeGen.PInvoke).
//
// EventPipe is disabled, as in a NativeAOT program linked without EventPipe: no session ever exists, so EventListener.EnableEvents of the
// runtime's own provider (Microsoft-Windows-DotNETRuntime) starts nothing (Enable answers with session 0, which the framework's dispatcher
// takes as "not enabled"), a provider's events are never defined (DefineEvent answers 0, and the framework writes nothing for an event
// without a handle) and nothing is written. What does not depend on a session is kept: CreateProvider answers with a handle that is never
// dereferenced (0 would make the framework throw OutOfMemoryException from every EventSource constructor, which NativeAOT's disabled
// EventPipe never reaches because it turns event sources off altogether), and the thread's activity id, which EventPipe keeps per thread
// (EventSource.CurrentThreadActivityId, SetCurrentThreadActivityId, WriteEventWithRelatedActivityId), is kept per thread here with
// eventpipeinternal.cpp's semantics (dotnet/runtime release/11.0, src/coreclr/nativeaot/Runtime).
// In-process listeners of the program's own event sources are unaffected: the framework dispatches those itself, without EventPipe.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static unsafe class UpstreamEventPipe
    {
        private const string Trigger = "QCall!";

        [ThreadStatic] private static Guid t_activityId;

        [RuntimeExport("EventPipeInternal_Enable", Trigger + "EventPipeInternal_Enable")]
        public static ulong Enable(char* outputFile, int format, uint circularBufferSizeInMB, void* providers, uint numProviders) => 0;

        [RuntimeExport("EventPipeInternal_Disable", Trigger + "EventPipeInternal_Disable")]
        public static void Disable(ulong sessionID) { }

        // Never dereferenced: DefineEvent, GetProvider and DeleteProvider below only receive it back.
        [RuntimeExport("EventPipeInternal_CreateProvider", Trigger + "EventPipeInternal_CreateProvider")]
        public static nint CreateProvider(char* providerName, void* callbackFunc, void* callbackContext) => 1;

        [RuntimeExport("EventPipeInternal_DefineEvent", Trigger + "EventPipeInternal_DefineEvent")]
        public static nint DefineEvent(nint provHandle, uint eventID, long keywords, uint eventVersion, uint level, void* pMetadata, uint metadataLength) => 0;

        [RuntimeExport("EventPipeInternal_GetProvider", Trigger + "EventPipeInternal_GetProvider")]
        public static nint GetProvider(char* providerName) => 0;

        [RuntimeExport("EventPipeInternal_DeleteProvider", Trigger + "EventPipeInternal_DeleteProvider")]
        public static void DeleteProvider(nint provHandle) { }

        /// <summary>
        /// The thread's activity id (Interop.Advapi32.ActivityControl): 1 get, 2 set, 3 create a new id without setting it, 4 swap,
        /// 5 return the current id and set a new one. A new id is a random version-4 Guid, as ep_thread_create_activity_id makes it.
        /// </summary>
        [RuntimeExport("EventPipeInternal_EventActivityIdControl", Trigger + "EventPipeInternal_EventActivityIdControl")]
        public static int EventActivityIdControl(uint controlCode, Guid* activityId)
        {
            if (activityId == null) return 1;
            switch (controlCode)
            {
                case 1: *activityId = t_activityId; return 0;
                case 2: t_activityId = *activityId; return 0;
                case 3: *activityId = Guid.NewGuid(); return 0;
                case 4: { Guid current = t_activityId; t_activityId = *activityId; *activityId = current; return 0; }
                case 5: *activityId = t_activityId; t_activityId = Guid.NewGuid(); return 0;
                default: return 1;
            }
        }

        [RuntimeExport("EventPipeInternal_WriteEventData", Trigger + "EventPipeInternal_WriteEventData")]
        public static void WriteEventData(nint eventHandle, void* pEventData, uint dataCount, Guid* activityId, Guid* relatedActivityId) { }

        [RuntimeExport("EventPipeInternal_GetSessionInfo", Trigger + "EventPipeInternal_GetSessionInfo")]
        public static int GetSessionInfo(ulong sessionID, void* pSessionInfo) => 0;

        [RuntimeExport("EventPipeInternal_GetNextEvent", Trigger + "EventPipeInternal_GetNextEvent")]
        public static int GetNextEvent(ulong sessionID, void* pInstance) => 0;

        [RuntimeExport("EventPipeInternal_SignalSession", Trigger + "EventPipeInternal_SignalSession")]
        public static int SignalSession(ulong sessionID) => 0;

        [RuntimeExport("EventPipeInternal_WaitForSessionSignal", Trigger + "EventPipeInternal_WaitForSessionSignal")]
        public static int WaitForSessionSignal(ulong sessionID, int timeoutMs) => 0;
    }

    /// <summary>The members of the tracing IL csharpc serves as NativeAOT's build of CoreLib has them (tools/il-first/slicer, csharpc profile).</summary>
    internal static class UpstreamTracing
    {
        /// <summary>
        /// EventSource.GetHelperCallFirstArg: CoreCLR's CoreLib reads an event method's IL to check the id it passes to WriteEvent; NativeAOT's
        /// has no IL to read and answers -1 ("could not be determined"), so the check is not made, and neither is it here.
        /// </summary>
        public static int GetHelperCallFirstArg(System.Reflection.MethodInfo method) => -1;

        /// <summary>
        /// TraceLogging's Statics.IsGenericMatch (`type.IsGenericType &amp;&amp; type.GetGenericTypeDefinition() == openType`), without asking a
        /// non-public type for its definition: csharpc has no Type for the definition of a CoreLib-internal interface (IBinaryFloatParseAndFormatInfo&lt;Half&gt;,
        /// which Half's GetInterfaces lists), and a non-public type's definition is never one of the public types TraceLogging passes.
        /// </summary>
        public static bool IsGenericMatch(Type type, object openType) =>
            type.IsGenericType && (type.IsPublic || type.IsNestedPublic || openType is Type { IsPublic: false, IsNestedPublic: false }) && type.GetGenericTypeDefinition() == (Type)openType;

        /// <summary>
        /// EventSource.InitializeDefaultEventSources (both overloads; the runtime calls the [UnmanagedCallersOnly] one at start-up): csharpc's runtime
        /// does not create the runtime's and libraries' default event sources (Microsoft-Windows-DotNETRuntime, System.Runtime, ...), whose events
        /// and counters come from the runtime itself (docs/il-first/System.Diagnostics.Tracing.md, "Differences").
        /// </summary>
        public static void InitializeDefaultEventSources() { }

        /// <summary>XplatEventLogger.InitializePersistentListener: no LTTng listener (CoreCLR's FEATURE_EVENTSOURCE_XPLAT), as in NativeAOT.</summary>
        public static System.Diagnostics.Tracing.EventListener InitializePersistentListener() => null;

#pragma warning disable CS8500 // (the [UnmanagedCallersOnly] overload's out parameter for an exception, never written here)
        public static unsafe void InitializeDefaultEventSources(Exception* pException) { }
#pragma warning restore CS8500
    }
}
