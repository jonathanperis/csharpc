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
    }

    internal static class SZArrayHelper<T>
    {
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
