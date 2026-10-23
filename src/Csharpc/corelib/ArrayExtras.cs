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
            // Sorts items along with keys: sort an index permutation, then apply it to both.
            int n = keys.Length;
            int[] perm = new int[n];
            for (int i = 0; i < n; i++) perm[i] = i;
            TKey[] k = new TKey[n];
            for (int i = 0; i < n; i++) k[i] = keys[i];
            SortHelper<int>.Sort(perm, 0, n, (a, b) => CoreIntrinsics.Compare(k[a], k[b]));
            TValue[] v = new TValue[items.Length];
            for (int i = 0; i < v.Length; i++) v[i] = items[i];
            for (int i = 0; i < n; i++) { keys[i] = k[perm[i]]; items[i] = v[perm[i]]; }
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

        public static void Copy(Array sourceArray, long sourceIndex, Array destinationArray, long destinationIndex, long length, bool longLength)
        {
            if (longLength && (length > int.MaxValue || length < int.MinValue))
                throw new ArgumentOutOfRangeException("length", "Arrays larger than 2GB are not supported.");
            if (sourceArray == null) ThrowHelper.ArgumentNull("sourceArray");
            if (destinationArray == null) ThrowHelper.ArgumentNull("destinationArray");
            if (length < 0) throw new ArgumentOutOfRangeException("length", length, "length ('" + length + "') must be a non-negative value.");
            if (sourceIndex < 0)
                throw new ArgumentOutOfRangeException("sourceIndex", sourceIndex, "sourceIndex ('" + sourceIndex + "') must be greater than or equal to '0'.");
            if (destinationIndex < 0)
                throw new ArgumentOutOfRangeException("destinationIndex", destinationIndex, "destinationIndex ('" + destinationIndex + "') must be greater than or equal to '0'.");
            if (sourceIndex > sourceArray.Length - length)
                throw new ArgumentException("Source array was not long enough. Check the source index, length, and the array's lower bounds.", "sourceArray");
            if (destinationIndex > destinationArray.Length - length)
                throw new ArgumentException("Destination array was not long enough. Check the destination index, length, and the array's lower bounds.", "destinationArray");
        }
    }
}
