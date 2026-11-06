// System.Runtime.InteropServices.ObjectiveC.ObjectiveCMarshal: the runtime hooks of the .NET for macOS / iOS
// bindings (Microsoft.macOS.dll initializes it at startup and gives every NSObject a reference-tracking handle).
//
// Off macOS every member is a PlatformNotSupportedException, exactly as in .NET. On macOS the reference tracking
// follows NativeAOT (ObjectiveCMarshal.cs / .NativeAot.cs, interoplibinterface_objc.cpp, gcenv.ee.cpp): Initialize
// registers the begin/end, isReferenced and entered-finalization callbacks with the collector (src/runtime/gc.c,
// "Objective-C reference tracking"), a type with [ObjectiveCTrackedType] and a finalizer gets tagged memory (two words
// the native side owns) and reference-tracking handles; every collection asks the native side about the tracked objects
// nothing else reached. SetMessageSendCallback overrides libobjc's objc_msgSend family: the P/Invoke stubs of those
// entry points (ModuleCodeGen.PInvoke.cs) call the registered function instead, and throw the exception
// SetMessageSendPendingException stored on the thread once the call returns. The unhandled-exception propagation
// handler is stored but never called: an exception that escapes an [UnmanagedCallersOnly] method ends the process.
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;

namespace CsharpcCoreLib
{
    internal static class ObjcTracking
    {
        [DllImport("__Internal")] public static extern byte csrt_objc_initialize(IntPtr beginEnd, IntPtr isReferenced, IntPtr enteredFinalization);
        [DllImport("__Internal")] public static extern byte csrt_objc_is_tracked_type(IntPtr obj);
        [DllImport("__Internal")] public static extern IntPtr csrt_objc_tagged_memory(IntPtr obj, int handle);
        [DllImport("__Internal")] public static extern void csrt_objc_release_handle(IntPtr obj);
        [DllImport("__Internal")] public static extern byte csrt_objc_set_msgsend(int kind, IntPtr func);
        [DllImport("__Internal")] public static extern void csrt_objc_set_pending(IntPtr exception);
    }

    [Shadows("System.Runtime.InteropServices.ObjectiveC.ObjectiveCMarshal")]
    public static unsafe class ObjectiveCMarshalImpl
    {
#if CSHARPC_TARGET_MACOS
        private static bool s_initialized;
        private static ObjectiveCMarshal.UnhandledExceptionPropagationHandler s_unhandledExceptionPropagationHandler;

        public static void Initialize(
            delegate* unmanaged<void> beginEndCallback,
            delegate* unmanaged<IntPtr, int> isReferencedCallback,
            delegate* unmanaged<IntPtr, void> trackedObjectEnteredFinalization,
            ObjectiveCMarshal.UnhandledExceptionPropagationHandler unhandledExceptionPropagationHandler)
        {
            ArgumentNullException.ThrowIfNull(beginEndCallback);
            ArgumentNullException.ThrowIfNull(isReferencedCallback);
            ArgumentNullException.ThrowIfNull(trackedObjectEnteredFinalization);
            ArgumentNullException.ThrowIfNull(unhandledExceptionPropagationHandler);

            if (s_unhandledExceptionPropagationHandler != null
                || ObjcTracking.csrt_objc_initialize((IntPtr)beginEndCallback, (IntPtr)isReferencedCallback, (IntPtr)trackedObjectEnteredFinalization) == 0)
                throw new InvalidOperationException("Attempt to reinitialize ObjectiveCMarshal.");
            s_initialized = true;
            s_unhandledExceptionPropagationHandler = unhandledExceptionPropagationHandler;
        }

        public static GCHandle CreateReferenceTrackingHandle(object obj, out Span<IntPtr> taggedMemory)
        {
            // GetOrCreateReferenceTrackingMemory validates the argument and the state.
            taggedMemory = GetOrCreateReferenceTrackingMemory(obj);
            ObjcTracking.csrt_objc_tagged_memory(CoreIntrinsics.ObjectToPointer(obj), 1);
            return GCHandleImpl.AllocReferenceTracking(obj);
        }

        public static Span<IntPtr> GetOrCreateReferenceTrackingMemory(object obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            if (!s_initialized) throw new InvalidOperationException("Attempt to track an Objective-C Type without initializing.");
            IntPtr pointer = CoreIntrinsics.ObjectToPointer(obj);
            if (ObjcTracking.csrt_objc_is_tracked_type(pointer) == 0)
                throw new InvalidOperationException("Attempt to track an Objective-C Type without a finalizer.");
            return new Span<IntPtr>((void*)ObjcTracking.csrt_objc_tagged_memory(pointer, 0), 2);
        }

        public static void SetMessageSendCallback(ObjectiveCMarshal.MessageSendFunction msgSendFunction, IntPtr func)
        {
            if (msgSendFunction < ObjectiveCMarshal.MessageSendFunction.MsgSend || msgSendFunction > ObjectiveCMarshal.MessageSendFunction.MsgSendSuperStret)
                throw new ArgumentOutOfRangeException("msgSendFunction");
            if (ObjcTracking.csrt_objc_set_msgsend((int)msgSendFunction, func) == 0)
                throw new InvalidOperationException("Attempt to update previously set Objective-C msgSend API overrides.");
        }

        // Thrown by the P/Invoke stub of the next objc_msgSend call on this thread to return (ModuleCodeGen.PInvoke.cs).
        public static void SetMessageSendPendingException(Exception exception) =>
            ObjcTracking.csrt_objc_set_pending(CoreIntrinsics.ObjectToPointer(exception));
#else
        public static void Initialize(
            delegate* unmanaged<void> beginEndCallback,
            delegate* unmanaged<IntPtr, int> isReferencedCallback,
            delegate* unmanaged<IntPtr, void> trackedObjectEnteredFinalization,
            ObjectiveCMarshal.UnhandledExceptionPropagationHandler unhandledExceptionPropagationHandler) =>
            throw new PlatformNotSupportedException();

        public static GCHandle CreateReferenceTrackingHandle(object obj, out Span<IntPtr> taggedMemory) =>
            throw new PlatformNotSupportedException();

        public static Span<IntPtr> GetOrCreateReferenceTrackingMemory(object obj) =>
            throw new PlatformNotSupportedException();

        public static void SetMessageSendCallback(ObjectiveCMarshal.MessageSendFunction msgSendFunction, IntPtr func) =>
            throw new PlatformNotSupportedException();

        public static void SetMessageSendPendingException(Exception exception) =>
            throw new PlatformNotSupportedException();
#endif
    }
}
