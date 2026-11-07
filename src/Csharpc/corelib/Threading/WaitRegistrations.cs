// ThreadPool.RegisterWaitForSingleObject and RegisteredWaitHandle, and WaitHandle.SafeWaitHandle's OS handle.
//
// A registration is one background thread that waits on the wait handle (a runtime event) with the timeout and
// queues the callback to the thread pool each time the wait ends; Unregister interrupts the thread, which is
// blocked in that wait. .NET multiplexes registrations over a few wait threads; the observable behaviour is the
// same: the callback runs on a pool thread with timedOut, a periodic registration keeps waiting after it,
// Unregister stops it and signals the optional wait handle once no callback is running.
//
// A runtime event has no operating-system handle. On Windows, WaitHandle.SafeWaitHandle creates a real event
// (CreateEvent, auto-reset) and a background thread that sets the runtime event whenever the OS event is
// signaled, so that native code which signals handles (RegNotifyChangeKeyValue in System.Net.Http's
// HttpWindowsProxy) reaches the managed side. Elsewhere there is no such native consumer and the property is an
// explicit NotSupportedException.
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.RegisteredWaitHandle")]
    public sealed class RegisteredWaitHandleImpl : MarshalByRefObject
    {
        private readonly WaitHandle _waitObject;
        private readonly WaitOrTimerCallback _callback;
        private readonly object _state;
        private readonly int _timeout;
        private readonly bool _executeOnlyOnce;
        private readonly object _lock = new object();
        private Thread _thread;
        private bool _unregisterCalled;
        private int _running;                 // callbacks queued or running
        private WaitHandle _unregisterSignal; // set once the last callback has finished after Unregister

        internal RegisteredWaitHandleImpl(WaitHandle waitObject, WaitOrTimerCallback callback, object state, int timeout, bool executeOnlyOnce)
        {
            _waitObject = waitObject;
            _callback = callback;
            _state = state;
            _timeout = timeout;
            _executeOnlyOnce = executeOnlyOnce;
        }

        internal void Start()
        {
            _thread = new Thread(Wait);
            _thread.IsBackground = true;
            _thread.Start();
        }

        private void Wait()
        {
            try
            {
                while (true)
                {
                    bool signaled = _waitObject.WaitOne(_timeout);
                    lock (_lock)
                    {
                        if (_unregisterCalled) return;
                        _running++;
                    }
                    bool timedOut = !signaled;
                    ThreadPool.QueueUserWorkItem(_ => RunCallback(timedOut));
                    if (_executeOnlyOnce) return;
                }
            }
            catch (ThreadInterruptedException)
            {
                // Unregister: this registration is over.
            }
        }

        private void RunCallback(bool timedOut)
        {
            try
            {
                _callback(_state, timedOut);
            }
            finally
            {
                SignalIfDone(decrement: true);
            }
        }

        private void SignalIfDone(bool decrement)
        {
            WaitHandle signal = null;
            lock (_lock)
            {
                if (decrement) _running--;
                if (_unregisterCalled && _running == 0)
                {
                    signal = _unregisterSignal;
                    _unregisterSignal = null;
                }
            }
            if (signal != null) SignalHandle(signal);
        }

        // The runtime's events are all one kind of object: Set is a runtime call whatever the declared type.
        private static void SignalHandle(WaitHandle handle) => CoreIntrinsics.ReinterpretAs<EventWaitHandle>(handle).Set();

        public bool Unregister(WaitHandle waitObject)
        {
            Thread thread;
            lock (_lock)
            {
                if (_unregisterCalled) return false;
                _unregisterCalled = true;
                _unregisterSignal = waitObject;
                thread = _thread;
            }
            // The waiting thread is blocked in WaitOne: interrupting it ends the registration.
            if (thread != null && thread != Thread.CurrentThread) thread.Interrupt();
            SignalIfDone(decrement: false);
            return true;
        }
    }

    [ShadowMembers("System.Threading.ThreadPool")]
    internal static class ThreadPoolWaits
    {
        public static RegisteredWaitHandle RegisterWaitForSingleObject(WaitHandle waitObject, WaitOrTimerCallback callBack, object state, int millisecondsTimeOutInterval, bool executeOnlyOnce) =>
            Register(waitObject, callBack, state, millisecondsTimeOutInterval, executeOnlyOnce, "millisecondsTimeOutInterval");

        public static RegisteredWaitHandle RegisterWaitForSingleObject(WaitHandle waitObject, WaitOrTimerCallback callBack, object state, uint millisecondsTimeOutInterval, bool executeOnlyOnce) =>
            Register(waitObject, callBack, state, millisecondsTimeOutInterval == uint.MaxValue ? -1 : checked((int)millisecondsTimeOutInterval), executeOnlyOnce, "millisecondsTimeOutInterval");

        public static RegisteredWaitHandle RegisterWaitForSingleObject(WaitHandle waitObject, WaitOrTimerCallback callBack, object state, long millisecondsTimeOutInterval, bool executeOnlyOnce)
        {
            if (millisecondsTimeOutInterval < -1) throw new ArgumentOutOfRangeException("millisecondsTimeOutInterval", "Number must be either non-negative and less than or equal to Int32.MaxValue or -1.");
            return Register(waitObject, callBack, state, millisecondsTimeOutInterval > int.MaxValue ? int.MaxValue : (int)millisecondsTimeOutInterval, executeOnlyOnce, "millisecondsTimeOutInterval");
        }

        public static RegisteredWaitHandle RegisterWaitForSingleObject(WaitHandle waitObject, WaitOrTimerCallback callBack, object state, TimeSpan timeout, bool executeOnlyOnce)
        {
            long milliseconds = (long)timeout.TotalMilliseconds;
            if (milliseconds < -1) throw new ArgumentOutOfRangeException("timeout", "Number must be either non-negative and less than or equal to Int32.MaxValue or -1.");
            if (milliseconds > int.MaxValue) throw new ArgumentOutOfRangeException("timeout", "Argument must be less than or equal to 2^31 - 1 milliseconds.");
            return Register(waitObject, callBack, state, (int)milliseconds, executeOnlyOnce, "timeout");
        }

        public static RegisteredWaitHandle UnsafeRegisterWaitForSingleObject(WaitHandle waitObject, WaitOrTimerCallback callBack, object state, int millisecondsTimeOutInterval, bool executeOnlyOnce) =>
            RegisterWaitForSingleObject(waitObject, callBack, state, millisecondsTimeOutInterval, executeOnlyOnce);

        public static RegisteredWaitHandle UnsafeRegisterWaitForSingleObject(WaitHandle waitObject, WaitOrTimerCallback callBack, object state, uint millisecondsTimeOutInterval, bool executeOnlyOnce) =>
            RegisterWaitForSingleObject(waitObject, callBack, state, millisecondsTimeOutInterval, executeOnlyOnce);

        public static RegisteredWaitHandle UnsafeRegisterWaitForSingleObject(WaitHandle waitObject, WaitOrTimerCallback callBack, object state, long millisecondsTimeOutInterval, bool executeOnlyOnce) =>
            RegisterWaitForSingleObject(waitObject, callBack, state, millisecondsTimeOutInterval, executeOnlyOnce);

        public static RegisteredWaitHandle UnsafeRegisterWaitForSingleObject(WaitHandle waitObject, WaitOrTimerCallback callBack, object state, TimeSpan timeout, bool executeOnlyOnce) =>
            RegisterWaitForSingleObject(waitObject, callBack, state, timeout, executeOnlyOnce);

        private static RegisteredWaitHandle Register(WaitHandle waitObject, WaitOrTimerCallback callBack, object state, int timeout, bool executeOnlyOnce, string timeoutName)
        {
            if (waitObject == null) throw new ArgumentNullException("waitObject");
            if (callBack == null) throw new ArgumentNullException("callBack");
            if (timeout < -1) throw new ArgumentOutOfRangeException(timeoutName, "Number must be either non-negative and less than or equal to Int32.MaxValue or -1.");
            var registration = new RegisteredWaitHandleImpl(waitObject, callBack, state, timeout, executeOnlyOnce);
            registration.Start();
            return (RegisteredWaitHandle)(object)registration;
        }
    }

    /// <summary>A handle to a wait object (WaitHandle.SafeWaitHandle): closes the operating-system event it wraps.</summary>
    [Shadows("Microsoft.Win32.SafeHandles.SafeWaitHandle")]
    public sealed class SafeWaitHandleImpl : SafeHandleZeroOrMinusOneIsInvalidImpl
    {
        public SafeWaitHandleImpl() : base(true) { }

        public SafeWaitHandleImpl(IntPtr existingHandle, bool ownsHandle) : base(ownsHandle) => SetHandle(existingHandle);

        protected override bool ReleaseHandle() => WaitHandleBridge.Release(handle);
    }

    internal static class EventHandles
    {
        /// <summary>The runtime event kind of an EventResetMode (csrt_event_new: 0 manual-reset, 1 auto-reset).</summary>
        public static int Kind(int mode)
        {
            if (mode == 0) return 1;
            if (mode == 1) return 0;
            throw new ArgumentException("Value of flags is invalid.", "mode");
        }
    }

    internal static unsafe class WaitHandleBridge
    {
        private static readonly object s_lock = new object();
        // Keyed by object: the table's enumerator tests keys against TKey, and csharpc can't test against WaitHandle.
        private static ConditionalWeakTable<object, SafeWaitHandle> s_handles;
        private static Dictionary<IntPtr, Bridge> s_bridges;

#if CSHARPC_TARGET_WINDOWS
        [DllImport("kernel32", SetLastError = true)] private static extern IntPtr CreateEventW(void* attributes, int manualReset, int initialState, void* name);
        [DllImport("kernel32", SetLastError = true)] private static extern int SetEvent(IntPtr handle);
        [DllImport("kernel32", SetLastError = true)] private static extern int CloseHandle(IntPtr handle);
        [DllImport("kernel32")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        private sealed class Bridge
        {
            private readonly WaitHandle _handle;
            private readonly IntPtr _os;
            private volatile bool _stop;

            public Bridge(WaitHandle handle, IntPtr os) { _handle = handle; _os = os; }

            public void Run()
            {
                // The OS event is auto-reset: one wake-up per signal.
                while (WaitForSingleObject(_os, 0xFFFFFFFF) == 0 && !_stop) CoreIntrinsics.ReinterpretAs<EventWaitHandle>(_handle).Set();
                CloseHandle(_os);
            }

            public void Stop()
            {
                _stop = true;
                SetEvent(_os); // wakes Run, which closes the event
            }
        }

        public static SafeWaitHandle Get(WaitHandle handle)
        {
            if (handle == null) throw new NullReferenceException();
            lock (s_lock)
            {
                s_handles ??= new ConditionalWeakTable<object, SafeWaitHandle>();
                s_bridges ??= new Dictionary<IntPtr, Bridge>();
                if (s_handles.TryGetValue(handle, out SafeWaitHandle existing)) return existing;
                IntPtr os = CreateEventW(null, 0, 0, null);
                if (os == IntPtr.Zero) throw new OutOfMemoryException("CreateEvent failed (error " + Marshal.GetLastPInvokeError() + ").");
                var bridge = new Bridge(handle, os);
                var thread = new Thread(bridge.Run);
                thread.IsBackground = true;
                thread.Start();
                var safe = new SafeWaitHandle(os, true);
                s_bridges[os] = bridge;
                s_handles.Add(handle, safe);
                return safe;
            }
        }

        public static bool Release(IntPtr handle)
        {
            Bridge bridge = null;
            lock (s_lock)
            {
                if (s_bridges != null && s_bridges.TryGetValue(handle, out bridge)) s_bridges.Remove(handle);
            }
            if (bridge != null)
            {
                bridge.Stop();
                return true;
            }
            return CloseHandle(handle) != 0;
        }
#else
        private sealed class Bridge { }

        public static SafeWaitHandle Get(WaitHandle handle) =>
            throw new NotSupportedException("WaitHandle.SafeWaitHandle is not supported by csharpc yet: a runtime event has no operating-system handle on this platform.");

        public static bool Release(IntPtr handle) => true;
#endif
    }
}
