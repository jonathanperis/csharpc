// System.Collections.Generic.Dictionary<TKey,TValue> and KeyValuePair<TKey,TValue> — a port of .NET's
// buckets + entries + free-list design, so enumeration order matches .NET exactly (insertion order,
// with removed slots reused LIFO by later additions).
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace CsharpcCoreLib
{
    internal static class HashHelpers
    {
        private static int[] s_primes;

        private static int[] Primes
        {
            get
            {
                if (s_primes == null)
                    s_primes = new int[]
                    {
                        3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
                        1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
                        17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
                        187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
                        1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369,
                    };
                return s_primes;
            }
        }

        private static bool IsPrime(int candidate)
        {
            if ((candidate & 1) != 0)
            {
                int limit = (int)Math.Sqrt(candidate);
                for (int divisor = 3; divisor <= limit; divisor += 2)
                    if ((candidate % divisor) == 0) return false;
                return true;
            }
            return candidate == 2;
        }

        public static int GetPrime(int min)
        {
            int[] primes = Primes;
            for (int i = 0; i < primes.Length; i++)
                if (primes[i] >= min) return primes[i];
            for (int i = (min | 1); i < int.MaxValue; i += 2)
                if (IsPrime(i) && ((i - 1) % 101 != 0)) return i;
            return min;
        }

        /// <summary>.NET's FastMod: value % divisor via 64-bit multiply-high (no hardware divide).</summary>
        public static ulong GetFastModMultiplier(uint divisor) => ulong.MaxValue / divisor + 1;

        public static uint FastMod(uint value, uint divisor, ulong multiplier) =>
            (uint)(((((multiplier * value) >> 32) + 1) * divisor) >> 32);

        public static int ExpandPrime(int oldSize)
        {
            int newSize = 2 * oldSize;
            if ((uint)newSize > 0x7FFFFFC3u && 0x7FFFFFC3 > oldSize) return 0x7FFFFFC3;
            return GetPrime(newSize);
        }
    }

    [Shadows("System.Collections.Generic.KeyValuePair`2")]
    public struct KeyValuePairImpl<TKey, TValue>
    {
        private readonly TKey key;
        private readonly TValue value;

        public KeyValuePairImpl(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
        }

        public TKey Key => key;
        public TValue Value => value;

        public void Deconstruct(out TKey key, out TValue value)
        {
            key = this.key;
            value = this.value;
        }

        public override string ToString()
        {
            var s = new StringBuilder();
            s.Append('[');
            if (key != null) s.Append(key.ToString());
            s.Append(", ");
            if (value != null) s.Append(value.ToString());
            s.Append(']');
            return s.ToString();
        }
    }

    [Shadows("System.Collections.DictionaryEntry")]
    public struct DictionaryEntryImpl
    {
        private object _key;
        private object _value;

        public DictionaryEntryImpl(object key, object value)
        {
            _key = key;
            _value = value;
        }

        public object Key { get => _key; set => _key = value; }
        public object Value { get => _value; set => _value = value; }

        public void Deconstruct(out object key, out object value)
        {
            key = _key;
            value = _value;
        }

        public override string ToString()
        {
            var s = new StringBuilder();
            s.Append('[');
            if (_key != null) s.Append(_key.ToString());
            s.Append(", ");
            if (_value != null) s.Append(_value.ToString());
            s.Append(']');
            return s.ToString();
        }
    }

    [Shadows("System.Collections.Generic.KeyValuePair")]
    public static class KeyValuePairStatics
    {
        public static KeyValuePair<TKey, TValue> Create<TKey, TValue>(TKey key, TValue value) => new KeyValuePair<TKey, TValue>(key, value);
    }


    [Shadows("System.Collections.Generic.CollectionExtensions")]
    public static class CollectionExtensionsImpl
    {
        public static TValue GetValueOrDefault<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary, TKey key) =>
            dictionary.TryGetValue(key, out TValue value) ? value : default(TValue);

        public static TValue GetValueOrDefault<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue) =>
            dictionary.TryGetValue(key, out TValue value) ? value : defaultValue;

        public static void AddRange<T>(List<T> list, ReadOnlySpan<T> source)
        {
            if (list == null) throw new ArgumentNullException("list");
            for (int i = 0; i < source.Length; i++) list.Add(source[i]);
        }

        public static bool TryAdd<TKey, TValue>(IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }

        public static bool Remove<TKey, TValue>(IDictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        {
            if (dictionary.TryGetValue(key, out value))
            {
                dictionary.Remove(key);
                return true;
            }
            value = default(TValue);
            return false;
        }
    }

    /// <summary>ICollection.CopyTo(Array, int)'s argument checks, with .NET's exceptions (Dictionary.cs).</summary>
    internal static class CollectionCopyChecks
    {
        internal static void Check(Array array, int index, int count)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.");
            if (array.GetLowerBound(0) != 0) throw new ArgumentException("The lower bound of target array must be zero.");
            if ((uint)index > (uint)array.Length) throw new ArgumentOutOfRangeException("index", "Non-negative number required.");
            if (array.Length - index < count) throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
        }

        internal static ArgumentException IncompatibleArrayType() =>
            new ArgumentException("Target array type is not compatible with the type of items in the collection.");
    }
}
