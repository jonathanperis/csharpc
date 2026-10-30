// T[] and string as BCL generic interfaces (see CodeGen/ModuleCodeGen.CoreLib.cs). Method names are
// "<interface metadata name without backtick>_<member name>"; the first parameter is the receiver.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    // Non-generic ICollection on any array: one slot table serves every element type, so only
    // element-type-agnostic members are implemented (the length is at the same offset for all arrays).
    internal static class SZArrayHelper
    {
        public static int ICollection_get_Count(Array a) => a.Length;
        public static bool ICollection_get_IsSynchronized(Array a) => false;
        public static object ICollection_get_SyncRoot(Array a) => a;

        // Arrays made by the C runtime have no per-element-type method table, so boxing their
        // elements isn't possible; generated code tags every array it allocates.
        public static IEnumerator IEnumerable_GetEnumerator(Array a) =>
            throw new NotSupportedException("csharpc: non-generic enumeration of an array created by the runtime is not supported yet.");
        public static void ICollection_CopyTo(Array a, Array array, int index) =>
            throw new NotSupportedException("csharpc: ICollection.CopyTo of an array created by the runtime is not supported yet.");
    }

    internal static class SZArrayHelper<T>
    {
        // Non-generic ICollection over T[] (Array's implementation: CopyTo is Array.Copy into any array).
        public static int ICollection_get_Count(T[] a) => a.Length;
        public static bool ICollection_get_IsSynchronized(T[] a) => false;
        public static object ICollection_get_SyncRoot(T[] a) => a;
        public static void ICollection_CopyTo(T[] a, Array array, int index)
        {
            if (array != null && array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", "array");
            ArrayChecks.CopyToUntyped(a, 0, array, index, a.Length, false);
        }

        // Non-generic IEnumerable / IList over T[] (per element type: elements are boxed).
        public static IEnumerator IEnumerable_GetEnumerator(T[] a) => new ArrayEnumerator<T>(a);

        public static object IList_get_Item(T[] a, int index) => CoreIntrinsics.Box(a[index]);
        public static void IList_set_Item(T[] a, int index, object value) { a[index] = CoreIntrinsics.CastOrThrow<T>(value); }
        public static bool IList_get_IsReadOnly(T[] a) => false;
        public static bool IList_get_IsFixedSize(T[] a) => true;
        public static int IList_Add(T[] a, object value) => throw new NotSupportedException("Collection was of a fixed size.");
        public static bool IList_Contains(T[] a, object value) => IList_IndexOf(a, value) >= 0;
        public static void IList_Clear(T[] a)
        {
            for (int i = 0; i < a.Length; i++) a[i] = default(T);
        }
        public static int IList_IndexOf(T[] a, object value)
        {
            for (int i = 0; i < a.Length; i++)
            {
                object element = CoreIntrinsics.Box(a[i]);
                if (element == null ? value == null : element.Equals(value)) return i;
            }
            return -1;
        }
        public static void IList_Insert(T[] a, int index, object value) => throw new NotSupportedException("Collection was of a fixed size.");
        public static void IList_Remove(T[] a, object value) => throw new NotSupportedException("Collection was of a fixed size.");
        public static void IList_RemoveAt(T[] a, int index) => throw new NotSupportedException("Collection was of a fixed size.");

        public static IEnumerator<T> IEnumerable1_GetEnumerator(T[] a) => new ArrayEnumerator<T>(a);

        // IStructuralComparable / IStructuralEquatable (System.Array's explicit implementations): element-wise
        // over boxed elements with the given comparer. Arrays of another element type would need untyped
        // element access, which csharpc doesn't have: reported instead of guessed.
        private static T[] OtherOfSameType(T[] a, object other)
        {
            if (other is T[] same) return same;
            // Arrays of every reference type share SZArrayHelper<object> (ModuleCodeGen.ElementHelper),
            // where T[] is object[] and a string[] is not: compare the arrays' own types instead.
            if (other is Array array && other.GetType() == a.GetType()) return CoreIntrinsics.ReinterpretAs<T[]>(array);
            if (other is Array)
                throw new NotSupportedException("csharpc: structural comparison of arrays with different element types is not supported yet.");
            return null;
        }

        public static int IStructuralComparable_CompareTo(T[] a, object other, IComparer comparer)
        {
            if (other == null) return 1;
            var o = OtherOfSameType(a, other);
            if (o == null || a.Length != o.Length)
                throw new ArgumentException("Object is not a array with the same number of elements as the array to compare it to.", "other");
            int i = 0, c = 0;
            while (i < o.Length && c == 0)
            {
                c = comparer.Compare(CoreIntrinsics.Box(a[i]), CoreIntrinsics.Box(o[i]));
                i++;
            }
            return c;
        }

        public static bool IStructuralEquatable_Equals(T[] a, object other, IEqualityComparer comparer)
        {
            if (other == null) return false;
            if (ReferenceEquals(a, other)) return true;
            var o = OtherOfSameType(a, other);
            if (o == null || o.Length != a.Length) return false;
            for (int i = 0; i < o.Length; i++)
                if (!comparer.Equals(CoreIntrinsics.Box(a[i]), CoreIntrinsics.Box(o[i]))) return false;
            return true;
        }

        public static int IStructuralEquatable_GetHashCode(T[] a, IEqualityComparer comparer)
        {
            if (comparer == null) throw new ArgumentNullException("comparer");
            // .NET combines the last 8 elements with a per-process random seed (HashCode); any stable
            // combination of the same elements keeps the contract.
            int hash = 0;
            for (int i = a.Length >= 8 ? a.Length - 8 : 0; i < a.Length; i++)
            {
                uint rol5 = ((uint)hash << 5) | ((uint)hash >> 27);
                hash = ((int)rol5 + hash) ^ comparer.GetHashCode(CoreIntrinsics.Box(a[i]));
            }
            return hash;
        }

        public static int ICollection1_get_Count(T[] a) => a.Length;
        public static bool ICollection1_get_IsReadOnly(T[] a) => true;
        public static void ICollection1_Add(T[] a, T item) => throw new NotSupportedException("Collection was of a fixed size.");
        public static void ICollection1_Clear(T[] a) => throw new NotSupportedException("Collection is read-only.");
        public static bool ICollection1_Contains(T[] a, T item) => IList1_IndexOf(a, item) >= 0;
        public static void ICollection1_CopyTo(T[] a, T[] array, int index)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (index < 0 || array.Length - index < a.Length)
                ThrowHelper.Argument("Destination array was not long enough. Check the destination index, length, and the array's lower bounds.");
            for (int i = 0; i < a.Length; i++) array[index + i] = a[i];
        }
        public static bool ICollection1_Remove(T[] a, T item) => throw new NotSupportedException("Collection was of a fixed size.");

        public static T IList1_get_Item(T[] a, int index) => a[index];
        public static void IList1_set_Item(T[] a, int index, T value) { a[index] = value; }
        public static int IList1_IndexOf(T[] a, T item)
        {
            for (int i = 0; i < a.Length; i++)
                if (CoreIntrinsics.AreEqual(a[i], item)) return i;
            return -1;
        }
        public static void IList1_Insert(T[] a, int index, T item) => throw new NotSupportedException("Collection was of a fixed size.");
        public static void IList1_RemoveAt(T[] a, int index) => throw new NotSupportedException("Collection was of a fixed size.");

        public static int IReadOnlyCollection1_get_Count(T[] a) => a.Length;
        public static T IReadOnlyList1_get_Item(T[] a, int index) => a[index];
    }

    internal sealed class ArrayEnumerator<T> : IEnumerator<T>
    {
        private readonly T[] _array;
        private int _index = -1;

        public ArrayEnumerator(T[] array) { _array = array; }

        public bool MoveNext()
        {
            if (_index < _array.Length) _index++;
            return _index < _array.Length;
        }

        public T Current => _array[_index];
        object IEnumerator.Current => CoreIntrinsics.Box(_array[_index]);
        public void Reset() { _index = -1; }
        public void Dispose() { }
    }

    internal static class StringInterfaces
    {
        public static IEnumerator<char> IEnumerable1_GetEnumerator(string s) => new StringEnumerator(s);
    }

    internal sealed class StringEnumerator : IEnumerator<char>
    {
        private readonly string _s;
        private int _index = -1;

        public StringEnumerator(string s) { _s = s; }

        public bool MoveNext()
        {
            if (_index < _s.Length) _index++;
            return _index < _s.Length;
        }

        public char Current => _s[_index];
        object IEnumerator.Current => CoreIntrinsics.Box(_s[_index]);
        public void Reset() { _index = -1; }
        public void Dispose() { }
    }
}
