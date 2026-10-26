// Generic System.Array static members, in C# (sorting uses the .NET-faithful introsort in Core.cs).
// Hand-written intrinsics for the same members, if any, take priority (CoreLib.Map).
#nullable disable
using System;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Array")]
    internal static class ArrayExtras
    {
        /// <summary>The largest length of any array (.NET's 0x7FFFFFC7, the same for every element type).</summary>
        public static int MaxLength => 0x7FFFFFC7;

        /// <summary>Array.GetEnumerator(): the array's own IEnumerable implementation.</summary>
        [ShadowInstance]
        public static System.Collections.IEnumerator GetEnumerator(Array array) => ((System.Collections.IEnumerable)array).GetEnumerator();

        public static System.Collections.ObjectModel.ReadOnlyCollection<T> AsReadOnly<T>(T[] array)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            return new System.Collections.ObjectModel.ReadOnlyCollection<T>(array);
        }

        public static void Sort<T>(T[] array)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            SortHelper<T>.SortDefault(array, 0, array.Length);
        }

        public static void Sort<T>(T[] array, Comparison<T> comparison)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (comparison == null) ThrowHelper.ArgumentNull("comparison");
            SortHelper<T>.Sort(array, 0, array.Length, comparison);
        }

        public static void Sort<T>(T[] array, IComparer<T> comparer)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            SortHelper<T>.Sort(array, 0, array.Length, comparer);
        }

        public static void Sort<T>(T[] array, int index, int length)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            ArrayChecks.IndexLength(array.Length, index, length);
            SortHelper<T>.Sort(array, index, length, (IComparer<T>)null);
        }

        public static void Sort<T>(T[] array, int index, int length, IComparer<T> comparer)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            ArrayChecks.IndexLength(array.Length, index, length);
            SortHelper<T>.Sort(array, index, length, comparer);
        }

        public static void Sort<TKey, TValue>(TKey[] keys, TValue[] items)
        {
            if (keys == null) ThrowHelper.ArgumentNull("keys");
            Sort(keys, items, 0, keys.Length, null);
        }

        public static void Sort<TKey, TValue>(TKey[] keys, TValue[] items, IComparer<TKey> comparer)
        {
            if (keys == null) ThrowHelper.ArgumentNull("keys");
            Sort(keys, items, 0, keys.Length, comparer);
        }

        public static void Sort<TKey, TValue>(TKey[] keys, TValue[] items, int index, int length) => Sort(keys, items, index, length, null);

        public static void Sort<TKey, TValue>(TKey[] keys, TValue[] items, int index, int length, IComparer<TKey> comparer)
        {
            if (keys == null) ThrowHelper.ArgumentNull("keys");
            if (index < 0 || length < 0) ThrowHelper.ArgumentOutOfRange(length < 0 ? "length" : "index", "Non-negative number required.");
            if (keys.Length - index < length || (items != null && index > items.Length - length))
                throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            if (length <= 1) return;
            if (items == null)
            {
                SortHelper<TKey>.Sort(keys, index, length, comparer);
                return;
            }
            // Sorts items along with keys: the same introsort over an index permutation makes the same
            // decisions (they only depend on the comparisons), then the permutation is applied to both.
            int[] perm = new int[length];
            for (int i = 0; i < length; i++) perm[i] = i;
            TKey[] k = new TKey[length];
            Array.Copy(keys, index, k, 0, length);
            TValue[] v = new TValue[length];
            Array.Copy(items, index, v, 0, length);
            if (comparer == null || comparer == Comparer<TKey>.Default)
                SortHelper<int>.Sort(perm, 0, length, (a, b) => CoreIntrinsics.Compare(k[a], k[b]));
            else
                SortHelper<int>.Sort(perm, 0, length, (a, b) => comparer.Compare(k[a], k[b]));
            for (int i = 0; i < length; i++) { keys[index + i] = k[perm[i]]; items[index + i] = v[perm[i]]; }
        }

        public static void Reverse<T>(T[] array)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            Reverse(array, 0, array.Length);
        }

        public static void Reverse<T>(T[] array, int index, int length)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (index < 0) ThrowHelper.ArgumentOutOfRange("index", "Non-negative number required.");
            if (length < 0) ThrowHelper.ArgumentOutOfRange("length", "Non-negative number required.");
            if (array.Length - index < length) ThrowHelper.Argument(ArrayChecks.InvalidOffLen);
            int i = index, j = index + length - 1;
            while (i < j) { T t = array[i]; array[i] = array[j]; array[j] = t; i++; j--; }
        }

        public static int IndexOf<T>(T[] array, T value)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            return IndexOf(array, value, 0, array.Length);
        }

        public static int IndexOf<T>(T[] array, T value, int startIndex)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            return IndexOf(array, value, startIndex, array.Length - startIndex);
        }

        public static int IndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            ArrayChecks.StartIndexCount(array.Length, startIndex, count);
            for (int i = startIndex; i < startIndex + count; i++)
                if (CoreIntrinsics.AreEqual(array[i], value)) return i;
            return -1;
        }

        public static int LastIndexOf<T>(T[] array, T value)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            for (int i = array.Length - 1; i >= 0; i--)
                if (CoreIntrinsics.AreEqual(array[i], value)) return i;
            return -1;
        }

        public static int LastIndexOf<T>(T[] array, T value, int startIndex)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            return LastIndexOf(array, value, startIndex, array.Length == 0 ? 0 : startIndex + 1);
        }

        // .NET's argument checks: the range is [startIndex - count + 1, startIndex], searched backwards.
        public static int LastIndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            const string IndexMustBeLess = "Index was out of range. Must be non-negative and less than the size of the collection.";
            const string BadCount = "Count must be positive and count must refer to a location within the string/array/collection.";
            if (array.Length == 0)
            {
                if (startIndex != -1 && startIndex != 0) ThrowHelper.ArgumentOutOfRange("startIndex", IndexMustBeLess);
                if (count != 0) ThrowHelper.ArgumentOutOfRange("count", BadCount);
                return -1;
            }
            if ((uint)startIndex >= (uint)array.Length) ThrowHelper.ArgumentOutOfRange("startIndex", IndexMustBeLess);
            if (count < 0 || startIndex - count + 1 < 0) ThrowHelper.ArgumentOutOfRange("count", BadCount);
            for (int i = startIndex; i > startIndex - count; i--)
                if (CoreIntrinsics.AreEqual(array[i], value)) return i;
            return -1;
        }

        public static void Resize<T>(ref T[] array, int newSize)
        {
            if (newSize < 0) ThrowHelper.ArgumentOutOfRange("newSize", "Non-negative number required.");
            T[] larray = array;
            if (larray == null) { array = new T[newSize]; return; }
            if (larray.Length != newSize)
            {
                T[] newArray = new T[newSize];
                int n = Math.Min(larray.Length, newSize);
                for (int i = 0; i < n; i++) newArray[i] = larray[i];
                array = newArray;
            }
        }

        public static void Fill<T>(T[] array, T value)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            for (int i = 0; i < array.Length; i++) array[i] = value;
        }

        public static void Fill<T>(T[] array, T value, int startIndex, int count)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            ArrayChecks.StartIndexCount(array.Length, startIndex, count);
            for (int i = startIndex; i < startIndex + count; i++) array[i] = value;
        }

        public static T[] Empty<T>() => new T[0];

        public static bool Exists<T>(T[] array, Predicate<T> match) => FindIndex(array, match) != -1;

        public static T Find<T>(T[] array, Predicate<T> match)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (match == null) ThrowHelper.ArgumentNull("match");
            for (int i = 0; i < array.Length; i++) if (match(array[i])) return array[i];
            return default(T);
        }

        public static T FindLast<T>(T[] array, Predicate<T> match)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (match == null) ThrowHelper.ArgumentNull("match");
            for (int i = array.Length - 1; i >= 0; i--) if (match(array[i])) return array[i];
            return default(T);
        }

        public static T[] FindAll<T>(T[] array, Predicate<T> match)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (match == null) ThrowHelper.ArgumentNull("match");
            var list = new List<T>();
            for (int i = 0; i < array.Length; i++) if (match(array[i])) list.Add(array[i]);
            return list.ToArray();
        }

        public static int FindIndex<T>(T[] array, Predicate<T> match)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (match == null) ThrowHelper.ArgumentNull("match");
            for (int i = 0; i < array.Length; i++) if (match(array[i])) return i;
            return -1;
        }

        public static int FindLastIndex<T>(T[] array, Predicate<T> match)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (match == null) ThrowHelper.ArgumentNull("match");
            for (int i = array.Length - 1; i >= 0; i--) if (match(array[i])) return i;
            return -1;
        }

        public static bool TrueForAll<T>(T[] array, Predicate<T> match)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (match == null) ThrowHelper.ArgumentNull("match");
            for (int i = 0; i < array.Length; i++) if (!match(array[i])) return false;
            return true;
        }

        public static TOutput[] ConvertAll<TInput, TOutput>(TInput[] array, Converter<TInput, TOutput> converter)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (converter == null) ThrowHelper.ArgumentNull("converter");
            var result = new TOutput[array.Length];
            for (int i = 0; i < array.Length; i++) result[i] = converter(array[i]);
            return result;
        }

        public static void ForEach<T>(T[] array, Action<T> action)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (action == null) ThrowHelper.ArgumentNull("action");
            for (int i = 0; i < array.Length; i++) action(array[i]);
        }

        public static int BinarySearch<T>(T[] array, T value)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            return SortHelper<T>.BinarySearch(array, 0, array.Length, value, null);
        }

        public static int BinarySearch<T>(T[] array, T value, IComparer<T> comparer)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            return SortHelper<T>.BinarySearch(array, 0, array.Length, value, comparer);
        }

        public static int BinarySearch<T>(T[] array, int index, int length, T value) => BinarySearch(array, index, length, value, null);

        // Non-generic element access: reference-type arrays (including CreateInstance-synthesized T[])
        // go through the runtime; value-type elements still box through IList.
        [ShadowInstance]
        public static object GetValue(Array array, int index)
        {
            if (array.Rank != 1) throw new ArgumentException("Array was not a one-dimensional array.");
            try { return ReflectionRuntime.GetValue(array, index); }
            catch (NotSupportedException) { return ((System.Collections.IList)array)[index]; }
        }

        [ShadowInstance]
        public static void SetValue(Array array, object value, int index)
        {
            if (array.Rank != 1) throw new ArgumentException("Array was not a one-dimensional array.");
            // Reference-type elements: the runtime's assignability check and .NET's message
            // (ReflectionRuntime.SetValue); value-type elements unbox through the array's IList.
            try { ReflectionRuntime.SetValue(array, value, index); }
            catch (NotSupportedException) { ((System.Collections.IList)array)[index] = value; }
        }

        /// <summary>A new array of `arrayType` (closed world: a type csharpc emitted, e.g. through typeof(X[])).</summary>
        public static Array CreateInstanceFromArrayType(Type arrayType, int length)
        {
            ArgumentNullException.ThrowIfNull(arrayType);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            switch (CoreIntrinsics.csrt_array_type_kind(arrayType))
            {
                case 1: throw new ArgumentException("Must be an array type.", "arrayType");
                case 2: throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", "arrayType");
            }
            return CoreIntrinsics.csrt_array_new_of_type(arrayType, length);
        }

        public static int BinarySearch(Array array, object value) => BinarySearch(array, value, null);

        public static int BinarySearch(Array array, object value, System.Collections.IComparer comparer)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            return BinarySearch(array, 0, array.Length, value, comparer);
        }

        public static int BinarySearch(Array array, int index, int length, object value) => BinarySearch(array, index, length, value, null);

        public static int BinarySearch(Array array, int index, int length, object value, System.Collections.IComparer comparer)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (array.Rank != 1) throw new RankException("Only single dimensional arrays are supported for the requested action.");
            if (index < 0 || length < 0) throw new ArgumentOutOfRangeException(index < 0 ? "index" : "length", "Non-negative number required.");
            if (array.Length - index < length)
                throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            comparer ??= System.Collections.Comparer.Default;
            var list = (System.Collections.IList)array;
            int lo = index, hi = index + length - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                int c;
                try { c = comparer.Compare(list[i], value); }
                catch (Exception e) { throw new InvalidOperationException("Failed to compare two elements in the array.", e); }
                if (c == 0) return i;
                if (c < 0) lo = i + 1;
                else hi = i - 1;
            }
            return ~lo;
        }

        public static int BinarySearch<T>(T[] array, int index, int length, T value, IComparer<T> comparer)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            ArrayChecks.IndexLength(array.Length, index, length);
            return SortHelper<T>.BinarySearch(array, index, length, value, comparer);
        }
    }

    /// <summary>
    /// Argument validation of the non-generic Array.Copy overloads, in .NET's order and with its
    /// exact exceptions (the element copy itself is an intrinsic over csrt_array_copy).
    /// </summary>
    internal static class ArrayChecks
    {
        /// <summary>`span is "text"`: the chars equal the constant's.</summary>
        public static bool SpanEqualsString(ReadOnlySpan<char> span, string value)
        {
            if (span.Length != value.Length) return false;
            for (int i = 0; i < span.Length; i++)
                if (span[i] != value[i]) return false;
            return true;
        }

        /// <summary>T[].Clone(): a shallow copy.</summary>
        public static T[] CloneArray<T>(T[] array)
        {
            if (array == null) throw new NullReferenceException();
            T[] copy = new T[array.Length];
            Array.Copy(array, copy, array.Length);
            return copy;
        }

        public const string InvalidOffLen =
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";

        /// <summary>(index, length) ranges of Sort/BinarySearch: blames index first, like .NET.</summary>
        public static void IndexLength(int arrayLength, int index, int length)
        {
            if (index < 0 || length < 0) ThrowHelper.ArgumentOutOfRange(index < 0 ? "index" : "length", "Non-negative number required.");
            if (arrayLength - index < length) ThrowHelper.Argument(InvalidOffLen);
        }

        /// <summary>(startIndex, count) ranges of IndexOf/Fill.</summary>
        public static void StartIndexCount(int arrayLength, int startIndex, int count)
        {
            if ((uint)startIndex > (uint)arrayLength)
                ThrowHelper.ArgumentOutOfRange("startIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            if ((uint)count > (uint)(arrayLength - startIndex))
                ThrowHelper.ArgumentOutOfRange("count", "Count must be positive and count must refer to a location within the string/array/collection.");
        }

        /// <summary>
        /// Array.Copy(T[], ..., Array, ...) where the destination's element type is only known at run time
        /// (ICollection.CopyTo implementations): same element type, or object[] (elements boxed).
        /// </summary>
        public static void CopyToUntyped<T>(T[] source, long sourceIndex, Array destination, long destinationIndex, long length, bool longLength)
        {
            Copy(source, sourceIndex, destination, destinationIndex, length, longLength);
            int from = (int)sourceIndex, to = (int)destinationIndex, count = (int)length;
            if (destination is T[] typed) { Array.Copy(source, from, typed, to, count); return; }
            if (destination is object[] objects)
            {
                for (int i = 0; i < count; i++) objects[to + i] = CoreIntrinsics.Box(source[from + i]);
                return;
            }
            throw new NotSupportedException("csharpc: Array.Copy into an array of another element type is not supported yet.");
        }

        public static void Copy(Array sourceArray, long sourceIndex, Array destinationArray, long destinationIndex, long length, bool longLength)
        {
            if (longLength && (length > int.MaxValue || length < int.MinValue))
                throw new ArgumentOutOfRangeException("length", "Arrays larger than 2GB are not supported.");
            // The frames are .NET 11's (Array.Copy's overloads all check in CopyImpl).
            if (sourceArray == null)
                throw CoreIntrinsics.LibraryFrames(new ArgumentNullException("sourceArray"), "System.ArgumentNullException.Throw(String paramName)\nSystem.Array.CopyImpl(Array sourceArray, Int32 sourceIndex, Array destinationArray, Int32 destinationIndex, Int32 length, Boolean reliable)");
            if (destinationArray == null)
                throw CoreIntrinsics.LibraryFrames(new ArgumentNullException("destinationArray"), "System.ArgumentNullException.Throw(String paramName)\nSystem.Array.CopyImpl(Array sourceArray, Int32 sourceIndex, Array destinationArray, Int32 destinationIndex, Int32 length, Boolean reliable)");
            if (length < 0)
                throw CoreIntrinsics.LibraryFrames(new ArgumentOutOfRangeException("length", length, "length ('" + length + "') must be a non-negative value."),
                    "System.ArgumentOutOfRangeException.ThrowNegative[T](T value, String paramName)\nSystem.Array.CopyImpl(Array sourceArray, Int32 sourceIndex, Array destinationArray, Int32 destinationIndex, Int32 length, Boolean reliable)");
            if (sourceIndex < 0)
                throw CoreIntrinsics.LibraryFrames(new ArgumentOutOfRangeException("sourceIndex", sourceIndex, "sourceIndex ('" + sourceIndex + "') must be greater than or equal to '0'."),
                    "System.ArgumentOutOfRangeException.ThrowLess[T](T value, T other, String paramName)\nSystem.Array.CopyImpl(Array sourceArray, Int32 sourceIndex, Array destinationArray, Int32 destinationIndex, Int32 length, Boolean reliable)");
            if (destinationIndex < 0)
                throw CoreIntrinsics.LibraryFrames(new ArgumentOutOfRangeException("destinationIndex", destinationIndex, "destinationIndex ('" + destinationIndex + "') must be greater than or equal to '0'."),
                    "System.ArgumentOutOfRangeException.ThrowLess[T](T value, T other, String paramName)\nSystem.Array.CopyImpl(Array sourceArray, Int32 sourceIndex, Array destinationArray, Int32 destinationIndex, Int32 length, Boolean reliable)");
            if (sourceIndex > sourceArray.Length - length)
                throw CoreIntrinsics.LibraryFrames(new ArgumentException("Source array was not long enough. Check the source index, length, and the array's lower bounds.", "sourceArray"), "System.Array.CopyImpl(Array sourceArray, Int32 sourceIndex, Array destinationArray, Int32 destinationIndex, Int32 length, Boolean reliable)");
            if (destinationIndex > destinationArray.Length - length)
                throw CoreIntrinsics.LibraryFrames(new ArgumentException("Destination array was not long enough. Check the destination index, length, and the array's lower bounds.", "destinationArray"), "System.Array.CopyImpl(Array sourceArray, Int32 sourceIndex, Array destinationArray, Int32 destinationIndex, Int32 length, Boolean reliable)");
        }
    }
}
