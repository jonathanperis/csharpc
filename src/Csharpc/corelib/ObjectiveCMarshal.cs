// System.Runtime.InteropServices.ObjectiveC.ObjectiveCMarshal: the runtime hooks of the .NET for macOS / iOS
// bindings (Microsoft.macOS.dll initializes it at startup and gives every NSObject a reference-tracking handle).
//
// Off macOS every member is a PlatformNotSupportedException, exactly as in .NET. On macOS the design needs what
// csharpc doesn't have yet: .NET's tracked types are types with a finalizer (csharpc has those now, ADR 0021),
// the collector asks the isReferencedCallback for each tracked object while it marks (between two
// beginEndCallback calls) and calls trackedObjectEnteredFinalization as an object enters the finalizer queue
// (NativeAOT: gcenv.ee.cpp, interoplibinterface_objc.cpp). Without those callbacks a "tracked" object could only
// be kept alive forever or freed without them: both differ silently from NativeAOT. Instead each
// member is an explicit NotSupportedException, so a program that links the bindings builds and stops with a
// message at its first use rather than mis-managing native objects.
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.ObjectiveC.ObjectiveCMarshal")]
    public static unsafe class ObjectiveCMarshalImpl
    {
#if CSHARPC_TARGET_MACOS
        private static Exception Unsupported(string member) => new NotSupportedException(
            "ObjectiveCMarshal." + member + " is not supported by csharpc yet: reference tracking needs the collector's tracked-reference callbacks (isReferencedCallback while marking, the begin/end callbacks), which csharpc does not provide yet.");
#else
        private static Exception Unsupported(string member) => new PlatformNotSupportedException();
#endif

        public static void Initialize(
            delegate* unmanaged<void> beginEndCallback,
            delegate* unmanaged<IntPtr, int> isReferencedCallback,
            delegate* unmanaged<IntPtr, void> trackedObjectEnteredFinalization,
            ObjectiveCMarshal.UnhandledExceptionPropagationHandler unhandledExceptionPropagationHandler) =>
            throw Unsupported("Initialize");

        public static GCHandle CreateReferenceTrackingHandle(object obj, out Span<IntPtr> taggedMemory) =>
            throw Unsupported("CreateReferenceTrackingHandle");

        public static Span<IntPtr> GetOrCreateReferenceTrackingMemory(object obj) =>
            throw Unsupported("GetOrCreateReferenceTrackingMemory");

        public static void SetMessageSendCallback(ObjectiveCMarshal.MessageSendFunction msgSendFunction, IntPtr func) =>
            throw Unsupported("SetMessageSendCallback");

        public static void SetMessageSendPendingException(Exception exception) =>
            throw Unsupported("SetMessageSendPendingException");
    }
}
