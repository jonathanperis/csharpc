// Runtime-facing BCL types that framework libraries compiled from IL use (System.Reflection.Metadata pins
// its byte arrays, rents decoding buffers and derives its disposables from CriticalFinalizerObject).
// csharpc's GC is conservative and non-moving (ADR 0004) and runs no finalizers.
#nullable disable
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace CsharpcCoreLib
{
    /// <summary>A base class whose only runtime meaning is "finalize reliably": csharpc runs no finalizers.</summary>
    [Shadows("System.Runtime.ConstrainedExecution.CriticalFinalizerObject")]
    public abstract class CriticalFinalizerObjectImpl
    {
        protected CriticalFinalizerObjectImpl() { }
    }

    /// <summary>
    /// GCHandle as an index into a process-wide table of strong references. Pinning is free (objects never
    /// move), so a pinned handle only remembers that AddrOfPinnedObject is allowed. Weak handles would need
    /// GC support (clearing on collection); they throw instead of silently keeping the target alive.
    /// </summary>
    [Shadows("System.Runtime.InteropServices.GCHandle")]
    public unsafe struct GCHandleImpl : IEquatable<GCHandle>
    {
        // (index + 1) << 1 | pinned; 0 is "not allocated", as in .NET.
        private IntPtr _handle;

        private static object s_lock;
        private static object[] s_targets;
        private static int[] s_free;
        private static int s_freeCount, s_used;

        private GCHandleImpl(IntPtr handle) { _handle = handle; }

        private static GCHandle To(GCHandleImpl h) => *(GCHandle*)&h;
        private static GCHandleImpl Of(GCHandle h) => *(GCHandleImpl*)&h;

        private static object Lock
        {
            get
            {
                object l = s_lock;
                if (l == null) { l = new object(); l = Interlocked.CompareExchange(ref s_lock, l, null) ?? l; }
                return l;
            }
        }

        public static GCHandle Alloc(object value) => Alloc(value, GCHandleType.Normal);

        public static GCHandle Alloc(object value, GCHandleType type)
        {
            if ((uint)type > (uint)GCHandleType.Pinned) throw new ArgumentOutOfRangeException("type", "Specified argument was out of the range of valid values.");
            if (type == GCHandleType.Weak || type == GCHandleType.WeakTrackResurrection)
                throw new NotSupportedException("Weak GC handles are not supported by csharpc yet.");
            int index;
            lock (Lock)
            {
                if (s_freeCount > 0) index = s_free[--s_freeCount];
                else
                {
                    if (s_targets == null) { s_targets = new object[16]; s_free = new int[16]; }
                    else if (s_used == s_targets.Length)
                    {
                        object[] grown = new object[s_used * 2];
                        Array.Copy(s_targets, grown, s_used);
                        s_targets = grown;
                        int[] free = new int[s_used * 2];
                        Array.Copy(s_free, free, s_freeCount);
                        s_free = free;
                    }
                    index = s_used++;
                }
                s_targets[index] = value;
            }
            return To(new GCHandleImpl((IntPtr)(((long)index + 1) << 1 | (type == GCHandleType.Pinned ? 1L : 0L))));
        }

        private int Index
        {
            get
            {
                if (_handle == IntPtr.Zero) throw new InvalidOperationException("Handle is not initialized.");
                return (int)(((long)_handle >> 1) - 1);
            }
        }

        public bool IsAllocated => _handle != IntPtr.Zero;

        public object Target
        {
            get { int i = Index; lock (Lock) return s_targets[i]; }
            set { int i = Index; lock (Lock) s_targets[i] = value; }
        }

        public void Free()
        {
            int i = Index;
            _handle = IntPtr.Zero;
            lock (Lock)
            {
                s_targets[i] = null;
                s_free[s_freeCount++] = i;
            }
        }

        public IntPtr AddrOfPinnedObject()
        {
            int i = Index;
            if (((long)_handle & 1) == 0) throw new InvalidOperationException("Handle is not pinned.");
            object target;
            lock (Lock) target = s_targets[i];
            if (target == null) return IntPtr.Zero;
            if (target is string s)
            {
                fixed (char* chars = s) return (IntPtr)chars;
            }
            byte* address = CoreIntrinsics.ObjectAddress(target);
            // Arrays: { method table, i64 length, elements } (csrt.h); other objects: the first field.
            return (IntPtr)(target is Array ? address + 16 : address + sizeof(IntPtr));
        }

        public static IntPtr ToIntPtr(GCHandle value) => Of(value)._handle;

        public static GCHandle FromIntPtr(IntPtr value)
        {
            if (value == IntPtr.Zero) throw new InvalidOperationException("Handle is not initialized.");
            return To(new GCHandleImpl(value));
        }

        // Operators must be declared on the shadow's own type; they map to GCHandle's like every member.
        public static explicit operator GCHandleImpl(IntPtr value) => Of(FromIntPtr(value));
        public static explicit operator IntPtr(GCHandleImpl value) => value._handle;
        public static bool operator ==(GCHandleImpl a, GCHandleImpl b) => a._handle == b._handle;
        public static bool operator !=(GCHandleImpl a, GCHandleImpl b) => a._handle != b._handle;

        public override bool Equals(object o) => o is GCHandle other && _handle == Of(other)._handle;
        public bool Equals(GCHandle other) => _handle == Of(other)._handle;
        public override int GetHashCode() => _handle.GetHashCode();
    }

    /// <summary>
    /// ArrayPool&lt;T&gt;: the shared pool's sizing and validation are .NET's (power-of-two buckets from 16
    /// elements to 2^30, exact sizes above, Array.Empty for 0), but nothing is cached: every Rent returns
    /// a fresh zeroed array, which is one of the outcomes .NET allows (pooled arrays may hold any data).
    /// </summary>
    [Shadows("System.Buffers.ArrayPool`1")]
    public abstract class ArrayPoolImpl<T>
    {
        private static ArrayPool<T> s_shared;

        public static ArrayPool<T> Shared
        {
            get
            {
                ArrayPool<T> pool = s_shared;
                if (pool == null) { pool = new SharedArrayPoolImpl<T>(); pool = Interlocked.CompareExchange(ref s_shared, pool, null) ?? pool; }
                return pool;
            }
        }

        public static ArrayPool<T> Create() => new SharedArrayPoolImpl<T>();
        public static ArrayPool<T> Create(int maxArrayLength, int maxArraysPerBucket) => new SharedArrayPoolImpl<T>();

        public abstract T[] Rent(int minimumLength);
        public abstract void Return(T[] array, bool clearArray = false);
    }

    internal sealed class SharedArrayPoolImpl<T> : ArrayPool<T>
    {
        private const int NumBuckets = 27;

        // .NET's Utilities.SelectBucketIndex: Log2((uint)size - 1 | 15) - 3.
        private static int SelectBucketIndex(int size)
        {
            uint v = (uint)size - 1 | 15;
            int log = 0;
            while ((v >>= 1) != 0) log++;
            return log - 3;
        }

        public override T[] Rent(int minimumLength)
        {
            int bucket = SelectBucketIndex(minimumLength);
            if ((uint)bucket < NumBuckets) return new T[16 << bucket];
            if (minimumLength == 0) return Array.Empty<T>();
            if (minimumLength < 0) throw new ArgumentOutOfRangeException("minimumLength");
            return new T[minimumLength];
        }

        public override void Return(T[] array, bool clearArray = false)
        {
            if (array == null) throw new ArgumentNullException("array");
            int bucket = SelectBucketIndex(array.Length);
            if ((uint)bucket < NumBuckets)
            {
                if (clearArray) Array.Clear(array);
                if (array.Length != 16 << bucket)
                    throw new ArgumentException("The buffer is not associated with this pool and may not be returned to it.", "array");
            }
        }
    }

    /// <summary>ReferenceEqualityComparer: identity equality and RuntimeHelpers.GetHashCode (one shared instance).</summary>
    [Shadows("System.Collections.Generic.ReferenceEqualityComparer")]
    public sealed class ReferenceEqualityComparerImpl : System.Collections.Generic.IEqualityComparer<object>, System.Collections.IEqualityComparer
    {
        private static ReferenceEqualityComparerImpl s_instance;

        private ReferenceEqualityComparerImpl() { }

        public static ReferenceEqualityComparerImpl Instance
        {
            get
            {
                ReferenceEqualityComparerImpl instance = s_instance;
                if (instance == null) { instance = new ReferenceEqualityComparerImpl(); instance = Interlocked.CompareExchange(ref s_instance, instance, null) ?? instance; }
                return instance;
            }
        }

        public new bool Equals(object x, object y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>StrongBox&lt;T&gt;: a heap cell holding one value (Roslyn shares mutable state through it).</summary>
    [Shadows("System.Runtime.CompilerServices.StrongBox`1")]
    public class StrongBoxImpl<T> : System.Runtime.CompilerServices.IStrongBox
    {
        public T Value;

        public StrongBoxImpl() { }

        public StrongBoxImpl(T value) { Value = value; }

        object System.Runtime.CompilerServices.IStrongBox.Value
        {
            get => Value;
            set => Value = (T)value;
        }
    }

    /// <summary>
    /// SpinWait, as dotnet/runtime's: the count and the yield threshold are observable (Count,
    /// NextSpinWillYield); how a spin waits (busy spin, yield, sleep) only affects timing.
    /// </summary>
    [Shadows("System.Threading.SpinWait")]
    public struct SpinWaitImpl
    {
        internal const int YieldThreshold = 10;
        private const int Sleep0EveryHowManyYields = 5;
        internal const int DefaultSleep1Threshold = 20;

        private int _count;

        public int Count
        {
            get => _count;
            internal set => _count = value;
        }

        public bool NextSpinWillYield => _count >= YieldThreshold || Environment.ProcessorCount == 1;

        public void SpinOnce() => SpinOnceCore(DefaultSleep1Threshold);

        public void SpinOnce(int sleep1Threshold)
        {
            if (sleep1Threshold < -1) throw new ArgumentOutOfRangeException("sleep1Threshold", sleep1Threshold, "Number must be either non-negative and less than or equal to Int32.MaxValue or -1.");
            if (sleep1Threshold >= 0 && sleep1Threshold < YieldThreshold) sleep1Threshold = YieldThreshold;
            SpinOnceCore(sleep1Threshold);
        }

        private void SpinOnceCore(int sleep1Threshold)
        {
            if ((_count >= YieldThreshold && ((_count >= sleep1Threshold && sleep1Threshold >= 0) || (_count - YieldThreshold) % 2 == 0))
                || Environment.ProcessorCount == 1)
            {
                if (_count >= sleep1Threshold && sleep1Threshold >= 0) Thread.Sleep(1);
                else
                {
                    int yieldsSoFar = _count >= YieldThreshold ? (_count - YieldThreshold) / 2 : _count;
                    if ((yieldsSoFar % Sleep0EveryHowManyYields) == (Sleep0EveryHowManyYields - 1)) Thread.Sleep(0);
                    else Thread.Yield();
                }
            }
            else
            {
                Thread.SpinWait(8 << Math.Min(_count, 10));
            }
            _count = _count == int.MaxValue ? YieldThreshold : _count + 1;
        }

        public void Reset() => _count = 0;

        public static void SpinUntil(Func<bool> condition) => SpinUntil(condition, Timeout.Infinite);

        public static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException("timeout", timeout, "The timeout must represent a value between -1 and Int32.MaxValue, inclusive.");
            return SpinUntil(condition, (int)totalMilliseconds);
        }

        public static bool SpinUntil(Func<bool> condition, int millisecondsTimeout)
        {
            if (millisecondsTimeout < Timeout.Infinite)
                throw new ArgumentOutOfRangeException("millisecondsTimeout", millisecondsTimeout, "The timeout must represent a value between -1 and Int32.MaxValue, inclusive.");
            if (condition == null) throw new ArgumentNullException("condition");
            long start = millisecondsTimeout == 0 || millisecondsTimeout == Timeout.Infinite ? 0 : Environment.TickCount64;
            SpinWaitImpl spinner = default;
            while (!condition())
            {
                if (millisecondsTimeout == 0) return false;
                spinner.SpinOnce();
                if (millisecondsTimeout != Timeout.Infinite && spinner.NextSpinWillYield && millisecondsTimeout <= Environment.TickCount64 - start)
                    return false;
            }
            return true;
        }
    }

    // What NativeAOT answers: no code is generated at run time, so Microsoft.Extensions.DependencyInjection,
    // System.Linq.Expressions and ASP.NET Core take their reflection-free paths.
    [ShadowMembers("System.Runtime.CompilerServices.RuntimeFeature")]
    internal static class RuntimeFeatureMembers
    {
        public static bool IsDynamicCodeSupported => false;
        public static bool IsDynamicCodeCompiled => false;
    }

    [ShadowMembers("System.Runtime.CompilerServices.RuntimeHelpers")]
    internal static unsafe class RuntimeHelpersMembers
    {
        [DllImport("__Internal")] private static extern nuint csrt_stack_remaining();

        // .NET's minimum for "sufficient": 128 KiB on 64-bit targets, 64 KiB on 32-bit ones.
        private static nuint MinimumStack => (nuint)(IntPtr.Size == 8 ? 128 * 1024 : 64 * 1024);

        public static bool TryEnsureSufficientExecutionStack() => csrt_stack_remaining() >= MinimumStack;

        public static void EnsureSufficientExecutionStack()
        {
            if (csrt_stack_remaining() < MinimumStack)
                throw new InsufficientExecutionStackException("Insufficient stack to continue executing the program safely. This can happen from having too many functions on the call stack or function on the stack using too much stack space.");
        }
    }

    /// <summary>Without a managed debugger (gdb and lldb aren't one), as .NET reports it.</summary>
    [ShadowMembers("System.Diagnostics.Debugger")]
    internal static class DebuggerMembers
    {
        public static bool IsAttached => false;
        public static bool IsLogging() => false;
        public static void Log(int level, string category, string message) { }
    }

    [Shadows("System.Threading.Timeout")]
    public static class TimeoutImpl
    {
        public const int Infinite = -1;
        public static readonly TimeSpan InfiniteTimeSpan = new TimeSpan(0, 0, 0, 0, Infinite);
    }
}
