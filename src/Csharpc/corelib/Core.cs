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
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern T CastOrThrow<T>(object value);
        /// <summary>csrt_string_compare_with: culture and ordinal comparisons (StringComparison 0..4).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int CompareStringsWith(string a, string b, int comparison);
        /// <summary>A new string of `length` chars whose contents are NOT initialized: write every char before use.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern string FastAllocateString(int length);
        /// <summary>The chars of a string as a writable span — only for strings that haven't escaped yet.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern Span<char> StringChars(string s);
        /// <summary>The object's address (its method-table header): GCHandle.AddrOfPinnedObject; objects never move.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern unsafe byte* ObjectAddress(object o);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern long DoubleToBits(double value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double BitsToDouble(long bits);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int SingleToBits(float value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float BitsToSingle(int bits);
        /// <summary>decimal's raw fields: flags (sign bit 31, scale in bits 16-23), high 32 and low 64 bits of the 96-bit integer.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int DecimalFlags(decimal value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern uint DecimalHi(decimal value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern ulong DecimalLo(decimal value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern decimal MakeDecimal(int flags, uint hi, ulong lo);
        /// <summary>DateTime's raw `_dateData`: ticks in bits 0-61, the kind in bits 62-63 (DateTimeOps.cs).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern long DateTimeData(DateTime value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern DateTime MakeDateTime(long data);
        /// <summary>An object reference as an IntPtr and back, for runtime slots that hold managed objects (GC-visible ones only).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern IntPtr ObjectToPointer(object value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern object PointerToObject(IntPtr pointer);
        /// <summary>
        /// The frames .NET's own library lists for this exception below the program's frames, innermost
        /// first, one per line (a constant): `throw LibraryFrames(new X(...), "System.Ns.Type.Method(Int32 a)")`.
        /// Copy them from .NET 11's output; they only appear when the program records stack traces.
        /// </summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern Exception LibraryFrames(Exception exception, string frames);
        /// <summary>
        /// LibraryFrames for an exception already in flight, from a `when` filter (which runs before the
        /// program's frames are recorded): appends the frames (a constant) and returns false.
        /// </summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool AppendLibraryFrames(Exception exception, string frames);
        /// <summary>csrt_exception_capture_trace: the stack-trace frames recorded so far (null: none).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern object CaptureStackTrace(Exception exception);
        /// <summary>csrt_exception_throw_captured: throws with the captured frames, .NET's marker line, then new frames.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern void ThrowWithStackTrace(Exception exception, object captured);
        /// <summary>csrt_unhandled_exception: prints "Unhandled exception. ..." and ends the process (never returns).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern void UnhandledException(Exception exception);
        /// <summary>csrt_is_iterator: an iterator/async iterator compiled from source (an LLVM coroutine object).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool IsCoroutineIterator(object o);
        /// <summary>The string resources of the IL assemblies, encoded (ModuleCodeGen.Resources.cs).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern string ResourceStrings();
        /// <summary>runtimeconfig.json configProperties, encoded (ModuleCodeGen.Resources.cs).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern string RuntimeConfig();
        /// <summary>csrt_task_try_fault_async: faults a pending task, or cancels it for an OperationCanceledException.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool TryFaultAsyncTask(System.Threading.Tasks.Task task, Exception exception);
        /// <summary>An enum's underlying value as .NET's EnumConverter keys it: sign-extended, then reinterpreted as ulong.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern ulong EnumToUInt64<T>(T value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern T EnumFromUInt64<T>(ulong value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool IsSignedEnum<T>();
        /// <summary>T is an interface or an abstract class (compile-time constant).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool IsAbstractType<T>();
        /// <summary>Whether a run-time Type is one of the program's enum types (Type.IsEnum for JsonStringEnumConverter).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool IsEnumType(Type type);
        /// <summary>Type.IsInstanceOfType: obj's class, a base class or an implemented interface is `type`.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool IsInstanceOfType(Type type, object obj);
        /// <summary>JsonMetadataServicesImpl's converter for a built-in T (selected at compile time), or null.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern object BuiltInJsonConverter<T>();
        /// <summary>to.IsAssignableFrom(from) for class and interface types (base chain and interface map).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool IsTypeAssignableTo(Type from, Type to);
        /// <summary>Type.IsInterface.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool IsInterfaceTypeObject(Type type);
        /// <summary>CsharpcCoreLib.JsonEnumFactory.Create&lt;E&gt;(...) for the enum E that `type` is, or null.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern object CreateEnumConverter(Type type, int converterOptions, object namingPolicy, object options);
        /// <summary>typeof(T).IsDefined(typeof(FlagsAttribute)), resolved at compile time.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool IsFlagsEnum<T>();
        /// <summary>[JsonStringEnumMemberName] of each member, in Enum.GetNames&lt;T&gt;() order (null where absent).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern string[] EnumMemberJsonNames<T>();
    }

    /// <summary>
    /// On a static method of a [ShadowMembers("T")] class: implements the library *instance* method of
    /// the same name whose parameters are this method's parameters after the first (the receiver).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ShadowInstanceAttribute : Attribute { }

    /// <summary>
    /// On a class: its methods are compiled for size (LLVM minsize). For code that runs once per culture
    /// or per process (building culture data), not per operation: smaller, and faster to compile.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    internal sealed class OptimizeForSizeAttribute : Attribute { }

    /// <summary>
    /// On a static method: also emitted as the C symbol `symbol` for the C runtime to call, in programs
    /// that reference the runtime global `trigger` (ModuleCodeGen.RuntimeExports.cs).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    internal sealed class RuntimeExportAttribute : Attribute
    {
        public RuntimeExportAttribute(string symbol, string trigger) { }
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
        // Exceptions for `throw CoreIntrinsics.LibraryFrames(..., "frames")`: the throw site names the
        // frames .NET's library lists, which depend on the public member that was called.
        public static Exception IndexOutOfRangeException() =>
            new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
        public static Exception KeyNotFoundException(string key) => new KeyNotFoundException("The given key '" + key + "' was not present in the dictionary.");
        public static Exception NoElementsException() => new InvalidOperationException("Sequence contains no elements");
        public static Exception NoMatchException() => new InvalidOperationException("Sequence contains no matching element");
        public static void EnumOpCantHappen() => throw new InvalidOperationException("Enumeration has either not started or has already finished.");

        // ArgumentOutOfRangeException.ThrowIf* (.NET 8+): exact messages, with the actual value.
        public static void IfNegative(int value, string paramName)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(paramName, value, paramName + " ('" + value + "') must be a non-negative value.");
        }

        public static void IfGreaterThan(int value, int other, string paramName)
        {
            if (value > other)
                throw new ArgumentOutOfRangeException(paramName, value, paramName + " ('" + value + "') must be less than or equal to '" + other + "'.");
        }

        public static void IfGreaterThan(uint value, uint other, string paramName)
        {
            if (value > other)
                throw new ArgumentOutOfRangeException(paramName, value, paramName + " ('" + value + "') must be less than or equal to '" + other + "'.");
        }

        public static void IndexMustBeLessOrEqual(string paramName) =>
            throw new ArgumentOutOfRangeException(paramName, "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
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

namespace CsharpcCoreLib
{
    using System.Collections.Generic;

    /// <summary>
    /// System.HashCode: .NET's xxHash32-based combiner. .NET seeds it randomly per process, so hash
    /// values are never stable across runs anyway; csharpc uses a fixed seed (deterministic builds).
    /// </summary>
    [Shadows("System.HashCode")]
    public struct HashCodeImpl
    {
        private const uint Seed = 0x9E3779B9u, Prime1 = 2654435761u, Prime2 = 2246822519u, Prime3 = 3266489917u, Prime4 = 668265263u, Prime5 = 374761393u;
        private uint _v1, _v2, _v3, _v4, _queue1, _queue2, _queue3, _length;

        private static uint Rol(uint v, int n) => (v << n) | (v >> (32 - n));
        private static uint Round(uint hash, uint input) => Rol(hash + input * Prime2, 13) * Prime1;
        private static uint QueueRound(uint hash, uint queued) => Rol(hash + queued * Prime3, 17) * Prime4;
        private static uint MixFinal(uint hash)
        {
            hash ^= hash >> 15; hash *= Prime2;
            hash ^= hash >> 13; hash *= Prime3;
            hash ^= hash >> 16;
            return hash;
        }
        private static uint Hash<T>(T value) => value is null ? 0u : (uint)CoreIntrinsics.HashOf(value);

        public static int Combine<T1>(T1 value1)
        {
            uint hash = unchecked(Seed + Prime5 + 4);
            hash = QueueRound(hash, Hash(value1));
            return (int)MixFinal(hash);
        }

        public static int Combine<T1, T2>(T1 value1, T2 value2)
        {
            uint hash = unchecked(Seed + Prime5 + 8);
            hash = QueueRound(hash, Hash(value1));
            hash = QueueRound(hash, Hash(value2));
            return (int)MixFinal(hash);
        }

        public static int Combine<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
            uint hash = unchecked(Seed + Prime5 + 12);
            hash = QueueRound(hash, Hash(value1));
            hash = QueueRound(hash, Hash(value2));
            hash = QueueRound(hash, Hash(value3));
            return (int)MixFinal(hash);
        }

        public static int Combine<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            var h = new HashCodeImpl();
            h.Add(value1); h.Add(value2); h.Add(value3); h.Add(value4);
            return h.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
            var h = new HashCodeImpl();
            h.Add(value1); h.Add(value2); h.Add(value3); h.Add(value4); h.Add(value5);
            return h.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
            var h = new HashCodeImpl();
            h.Add(value1); h.Add(value2); h.Add(value3); h.Add(value4); h.Add(value5); h.Add(value6);
            return h.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5, T6, T7>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
            var h = new HashCodeImpl();
            h.Add(value1); h.Add(value2); h.Add(value3); h.Add(value4); h.Add(value5); h.Add(value6); h.Add(value7);
            return h.ToHashCode();
        }

        public void Add<T>(T value) => AddHash(Hash(value));

        public void Add<T>(T value, IEqualityComparer<T> comparer) =>
            AddHash(value is null ? 0u : comparer != null ? (uint)comparer.GetHashCode(value) : Hash(value));

        private void AddHash(uint hash)
        {
            uint previous = _length++;
            uint position = previous % 4;
            if (position == 0) _queue1 = hash;
            else if (position == 1) _queue2 = hash;
            else if (position == 2) _queue3 = hash;
            else
            {
                if (previous == 3) { _v1 = unchecked(Seed + Prime1 + Prime2); _v2 = unchecked(Seed + Prime2); _v3 = Seed; _v4 = unchecked(Seed - Prime1); }
                _v1 = Round(_v1, _queue1); _v2 = Round(_v2, _queue2); _v3 = Round(_v3, _queue3); _v4 = Round(_v4, hash);
            }
        }

        public int ToHashCode()
        {
            uint length = _length;
            uint position = length % 4;
            uint hash = length < 4 ? unchecked(Seed + Prime5) : Rol(_v1, 1) + Rol(_v2, 7) + Rol(_v3, 12) + Rol(_v4, 18);
            hash += length * 4;
            if (position > 0) { hash = QueueRound(hash, _queue1); if (position > 1) { hash = QueueRound(hash, _queue2); if (position > 2) hash = QueueRound(hash, _queue3); } }
            return (int)MixFinal(hash);
        }
    }
}
