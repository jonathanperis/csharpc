// System.Runtime.InteropServices.CollectionsMarshal. The List<T> members are the framework's code over the framework's List<T> (served from its IL,
// ADR 0023: [UnsafeAccessor] reaches the list's private fields, which the closed world binds at compile time); the Dictionary<TKey, TValue> ones hand
// out the value slots of the corelib's Dictionary (corelib/Dictionary.cs).
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.CollectionsMarshal")]
    public static class CollectionsMarshalImpl
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_items")] private static extern ref T[] Items<T>(List<T> list);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_size")] private static extern ref int Size<T>(List<T> list);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_version")] private static extern ref int Version<T>(List<T> list);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Grow")] private static extern void Grow<T>(List<T> list, int capacity);

        public static Span<T> AsSpan<T>(List<T> list)
        {
            if (list == null) return default(Span<T>);
            int size = Size(list);
            T[] items = Items(list);
            if ((uint)size > (uint)items.Length) throw new InvalidOperationException("Operations that change non-concurrent collections must have exclusive access. A concurrent update was performed on this collection and corrupted its state. The collection's state is no longer correct.");
            return new Span<T>(items, 0, size);
        }

        public static void SetCount<T>(List<T> list, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            Version(list)++;
            if (count > list.Capacity) Grow(list, count);
            else if (count < Size(list) && RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                Array.Clear(Items(list), count, Size(list) - count);
            Size(list) = count;
        }

        // Dictionary<TKey, TValue>'s value slots, as .NET hands them out (corelib/Dictionary.cs).
        public static ref TValue GetValueRefOrAddDefault<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key, out bool exists) =>
            ref ((DictionaryImpl<TKey, TValue>)(object)dictionary).GetValueRefOrAddDefault(key, out exists);

        public static ref TValue GetValueRefOrNullRef<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key) =>
            ref ((DictionaryImpl<TKey, TValue>)(object)dictionary).GetValueRefOrNullRef(key);
    }
}
