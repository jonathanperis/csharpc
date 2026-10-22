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
            SortHelper<T>.Sort(array, index, length, (IComparer<T>)null);
        }

        public static void Sort<T>(T[] array, int index, int length, IComparer<T> comparer)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
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
            int i = index, j = index + length - 1;
            while (i < j) { T t = array[i]; array[i] = array[j]; array[j] = t; i++; j--; }
        }

        public static int IndexOf<T>(T[] array, T value) => IndexOf(array, value, 0, array.Length);
        public static int IndexOf<T>(T[] array, T value, int startIndex) => IndexOf(array, value, startIndex, array.Length - startIndex);

        public static int IndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            for (int i = startIndex; i < startIndex + count; i++)
                if (CoreIntrinsics.AreEqual(array[i], value)) return i;
            return -1;
        }

        public static int LastIndexOf<T>(T[] array, T value)
        {
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
            for (int i = startIndex; i < startIndex + count; i++) array[i] = value;
        }

        public static T[] Empty<T>() => new T[0];

        public static bool Exists<T>(T[] array, Predicate<T> match) => FindIndex(array, match) != -1;

        public static T Find<T>(T[] array, Predicate<T> match)
        {
            for (int i = 0; i < array.Length; i++) if (match(array[i])) return array[i];
            return default(T);
        }

        public static T FindLast<T>(T[] array, Predicate<T> match)
        {
            for (int i = array.Length - 1; i >= 0; i--) if (match(array[i])) return array[i];
            return default(T);
        }

        public static T[] FindAll<T>(T[] array, Predicate<T> match)
        {
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
            for (int i = array.Length - 1; i >= 0; i--) if (match(array[i])) return i;
            return -1;
        }

        public static bool TrueForAll<T>(T[] array, Predicate<T> match)
        {
            for (int i = 0; i < array.Length; i++) if (!match(array[i])) return false;
            return true;
        }

        public static TOutput[] ConvertAll<TInput, TOutput>(TInput[] array, Converter<TInput, TOutput> converter)
        {
            var result = new TOutput[array.Length];
            for (int i = 0; i < array.Length; i++) result[i] = converter(array[i]);
            return result;
        }

        public static void ForEach<T>(T[] array, Action<T> action)
        {
            for (int i = 0; i < array.Length; i++) action(array[i]);
        }

        public static int BinarySearch<T>(T[] array, T value) => SortHelper<T>.BinarySearch(array, 0, array.Length, value, null);
        public static int BinarySearch<T>(T[] array, T value, IComparer<T> comparer) => SortHelper<T>.BinarySearch(array, 0, array.Length, value, comparer);
        public static int BinarySearch<T>(T[] array, int index, int length, T value) => SortHelper<T>.BinarySearch(array, index, length, value, null);
    }
}
