// csharpc shadow corelib — infrastructure. See docs/10-corelib.md.
//
// Rules for corelib code:
//  * Declare a shadow with [Shadows("<CLR metadata name>")] (e.g. "System.Collections.Generic.List`1",
//    nested: "System.Collections.Generic.List`1+Enumerator"); same generic arity, same member names
//    and parameter types as the real BCL member. Codegen redirects real -> shadow (CodeGen/CoreLib.cs).
//  * Refer to BCL types by their REAL names (List<T>, KeyValuePair<K,V>, IEnumerable<T>...); codegen
//    maps them. Use the shadow name only inside its own class (or via a cast through object).
//  * Conservative C# (parsed with the user's LangVersion): no primary constructors, no collection
//    expressions, no static field initializers in generic types (use lazy init).
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
    internal sealed class ShadowsAttribute : Attribute
    {
        public ShadowsAttribute(string name) { }
    }

    /// <summary>Bodies synthesized by codegen per instantiation (FunctionCodeGen.CoreLib.cs).</summary>
    internal static class CoreIntrinsics
    {
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool AreEqual<T>(T a, T b);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int HashOf<T>(T value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int Compare<T>(T a, T b);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool IsValueType<T>();
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern object Box<T>(T value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern T Unbox<T>(object value);
    }

    internal static class ThrowHelper
    {
        public static void ArgumentOutOfRange(string paramName, string message) => throw new ArgumentOutOfRangeException(paramName, message);
        public static void IndexOutOfRange() =>
            throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
        public static void ArgumentNull(string paramName) => throw new ArgumentNullException(paramName);
        public static void Argument(string message) => throw new ArgumentException(message);
        public static void InvalidOperation(string message) => throw new InvalidOperationException(message);
        public static void VersionChanged() => throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
        public static void NoElements() => throw new InvalidOperationException("Sequence contains no elements");
        public static void NoMatch() => throw new InvalidOperationException("Sequence contains no matching element");
        public static void MoreThanOne() => throw new InvalidOperationException("Sequence contains more than one element");
        public static void MoreThanOneMatch() => throw new InvalidOperationException("Sequence contains more than one matching element");
        public static void NotSupported() => throw new NotSupportedException("Collection is read-only.");
        public static void KeyNotFound(string key) => throw new KeyNotFoundException("The given key '" + key + "' was not present in the dictionary.");
        public static void DuplicateKey(string key) => throw new ArgumentException("An item with the same key has already been added. Key: " + key);
        public static void EmptyQueue() => throw new InvalidOperationException("Queue empty.");
        public static void EmptyStack() => throw new InvalidOperationException("Stack empty.");
    }

    // ---------------------------------------------------------------------------------------------
    // Comparers
    // ---------------------------------------------------------------------------------------------

    [Shadows("System.Collections.Generic.EqualityComparer`1")]
    public abstract class EqualityComparerImpl<T> : IEqualityComparer<T>, System.Collections.IEqualityComparer
    {
        private static EqualityComparer<T> s_default;

        public static EqualityComparer<T> Default
        {
            get
            {
                if (s_default == null) s_default = (EqualityComparer<T>)(object)new DefaultEqualityComparer<T>();
                return s_default;
            }
        }

        public static EqualityComparer<T> Create(Func<T, T, bool> equals, Func<T, int> getHashCode) =>
            (EqualityComparer<T>)(object)new DelegateEqualityComparer<T>(equals, getHashCode);

        public abstract bool Equals(T x, T y);
        public abstract int GetHashCode(T obj);

        bool System.Collections.IEqualityComparer.Equals(object x, object y) => Equals(CoreIntrinsics.Unbox<T>(x), CoreIntrinsics.Unbox<T>(y));
        int System.Collections.IEqualityComparer.GetHashCode(object obj) => obj == null ? 0 : GetHashCode(CoreIntrinsics.Unbox<T>(obj));
    }

    internal sealed class DefaultEqualityComparer<T> : EqualityComparerImpl<T>
    {
        public override bool Equals(T x, T y) => CoreIntrinsics.AreEqual(x, y);
        public override int GetHashCode(T obj) => CoreIntrinsics.HashOf(obj);
    }

    internal sealed class DelegateEqualityComparer<T> : EqualityComparerImpl<T>
    {
        private readonly Func<T, T, bool> _equals;
        private readonly Func<T, int> _hash;
        public DelegateEqualityComparer(Func<T, T, bool> equals, Func<T, int> hash) { _equals = equals; _hash = hash; }
        public override bool Equals(T x, T y) => _equals(x, y);
        public override int GetHashCode(T obj) => _hash == null ? 0 : _hash(obj);
    }

    [Shadows("System.Collections.Generic.Comparer`1")]
    public abstract class ComparerImpl<T> : IComparer<T>, System.Collections.IComparer
    {
        private static Comparer<T> s_default;

        public static Comparer<T> Default
        {
            get
            {
                if (s_default == null) s_default = (Comparer<T>)(object)new DefaultComparer<T>();
                return s_default;
            }
        }

        public static Comparer<T> Create(Comparison<T> comparison) => (Comparer<T>)(object)new ComparisonComparer<T>(comparison);

        public abstract int Compare(T x, T y);

        int System.Collections.IComparer.Compare(object x, object y) => Compare(CoreIntrinsics.Unbox<T>(x), CoreIntrinsics.Unbox<T>(y));
    }

    internal sealed class DefaultComparer<T> : ComparerImpl<T>
    {
        public override int Compare(T x, T y) => CoreIntrinsics.Compare(x, y);
    }

    internal sealed class ComparisonComparer<T> : ComparerImpl<T>
    {
        private readonly Comparison<T> _comparison;
        public ComparisonComparer(Comparison<T> comparison) { _comparison = comparison; }
        public override int Compare(T x, T y) => _comparison(x, y);
    }

    // ---------------------------------------------------------------------------------------------
    // Sorting: a faithful port of .NET's ArraySortHelper<T> introsort, so that the (unstable) order
    // of equal elements matches .NET exactly. One copy uses the default comparer inline
    // (CoreIntrinsics.Compare), the other a Comparison<T>.
    // ---------------------------------------------------------------------------------------------

    internal static class SortHelper<T>
    {
        private const int IntrosortSizeThreshold = 16;

        public static void Sort(T[] keys, int index, int length, IComparer<T> comparer)
        {
            if (length < 2) return;
            if (comparer == null || IsDefault(comparer)) SortDefault(keys, index, length);
            else Sort(keys, index, length, (a, b) => comparer.Compare(a, b));
        }

        private static bool IsDefault(IComparer<T> comparer) => (object)comparer == (object)Comparer<T>.Default;

        private static int Log2(int n) { int r = 0; while ((n >>= 1) != 0) r++; return r; }

        // ---- default comparer ----------------------------------------------------------------

        public static void SortDefault(T[] keys, int index, int length)
        {
            if (length > 1) IntroSortDefault(keys, index, length, 2 * (Log2(length) + 1));
        }

        private static void SwapIfGreaterDefault(T[] k, int i, int j)
        {
            if (CoreIntrinsics.Compare(k[i], k[j]) > 0) { T t = k[i]; k[i] = k[j]; k[j] = t; }
        }

        private static void IntroSortDefault(T[] keys, int lo, int partitionSize, int depthLimit)
        {
            while (partitionSize > 1)
            {
                if (partitionSize <= IntrosortSizeThreshold)
                {
                    if (partitionSize == 2) { SwapIfGreaterDefault(keys, lo, lo + 1); return; }
                    if (partitionSize == 3)
                    {
                        SwapIfGreaterDefault(keys, lo, lo + 1);
                        SwapIfGreaterDefault(keys, lo, lo + 2);
                        SwapIfGreaterDefault(keys, lo + 1, lo + 2);
                        return;
                    }
                    for (int i = 0; i < partitionSize - 1; i++)
                    {
                        T t = keys[lo + i + 1];
                        int j = i;
                        while (j >= 0 && CoreIntrinsics.Compare(t, keys[lo + j]) < 0) { keys[lo + j + 1] = keys[lo + j]; j--; }
                        keys[lo + j + 1] = t;
                    }
                    return;
                }
                if (depthLimit == 0) { HeapSortDefault(keys, lo, partitionSize); return; }
                depthLimit--;
                int p = PartitionDefault(keys, lo, partitionSize);
                IntroSortDefault(keys, lo + p + 1, partitionSize - (p + 1), depthLimit);
                partitionSize = p;
            }
        }

        private static int PartitionDefault(T[] keys, int lo, int length)
        {
            int hi = length - 1;
            int middle = hi >> 1;
            SwapIfGreaterDefault(keys, lo, lo + middle);
            SwapIfGreaterDefault(keys, lo, lo + hi);
            SwapIfGreaterDefault(keys, lo + middle, lo + hi);
            T pivot = keys[lo + middle];
            T tmp = keys[lo + middle]; keys[lo + middle] = keys[lo + hi - 1]; keys[lo + hi - 1] = tmp;
            int left = 0, right = hi - 1;
            while (left < right)
            {
                while (CoreIntrinsics.Compare(keys[lo + ++left], pivot) < 0) { }
                while (CoreIntrinsics.Compare(pivot, keys[lo + --right]) < 0) { }
                if (left >= right) break;
                tmp = keys[lo + left]; keys[lo + left] = keys[lo + right]; keys[lo + right] = tmp;
            }
            if (left != hi - 1) { tmp = keys[lo + left]; keys[lo + left] = keys[lo + hi - 1]; keys[lo + hi - 1] = tmp; }
            return left;
        }

        private static void HeapSortDefault(T[] keys, int lo, int n)
        {
            for (int i = n >> 1; i >= 1; i--) DownHeapDefault(keys, lo, i, n);
            for (int i = n; i > 1; i--)
            {
                T t = keys[lo]; keys[lo] = keys[lo + i - 1]; keys[lo + i - 1] = t;
                DownHeapDefault(keys, lo, 1, i - 1);
            }
        }

        private static void DownHeapDefault(T[] keys, int lo, int i, int n)
        {
            T d = keys[lo + i - 1];
            while (i <= n >> 1)
            {
                int child = 2 * i;
                if (child < n && CoreIntrinsics.Compare(keys[lo + child - 1], keys[lo + child]) < 0) child++;
                if (!(CoreIntrinsics.Compare(d, keys[lo + child - 1]) < 0)) break;
                keys[lo + i - 1] = keys[lo + child - 1];
                i = child;
            }
            keys[lo + i - 1] = d;
        }

        // ---- Comparison<T> -------------------------------------------------------------------

        public static void Sort(T[] keys, int index, int length, Comparison<T> comparer)
        {
            if (length > 1) IntroSort(keys, index, length, 2 * (Log2(length) + 1), comparer);
        }

        private static void SwapIfGreater(T[] k, int i, int j, Comparison<T> c)
        {
            if (c(k[i], k[j]) > 0) { T t = k[i]; k[i] = k[j]; k[j] = t; }
        }

        private static void IntroSort(T[] keys, int lo, int partitionSize, int depthLimit, Comparison<T> c)
        {
            while (partitionSize > 1)
            {
                if (partitionSize <= IntrosortSizeThreshold)
                {
                    if (partitionSize == 2) { SwapIfGreater(keys, lo, lo + 1, c); return; }
                    if (partitionSize == 3)
                    {
                        SwapIfGreater(keys, lo, lo + 1, c);
                        SwapIfGreater(keys, lo, lo + 2, c);
                        SwapIfGreater(keys, lo + 1, lo + 2, c);
                        return;
                    }
                    for (int i = 0; i < partitionSize - 1; i++)
                    {
                        T t = keys[lo + i + 1];
                        int j = i;
                        while (j >= 0 && c(t, keys[lo + j]) < 0) { keys[lo + j + 1] = keys[lo + j]; j--; }
                        keys[lo + j + 1] = t;
                    }
                    return;
                }
                if (depthLimit == 0) { HeapSort(keys, lo, partitionSize, c); return; }
                depthLimit--;
                int p = Partition(keys, lo, partitionSize, c);
                IntroSort(keys, lo + p + 1, partitionSize - (p + 1), depthLimit, c);
                partitionSize = p;
            }
        }

        private static int Partition(T[] keys, int lo, int length, Comparison<T> c)
        {
            int hi = length - 1;
            int middle = hi >> 1;
            SwapIfGreater(keys, lo, lo + middle, c);
            SwapIfGreater(keys, lo, lo + hi, c);
            SwapIfGreater(keys, lo + middle, lo + hi, c);
            T pivot = keys[lo + middle];
            T tmp = keys[lo + middle]; keys[lo + middle] = keys[lo + hi - 1]; keys[lo + hi - 1] = tmp;
            int left = 0, right = hi - 1;
            while (left < right)
            {
                while (c(keys[lo + ++left], pivot) < 0) { }
                while (c(pivot, keys[lo + --right]) < 0) { }
                if (left >= right) break;
                tmp = keys[lo + left]; keys[lo + left] = keys[lo + right]; keys[lo + right] = tmp;
            }
            if (left != hi - 1) { tmp = keys[lo + left]; keys[lo + left] = keys[lo + hi - 1]; keys[lo + hi - 1] = tmp; }
            return left;
        }

        private static void HeapSort(T[] keys, int lo, int n, Comparison<T> c)
        {
            for (int i = n >> 1; i >= 1; i--) DownHeap(keys, lo, i, n, c);
            for (int i = n; i > 1; i--)
            {
                T t = keys[lo]; keys[lo] = keys[lo + i - 1]; keys[lo + i - 1] = t;
                DownHeap(keys, lo, 1, i - 1, c);
            }
        }

        private static void DownHeap(T[] keys, int lo, int i, int n, Comparison<T> c)
        {
            T d = keys[lo + i - 1];
            while (i <= n >> 1)
            {
                int child = 2 * i;
                if (child < n && c(keys[lo + child - 1], keys[lo + child]) < 0) child++;
                if (!(c(d, keys[lo + child - 1]) < 0)) break;
                keys[lo + i - 1] = keys[lo + child - 1];
                i = child;
            }
            keys[lo + i - 1] = d;
        }

        // ---- binary search (ArraySortHelper.InternalBinarySearch) ------------------------------

        public static int BinarySearch(T[] array, int index, int length, T value, IComparer<T> comparer)
        {
            int lo = index, hi = index + length - 1;
            bool useDefault = comparer == null || IsDefault(comparer);
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                int order = useDefault ? CoreIntrinsics.Compare(array[i], value) : comparer.Compare(array[i], value);
                if (order == 0) return i;
                if (order < 0) lo = i + 1; else hi = i - 1;
            }
            return ~lo;
        }
    }
}
