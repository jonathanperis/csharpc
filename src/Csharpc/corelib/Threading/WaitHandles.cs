// WaitHandle.WaitAny / WaitAll and CancellationToken.WaitHandle, over the runtime's events (a WaitHandle is a
// csrt event: src/runtime/threads.c has no multi-object wait). The argument checks, messages and results are
// .NET's (WaitHandle.cs, release/11.0): the lowest signaled index for WaitAny, WaitTimeout (258) on timeout,
// WaitAll takes every handle or none. A wait that has to block sleeps on the first handle in short slices
// and re-checks the others between them, so a signal on another handle is seen within ~1-4 ms (WaitAny over
// several handles is what BlockingCollection.AddToAny/TakeFromAny and a token's WaitHandle use).
#nullable disable
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Threading.WaitHandle")]
    internal static class WaitHandleMembers
    {
        private const int MaxWaitHandles = 64;
        private const int WaitTimeout = 0x102;

        // The runtime event behind a handle: manual 0, auto 1, semaphore 2, countdown 3 (threads.c). A wait takes
        // (resets, or decrements) an auto-reset event and a semaphore, which a failed WaitAll must give back.
        private static bool IsConsumed(WaitHandle handle) => CoreIntrinsics.csrt_event_kind(handle) is 1 or 2;
        private static void Restore(WaitHandle handle) => CoreIntrinsics.csrt_event_set(handle, 1);

        public static int WaitAny(WaitHandle[] waitHandles) => WaitAnyCore(waitHandles, Timeout.Infinite);
        public static int WaitAny(WaitHandle[] waitHandles, int millisecondsTimeout) => WaitAnyCore(waitHandles, CheckTimeout(millisecondsTimeout));
        public static int WaitAny(WaitHandle[] waitHandles, TimeSpan timeout) => WaitAnyCore(waitHandles, ToTimeoutMilliseconds(timeout));
        public static int WaitAny(WaitHandle[] waitHandles, int millisecondsTimeout, bool exitContext) => WaitAnyCore(waitHandles, CheckTimeout(millisecondsTimeout));
        public static int WaitAny(WaitHandle[] waitHandles, TimeSpan timeout, bool exitContext) => WaitAnyCore(waitHandles, ToTimeoutMilliseconds(timeout));

        public static bool WaitAll(WaitHandle[] waitHandles) => WaitAllCore(waitHandles, Timeout.Infinite);
        public static bool WaitAll(WaitHandle[] waitHandles, int millisecondsTimeout) => WaitAllCore(waitHandles, CheckTimeout(millisecondsTimeout));
        public static bool WaitAll(WaitHandle[] waitHandles, TimeSpan timeout) => WaitAllCore(waitHandles, ToTimeoutMilliseconds(timeout));
        public static bool WaitAll(WaitHandle[] waitHandles, int millisecondsTimeout, bool exitContext) => WaitAllCore(waitHandles, CheckTimeout(millisecondsTimeout));
        public static bool WaitAll(WaitHandle[] waitHandles, TimeSpan timeout, bool exitContext) => WaitAllCore(waitHandles, ToTimeoutMilliseconds(timeout));

        private static int CheckTimeout(int millisecondsTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            return millisecondsTimeout;
        }

        private static int ToTimeoutMilliseconds(TimeSpan timeout)
        {
            long milliseconds = (long)timeout.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(milliseconds, -1, "timeout");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(milliseconds, int.MaxValue, "timeout");
            return (int)milliseconds;
        }

        private static void Validate(WaitHandle[] waitHandles, bool checkForDuplicates)
        {
            ArgumentNullException.ThrowIfNull(waitHandles);
            if (waitHandles.Length == 0) throw new ArgumentException("Waithandle array may not be empty.", "waitHandles");
            if (waitHandles.Length > MaxWaitHandles) throw new NotSupportedException("The number of WaitHandles must be less than or equal to 64.");
            for (int i = 0; i < waitHandles.Length; i++)
                if (waitHandles[i] == null) throw new ArgumentNullException("waitHandles[" + i + "]", "At least one element in the specified array was null.");
            if (checkForDuplicates)
                for (int i = 1; i < waitHandles.Length; i++)
                    for (int j = 0; j < i; j++)
                        if (ReferenceEquals(waitHandles[i], waitHandles[j])) throw new DuplicateWaitObjectException("waitHandles[" + i + "]");
        }

        // How long one blocking slice lasts: short at first (a signal usually follows soon), then longer.
        private static int Slice(int rounds) => rounds < 100 ? 1 : 4;

        private static int WaitAnyCore(WaitHandle[] waitHandles, int millisecondsTimeout)
        {
            Validate(waitHandles, false);
            int found = FirstSignaled(waitHandles, 0);
            if (found >= 0) return found;
            if (millisecondsTimeout == 0) return WaitTimeout;
            long start = Environment.TickCount64;
            for (int rounds = 0; ; rounds++)
            {
                int slice = Slice(rounds);
                if (millisecondsTimeout != Timeout.Infinite)
                {
                    long remaining = millisecondsTimeout - (Environment.TickCount64 - start);
                    if (remaining <= 0)
                    {
                        found = FirstSignaled(waitHandles, 0);
                        return found >= 0 ? found : WaitTimeout;
                    }
                    if (remaining < slice) slice = (int)remaining;
                }
                if (waitHandles[0].WaitOne(slice)) return 0;
                found = FirstSignaled(waitHandles, 1);
                if (found >= 0) return found;
            }
        }

        /// <summary>The lowest index from `first` whose handle is signaled (an auto-reset one is consumed), else -1.</summary>
        private static int FirstSignaled(WaitHandle[] waitHandles, int first)
        {
            for (int i = first; i < waitHandles.Length; i++)
                if (waitHandles[i].WaitOne(0)) return i;
            return -1;
        }

        private static bool WaitAllCore(WaitHandle[] waitHandles, int millisecondsTimeout)
        {
            Validate(waitHandles, true);
            if (TryTakeAll(waitHandles)) return true;
            if (millisecondsTimeout == 0) return false;
            long start = Environment.TickCount64;
            for (int rounds = 0; ; rounds++)
            {
                int slice = Slice(rounds);
                if (millisecondsTimeout != Timeout.Infinite)
                {
                    long remaining = millisecondsTimeout - (Environment.TickCount64 - start);
                    if (remaining <= 0) return TryTakeAll(waitHandles);
                    if (remaining < slice) slice = (int)remaining;
                }
                // Sleep on the first handle that isn't signaled yet; an auto-reset event that woke it was consumed: signaled again.
                WaitHandle sleeper = waitHandles[FirstUnsignaled(waitHandles)];
                if (sleeper.WaitOne(slice) && IsConsumed(sleeper)) Restore(sleeper);
                if (TryTakeAll(waitHandles)) return true;
            }
        }

        private static int FirstUnsignaled(WaitHandle[] waitHandles)
        {
            for (int i = 0; i < waitHandles.Length; i++)
                if (!IsSignaled(waitHandles[i])) return i;
            return 0;
        }

        // A manual-reset event or a countdown stays signaled: its state can be read without taking it.
        private static bool IsSignaled(WaitHandle handle) => !IsConsumed(handle) && handle.WaitOne(0);

        // All handles at once or none: an auto-reset event taken before a handle that isn't signaled is signaled
        // again (the other kinds are not consumed by a wait).
        private static bool TryTakeAll(WaitHandle[] waitHandles)
        {
            for (int i = 0; i < waitHandles.Length; i++)
            {
                if (waitHandles[i].WaitOne(0)) continue;
                for (int j = 0; j < i; j++)
                    if (IsConsumed(waitHandles[j])) Restore(waitHandles[j]);
                return false;
            }
            return true;
        }
    }

    /// <summary>CancellationToken.WaitHandle: one ManualResetEvent per source, set when it is canceled.</summary>
    internal static class CancellationWaitHandles
    {
        private sealed class Box { internal ManualResetEvent Event; }

        private static readonly ConditionalWeakTable<object, Box> s_handles = new ConditionalWeakTable<object, Box>();
        private static readonly object s_lock = new object();
        private static ManualResetEvent s_never;

        public static WaitHandle Get(CancellationToken token)
        {
            object source = CoreIntrinsics.TokenSource(token);
            if (source == null)
            {
                lock (s_lock) return s_never ??= new ManualResetEvent(false); // CancellationToken.None: never canceled
            }
            CoreIntrinsics.csrt_cts_token(CoreIntrinsics.ReinterpretAs<CancellationTokenSource>(source)); // ObjectDisposedException once disposed
            lock (s_lock)
            {
                if (s_handles.TryGetValue(source, out Box existing)) return existing.Event;
                var box = new Box { Event = new ManualResetEvent(token.IsCancellationRequested) };
                if (!token.IsCancellationRequested) token.Register(() => box.Event.Set());
                s_handles.Add(source, box);
                return box.Event;
            }
        }
    }
}
