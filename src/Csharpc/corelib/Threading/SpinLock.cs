// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// System.Threading.SpinLock, a port of dotnet/runtime's SpinLock.cs (release/11.0): the same state word (owner
// thread id with tracking enabled, lock bit and waiter count without it), argument checks and exception
// messages. System.Net.Http's failed-proxy cache holds one.
#nullable disable
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.SpinLock")]
    public struct SpinLockImpl
    {
        private int _owner;

        private const int SLEEP_ONE_FREQUENCY = 40;
        private const int TIMEOUT_CHECK_FREQUENCY = 10;
        private const int LOCK_ID_DISABLE_MASK = unchecked((int)0x80000000);
        private const int LOCK_ANONYMOUS_OWNED = 0x1;
        private const int WAITERS_MASK = ~(LOCK_ID_DISABLE_MASK | 1);
        private const int ID_DISABLED_AND_ANONYMOUS_OWNED = unchecked((int)0x80000001);
        private const int LOCK_UNOWNED = 0;
        private const int MAXIMUM_WAITERS = WAITERS_MASK;

        private const string TimeoutOutOfRange = "The timeout must be a value between -1 and Int32.MaxValue, inclusive.";

        private static int CompareExchange(ref int location, int value, int comparand, ref bool success)
        {
            int result = Interlocked.CompareExchange(ref location, value, comparand);
            success = result == comparand;
            return result;
        }

        public SpinLockImpl(bool enableThreadOwnerTracking)
        {
            _owner = LOCK_UNOWNED;
            if (!enableThreadOwnerTracking) _owner |= LOCK_ID_DISABLE_MASK;
        }

        public void Enter(ref bool lockTaken)
        {
            int observedOwner = Volatile.Read(ref _owner);
            if (lockTaken
                || (observedOwner & ID_DISABLED_AND_ANONYMOUS_OWNED) != LOCK_ID_DISABLE_MASK
                || CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) != observedOwner)
                ContinueTryEnter(Timeout.Infinite, ref lockTaken);
        }

        public void TryEnter(ref bool lockTaken)
        {
            int observedOwner = Volatile.Read(ref _owner);
            if (((observedOwner & LOCK_ID_DISABLE_MASK) == 0) | lockTaken)
                ContinueTryEnter(0, ref lockTaken);
            else if ((observedOwner & LOCK_ANONYMOUS_OWNED) != 0)
                lockTaken = false;
            else
                CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken);
        }

        public void TryEnter(TimeSpan timeout, ref bool lockTaken)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException("timeout", timeout, TimeoutOutOfRange);
            TryEnter((int)timeout.TotalMilliseconds, ref lockTaken);
        }

        public void TryEnter(int millisecondsTimeout, ref bool lockTaken)
        {
            int observedOwner = Volatile.Read(ref _owner);
            if (millisecondsTimeout < -1
                || lockTaken
                || (observedOwner & ID_DISABLED_AND_ANONYMOUS_OWNED) != LOCK_ID_DISABLE_MASK
                || CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) != observedOwner)
                ContinueTryEnter(millisecondsTimeout, ref lockTaken);
        }

        private void ContinueTryEnter(int millisecondsTimeout, ref bool lockTaken)
        {
            if (lockTaken)
            {
                lockTaken = false;
                throw new ArgumentException("The tookLock argument must be set to false before calling this method.");
            }

            if (millisecondsTimeout < -1)
                throw new ArgumentOutOfRangeException("millisecondsTimeout", millisecondsTimeout, TimeoutOutOfRange);

            long startTime = 0;
            if (millisecondsTimeout != Timeout.Infinite && millisecondsTimeout != 0)
                startTime = Environment.TickCount64;

            if (IsThreadOwnerTrackingEnabled)
            {
                ContinueTryEnterWithThreadTracking(millisecondsTimeout, startTime, ref lockTaken);
                return;
            }

            int observedOwner;
            int turn = int.MaxValue;

            observedOwner = Volatile.Read(ref _owner);
            if ((observedOwner & LOCK_ANONYMOUS_OWNED) == LOCK_UNOWNED)
            {
                if (CompareExchange(ref _owner, observedOwner | 1, observedOwner, ref lockTaken) == observedOwner) return;
                if (millisecondsTimeout == 0) return;
            }
            else if (millisecondsTimeout == 0)
            {
                return;
            }
            else
            {
                if ((observedOwner & WAITERS_MASK) != MAXIMUM_WAITERS)
                    turn = (Interlocked.Add(ref _owner, 2) & WAITERS_MASK) >> 1;
            }

            SpinWaitImpl spinner = default;
            if (turn > Environment.ProcessorCount) spinner.Count = SpinWaitImpl.YieldThreshold;
            while (true)
            {
                spinner.SpinOnce(SLEEP_ONE_FREQUENCY);

                observedOwner = Volatile.Read(ref _owner);
                if ((observedOwner & LOCK_ANONYMOUS_OWNED) == LOCK_UNOWNED)
                {
                    int newOwner = (observedOwner & WAITERS_MASK) == 0 ? observedOwner | 1 : (observedOwner - 2) | 1;
                    if (CompareExchange(ref _owner, newOwner, observedOwner, ref lockTaken) == observedOwner) return;
                }

                if (spinner.Count % TIMEOUT_CHECK_FREQUENCY == 0)
                {
                    if (millisecondsTimeout != Timeout.Infinite && UpdateTimeOut(startTime, millisecondsTimeout) <= 0)
                    {
                        DecrementWaiters();
                        return;
                    }
                }
            }
        }

        // TimeoutHelper.UpdateTimeOut: the milliseconds left, or 0 once expired.
        private static int UpdateTimeOut(long startTime, int originalWaitMillisecondsTimeout)
        {
            long elapsed = Environment.TickCount64 - startTime;
            if (elapsed > int.MaxValue) return 0;
            int remaining = originalWaitMillisecondsTimeout - (int)elapsed;
            return remaining <= 0 ? 0 : remaining;
        }

        private void DecrementWaiters()
        {
            SpinWaitImpl spinner = default;
            while (true)
            {
                int observedOwner = Volatile.Read(ref _owner);
                if ((observedOwner & WAITERS_MASK) == 0) return;
                if (Interlocked.CompareExchange(ref _owner, observedOwner - 2, observedOwner) == observedOwner) break;
                spinner.SpinOnce();
            }
        }

        private void ContinueTryEnterWithThreadTracking(int millisecondsTimeout, long startTime, ref bool lockTaken)
        {
            const int LockUnowned = 0;
            int newOwner = Environment.CurrentManagedThreadId;
            if (Volatile.Read(ref _owner) == newOwner)
                throw new LockRecursionException("The calling thread already holds the lock.");

            SpinWaitImpl spinner = default;
            while (true)
            {
                spinner.SpinOnce();

                if (Volatile.Read(ref _owner) == LockUnowned)
                {
                    if (CompareExchange(ref _owner, newOwner, LockUnowned, ref lockTaken) == LockUnowned) return;
                }
                if (millisecondsTimeout == 0
                    || (millisecondsTimeout != Timeout.Infinite && spinner.NextSpinWillYield && UpdateTimeOut(startTime, millisecondsTimeout) <= 0))
                    return;
            }
        }

        public void Exit()
        {
            if ((Volatile.Read(ref _owner) & LOCK_ID_DISABLE_MASK) == 0) ExitSlowPath(true);
            else Interlocked.Decrement(ref _owner);
        }

        public void Exit(bool useMemoryBarrier)
        {
            int tmpOwner = Volatile.Read(ref _owner);
            if ((tmpOwner & LOCK_ID_DISABLE_MASK) != 0 & !useMemoryBarrier)
                Volatile.Write(ref _owner, tmpOwner & (~LOCK_ANONYMOUS_OWNED));
            else
                ExitSlowPath(useMemoryBarrier);
        }

        private void ExitSlowPath(bool useMemoryBarrier)
        {
            bool threadTrackingEnabled = (Volatile.Read(ref _owner) & LOCK_ID_DISABLE_MASK) == 0;
            if (threadTrackingEnabled && !IsHeldByCurrentThread)
                throw new SynchronizationLockException("The calling thread does not hold the lock.");

            if (useMemoryBarrier)
            {
                if (threadTrackingEnabled) Interlocked.Exchange(ref _owner, LOCK_UNOWNED);
                else Interlocked.Decrement(ref _owner);
            }
            else if (threadTrackingEnabled)
            {
                Volatile.Write(ref _owner, LOCK_UNOWNED);
            }
            else
            {
                int tmpOwner = Volatile.Read(ref _owner);
                Volatile.Write(ref _owner, tmpOwner & (~LOCK_ANONYMOUS_OWNED));
            }
        }

        public bool IsHeld
        {
            get
            {
                if (IsThreadOwnerTrackingEnabled) return Volatile.Read(ref _owner) != LOCK_UNOWNED;
                return (Volatile.Read(ref _owner) & LOCK_ANONYMOUS_OWNED) != LOCK_UNOWNED;
            }
        }

        public bool IsHeldByCurrentThread
        {
            get
            {
                if (!IsThreadOwnerTrackingEnabled) throw new InvalidOperationException("Thread tracking is disabled.");
                return (Volatile.Read(ref _owner) & (~LOCK_ID_DISABLE_MASK)) == Environment.CurrentManagedThreadId;
            }
        }

        public bool IsThreadOwnerTrackingEnabled => (Volatile.Read(ref _owner) & LOCK_ID_DISABLE_MASK) == 0;
    }
}
