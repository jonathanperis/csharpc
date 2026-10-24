// WeakReference, WeakReference<T> and ConditionalWeakTable<TKey, TValue> over the collector's weak
// slots (src/runtime/gc.c, "Weak references and ephemerons"). A slot is cleared when a collection
// finds its target unreachable; a ConditionalWeakTable entry is an ephemeron (its value lives while
// its key does). csharpc runs no finalizers, so a slot names its owner and the collector frees it when
// the owner dies, which is when .NET's finalizers would release the handle.
// ConditionalWeakTable follows dotnet/runtime (release/11.0) src/libraries/System.Private.CoreLib/src/
// System/Runtime/CompilerServices/ConditionalWeakTable.cs where it is observable: identity keys,
// enumeration in insertion order, exception types and messages. See THIRD-PARTY-NOTICES.md.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static class WeakSlots
    {
        [DllImport("__Internal")] public static extern IntPtr csrt_weak_alloc(IntPtr target, IntPtr value, IntPtr owner, int ephemeron);
        [DllImport("__Internal")] public static extern IntPtr csrt_weak_target(IntPtr handle);
        [DllImport("__Internal")] public static extern IntPtr csrt_weak_value(IntPtr handle);
        [DllImport("__Internal")] public static extern void csrt_weak_set(IntPtr handle, IntPtr target, IntPtr value);
        [DllImport("__Internal")] public static extern void csrt_weak_free(IntPtr handle);

        public static IntPtr Alloc(object target, object owner) =>
            csrt_weak_alloc(CoreIntrinsics.ObjectToPointer(target), IntPtr.Zero, CoreIntrinsics.ObjectToPointer(owner), 0);

        public static IntPtr AllocEphemeron(object key, object value, object owner) =>
            csrt_weak_alloc(CoreIntrinsics.ObjectToPointer(key), CoreIntrinsics.ObjectToPointer(value), CoreIntrinsics.ObjectToPointer(owner), 1);

        public static object Target(IntPtr handle) => CoreIntrinsics.PointerToObject(csrt_weak_target(handle));

        public static object Value(IntPtr handle) => CoreIntrinsics.PointerToObject(csrt_weak_value(handle));

        public static void Set(IntPtr handle, object target, object value) =>
            csrt_weak_set(handle, CoreIntrinsics.ObjectToPointer(target), CoreIntrinsics.ObjectToPointer(value));
    }

    [Shadows("System.WeakReference`1")]
    public sealed class WeakReferenceImpl<T> where T : class
    {
        private readonly IntPtr _handle;

        public WeakReferenceImpl(T target) : this(target, false) { }

        public WeakReferenceImpl(T target, bool trackResurrection)
        {
            // trackResurrection only matters to finalizers, which csharpc never runs.
            _handle = WeakSlots.Alloc(target, this);
        }

        public bool TryGetTarget(out T target)
        {
            T o = (T)WeakSlots.Target(_handle);
            target = o;
            return o != null;
        }

        public void SetTarget(T target) => WeakSlots.Set(_handle, target, null);
    }

    [Shadows("System.WeakReference")]
    public class WeakReferenceImpl
    {
        private readonly IntPtr _handle;
        private readonly bool _trackResurrection;

        public WeakReferenceImpl(object target) : this(target, false) { }

        public WeakReferenceImpl(object target, bool trackResurrection)
        {
            _handle = WeakSlots.Alloc(target, this);
            _trackResurrection = trackResurrection;
        }

        public virtual bool IsAlive => WeakSlots.Target(_handle) != null;

        public virtual bool TrackResurrection => _trackResurrection;

        public virtual object Target
        {
            get => WeakSlots.Target(_handle);
            set => WeakSlots.Set(_handle, value, null);
        }
    }

    [Shadows("System.Runtime.CompilerServices.ConditionalWeakTable`2")]
    public sealed class ConditionalWeakTableImpl<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
        where TKey : class
        where TValue : class
    {
        // Entries in insertion order (enumeration order); buckets chain them by the key's identity hash.
        // Each entry's ephemeron names `_lock` as its owner: the slots are freed with the table.
        private struct Entry
        {
            public IntPtr Handle; // 0: removed
            public int HashCode;
            public int Next;
        }

        private readonly object _lock = new object();
        private Entry[] _entries = new Entry[8];
        private int[] _buckets = NewBuckets(8);
        private int _count;

        private static int[] NewBuckets(int size)
        {
            int[] buckets = new int[size];
            for (int i = 0; i < size; i++) buckets[i] = -1;
            return buckets;
        }

        public ConditionalWeakTableImpl() { }

        private int FindEntry(TKey key, int hashCode)
        {
            for (int i = _buckets[hashCode & (_buckets.Length - 1)]; i >= 0; i = _entries[i].Next)
                if (_entries[i].HashCode == hashCode && _entries[i].Handle != IntPtr.Zero
                    && ReferenceEquals(WeakSlots.Target(_entries[i].Handle), key))
                    return i;
            return -1;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (key == null) throw new ArgumentNullException("key");
            lock (_lock)
            {
                int i = FindEntry(key, RuntimeHelpers.GetHashCode(key));
                if (i >= 0)
                {
                    value = (TValue)WeakSlots.Value(_entries[i].Handle);
                    return true;
                }
            }
            value = default(TValue);
            return false;
        }

        public void Add(TKey key, TValue value)
        {
            if (key == null) throw new ArgumentNullException("key");
            lock (_lock)
            {
                int hashCode = RuntimeHelpers.GetHashCode(key);
                if (FindEntry(key, hashCode) >= 0) throw new ArgumentException("An item with the same key has already been added.");
                CreateEntry(key, value, hashCode);
            }
        }

        public bool TryAdd(TKey key, TValue value)
        {
            if (key == null) throw new ArgumentNullException("key");
            lock (_lock)
            {
                int hashCode = RuntimeHelpers.GetHashCode(key);
                if (FindEntry(key, hashCode) >= 0) return false;
                CreateEntry(key, value, hashCode);
                return true;
            }
        }

        public void AddOrUpdate(TKey key, TValue value)
        {
            if (key == null) throw new ArgumentNullException("key");
            lock (_lock)
            {
                int hashCode = RuntimeHelpers.GetHashCode(key);
                int i = FindEntry(key, hashCode);
                if (i >= 0) WeakSlots.Set(_entries[i].Handle, key, value);
                else CreateEntry(key, value, hashCode);
            }
        }

        public bool Remove(TKey key) => Remove(key, out _);

        public bool Remove(TKey key, out TValue value)
        {
            if (key == null) throw new ArgumentNullException("key");
            lock (_lock)
            {
                int i = FindEntry(key, RuntimeHelpers.GetHashCode(key));
                if (i >= 0)
                {
                    value = (TValue)WeakSlots.Value(_entries[i].Handle);
                    ReleaseEntry(i);
                    return true;
                }
            }
            value = default(TValue);
            return false;
        }

        public void Clear()
        {
            lock (_lock)
            {
                for (int i = 0; i < _count; i++)
                    if (_entries[i].Handle != IntPtr.Zero) ReleaseEntry(i);
            }
        }

        public TValue GetValue(TKey key, ConditionalWeakTable<TKey, TValue>.CreateValueCallback createValueCallback)
        {
            if (createValueCallback == null) throw new ArgumentNullException("createValueCallback");
            return TryGetValue(key, out TValue existing) ? existing : GetValueLocked(key, createValueCallback(key));
        }

        public TValue GetOrCreateValue(TKey key) =>
            TryGetValue(key, out TValue existing) ? existing : GetValueLocked(key, Activator.CreateInstance<TValue>());

        public TValue GetOrAdd(TKey key, TValue value) =>
            TryGetValue(key, out TValue existing) ? existing : GetValueLocked(key, value);

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            if (valueFactory == null) throw new ArgumentNullException("valueFactory");
            return TryGetValue(key, out TValue existing) ? existing : GetValueLocked(key, valueFactory(key));
        }

        public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
        {
            if (valueFactory == null) throw new ArgumentNullException("valueFactory");
            return TryGetValue(key, out TValue existing) ? existing : GetValueLocked(key, valueFactory(key, factoryArgument));
        }

        /// <summary>Adds the created value unless another thread added one first (then that one wins, as in .NET).</summary>
        private TValue GetValueLocked(TKey key, TValue value)
        {
            lock (_lock)
            {
                int hashCode = RuntimeHelpers.GetHashCode(key);
                int i = FindEntry(key, hashCode);
                if (i >= 0) return (TValue)WeakSlots.Value(_entries[i].Handle);
                CreateEntry(key, value, hashCode);
                return value;
            }
        }

        private void CreateEntry(TKey key, TValue value, int hashCode)
        {
            if (_count == _entries.Length) Resize();
            int i = _count++;
            _entries[i].Handle = WeakSlots.AllocEphemeron(key, value, _lock);
            _entries[i].HashCode = hashCode;
            int bucket = hashCode & (_buckets.Length - 1);
            _entries[i].Next = _buckets[bucket];
            _buckets[bucket] = i;
        }

        private void ReleaseEntry(int i)
        {
            WeakSlots.csrt_weak_free(_entries[i].Handle);
            _entries[i].Handle = IntPtr.Zero;
        }

        /// <summary>Drops removed entries and entries whose key was collected, then grows if still full.</summary>
        private void Resize()
        {
            int live = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].Handle == IntPtr.Zero) continue;
                if (WeakSlots.Target(_entries[i].Handle) == null) { ReleaseEntry(i); continue; }
                live++;
            }
            int size = _entries.Length;
            if (live * 2 >= size) size *= 2;
            Entry[] entries = new Entry[size];
            int[] buckets = NewBuckets(size);
            int count = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].Handle == IntPtr.Zero) continue;
                entries[count] = _entries[i];
                int bucket = entries[count].HashCode & (size - 1);
                entries[count].Next = buckets[bucket];
                buckets[bucket] = count;
                count++;
            }
            _entries = entries;
            _buckets = buckets;
            _count = count;
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            // A snapshot of the live entries (.NET's enumerator also tolerates concurrent changes).
            List<KeyValuePair<TKey, TValue>> items = new List<KeyValuePair<TKey, TValue>>();
            lock (_lock)
            {
                for (int i = 0; i < _count; i++)
                {
                    if (_entries[i].Handle == IntPtr.Zero) continue;
                    TKey key = (TKey)WeakSlots.Target(_entries[i].Handle);
                    if (key != null) items.Add(new KeyValuePair<TKey, TValue>(key, (TValue)WeakSlots.Value(_entries[i].Handle)));
                }
            }
            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
