// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// System.Collections.Hashtable, ported from dotnet/runtime (release/11.0)
// src/libraries/System.Private.CoreLib/src/System/Collections/Hashtable.cs; see THIRD-PARTY-NOTICES.md.
// The bucket layout is .NET's (double hashing over prime sizes, collision bits, the same growth and
// rehash points), so enumeration visits entries in .NET's order. Not ported: serialization and
// the obsolete IHashCodeProvider/IComparer constructors.
#nullable disable
using System;
using System.Collections;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Hashtable")]
    public class HashtableImpl : IDictionary, ICloneable
    {
        private struct Bucket
        {
            public object key;
            public object val;
            public int hash_coll; // high bit: collision; low 31 bits: the hash code
        }

        private const int InitialSize = 3;
        private const int HashPrime = 101;

        private Bucket[] _buckets;
        private int _count;
        private int _occupancy;
        private int _loadsize;
        private float _loadFactor;
        private int _version;
        private ICollection _keys;
        private ICollection _values;
        private IEqualityComparer _keycomparer;

        public HashtableImpl() : this(0, 1.0f) { }
        public HashtableImpl(int capacity) : this(capacity, 1.0f) { }

        public HashtableImpl(int capacity, float loadFactor)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity, "capacity");
            if (!(loadFactor >= 0.1f && loadFactor <= 1.0f))
                throw new ArgumentOutOfRangeException("loadFactor", "Load factor needs to be between 0.1 and 1.0.");
            _loadFactor = 0.72f * loadFactor;
            double rawsize = capacity / _loadFactor;
            if (rawsize > int.MaxValue) throw new ArgumentException("Hashtable's capacity overflowed and went negative. Check load factor, capacity and the current size of the table.", "capacity");
            int hashsize = (rawsize > InitialSize) ? HashHelpers.GetPrime((int)rawsize) : InitialSize;
            _buckets = new Bucket[hashsize];
            _loadsize = (int)(_loadFactor * hashsize);
        }

        public HashtableImpl(int capacity, float loadFactor, IEqualityComparer equalityComparer) : this(capacity, loadFactor) { _keycomparer = equalityComparer; }
        public HashtableImpl(IEqualityComparer equalityComparer) : this(0, 1.0f, equalityComparer) { }
        public HashtableImpl(int capacity, IEqualityComparer equalityComparer) : this(capacity, 1.0f, equalityComparer) { }
        public HashtableImpl(IDictionary d) : this(d, 1.0f) { }
        public HashtableImpl(IDictionary d, float loadFactor) : this(d, loadFactor, null) { }
        public HashtableImpl(IDictionary d, IEqualityComparer equalityComparer) : this(d, 1.0f, equalityComparer) { }

        public HashtableImpl(IDictionary d, float loadFactor, IEqualityComparer equalityComparer)
            : this(d != null ? d.Count : 0, loadFactor, equalityComparer)
        {
            if (d == null) throw new ArgumentNullException("d");
            IDictionaryEnumerator e = d.GetEnumerator();
            while (e.MoveNext()) Add(e.Key, e.Value);
        }

        private uint InitHash(object key, int hashsize, out uint seed, out uint incr)
        {
            uint hashcode = (uint)GetHash(key) & 0x7FFFFFFF;
            seed = hashcode;
            incr = 1 + ((seed * HashPrime) % ((uint)hashsize - 1));
            return hashcode;
        }

        public virtual void Add(object key, object value) => Insert(key, value, true);

        public virtual void Clear()
        {
            if (_count == 0 && _occupancy == 0) return;
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i].hash_coll = 0;
                _buckets[i].key = null;
                _buckets[i].val = null;
            }
            _count = 0;
            _occupancy = 0;
            _version++;
        }

        public virtual object Clone()
        {
            Bucket[] lbuckets = _buckets;
            var ht = new HashtableImpl(_count, 1.0f, _keycomparer);
            ht._version = _version;
            ht._loadFactor = _loadFactor;
            ht._count = 0;
            int bucket = lbuckets.Length;
            while (bucket > 0)
            {
                bucket--;
                object keyv = lbuckets[bucket].key;
                if (keyv != null && keyv != lbuckets) ht[keyv] = lbuckets[bucket].val;
            }
            return ht;
        }

        public virtual bool Contains(object key) => ContainsKey(key);

        public virtual bool ContainsKey(object key)
        {
            if (key == null) throw new ArgumentNullException("key");
            Bucket[] lbuckets = _buckets;
            uint hashcode = InitHash(key, lbuckets.Length, out uint seed, out uint incr);
            int ntry = 0;
            Bucket b;
            int bucketNumber = (int)(seed % (uint)lbuckets.Length);
            do
            {
                b = lbuckets[bucketNumber];
                if (b.key == null) return false;
                if (((b.hash_coll & 0x7FFFFFFF) == hashcode) && KeyEquals(b.key, key)) return true;
                bucketNumber = (int)(((long)bucketNumber + incr) % (uint)lbuckets.Length);
            } while (b.hash_coll < 0 && ++ntry < lbuckets.Length);
            return false;
        }

        public virtual bool ContainsValue(object value)
        {
            if (value == null)
            {
                for (int i = _buckets.Length; --i >= 0;)
                    if (_buckets[i].key != null && _buckets[i].key != _buckets && _buckets[i].val == null) return true;
            }
            else
            {
                for (int i = _buckets.Length; --i >= 0;)
                {
                    object val = _buckets[i].val;
                    if (val != null && val.Equals(value)) return true;
                }
            }
            return false;
        }

        private void CopyKeys(Array array, int arrayIndex)
        {
            Bucket[] lbuckets = _buckets;
            for (int i = lbuckets.Length; --i >= 0;)
            {
                object keyv = lbuckets[i].key;
                if (keyv != null && keyv != _buckets) array.SetValue(keyv, arrayIndex++);
            }
        }

        private void CopyEntries(Array array, int arrayIndex)
        {
            Bucket[] lbuckets = _buckets;
            for (int i = lbuckets.Length; --i >= 0;)
            {
                object keyv = lbuckets[i].key;
                if (keyv != null && keyv != _buckets) array.SetValue(new DictionaryEntry(keyv, lbuckets[i].val), arrayIndex++);
            }
        }

        private void CopyValues(Array array, int arrayIndex)
        {
            Bucket[] lbuckets = _buckets;
            for (int i = lbuckets.Length; --i >= 0;)
            {
                object keyv = lbuckets[i].key;
                if (keyv != null && keyv != _buckets) array.SetValue(lbuckets[i].val, arrayIndex++);
            }
        }

        private static void CheckCopyTo(Array array, int arrayIndex, int count)
        {
            if (array == null) throw new ArgumentNullException("array");
            if (array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", "array");
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex, "arrayIndex");
            if (array.Length - arrayIndex < count) throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
        }

        public virtual void CopyTo(Array array, int arrayIndex)
        {
            CheckCopyTo(array, arrayIndex, Count);
            CopyEntries(array, arrayIndex);
        }

        public virtual object this[object key]
        {
            get
            {
                if (key == null) throw new ArgumentNullException("key");
                Bucket[] lbuckets = _buckets;
                uint hashcode = InitHash(key, lbuckets.Length, out uint seed, out uint incr);
                int ntry = 0;
                Bucket b;
                int bucketNumber = (int)(seed % (uint)lbuckets.Length);
                do
                {
                    b = lbuckets[bucketNumber];
                    if (b.key == null) return null;
                    if (((b.hash_coll & 0x7FFFFFFF) == hashcode) && KeyEquals(b.key, key)) return b.val;
                    bucketNumber = (int)(((long)bucketNumber + incr) % (uint)lbuckets.Length);
                } while (b.hash_coll < 0 && ++ntry < lbuckets.Length);
                return null;
            }
            set => Insert(key, value, false);
        }

        private void Expand() => Rehash(HashHelpers.ExpandPrime(_buckets.Length));
        private void Rehash() => Rehash(_buckets.Length);

        private void Rehash(int newsize)
        {
            _occupancy = 0;
            Bucket[] newBuckets = new Bucket[newsize];
            for (int nb = 0; nb < _buckets.Length; nb++)
            {
                Bucket oldb = _buckets[nb];
                if (oldb.key != null && oldb.key != _buckets)
                    PutEntry(newBuckets, oldb.key, oldb.val, oldb.hash_coll & 0x7FFFFFFF);
            }
            _buckets = newBuckets;
            _loadsize = (int)(_loadFactor * newsize);
            _version++;
        }

        public virtual IDictionaryEnumerator GetEnumerator() => new HashtableEnumerator(this, HashtableEnumerator.DictEntry);
        IEnumerator IEnumerable.GetEnumerator() => new HashtableEnumerator(this, HashtableEnumerator.DictEntry);

        // Synchronized: .NET's SyncHashtable — writes lock the table's SyncRoot, reads don't.
        private HashtableImpl(bool _) { }

        public static Hashtable Synchronized(Hashtable table)
        {
            ArgumentNullException.ThrowIfNull(table);
            return (Hashtable)(object)new SyncHashtable(table);
        }

        private sealed class SyncHashtable : HashtableImpl, IEnumerable
        {
            private readonly Hashtable _table;

            internal SyncHashtable(Hashtable table) : base(false) { _table = table; }

            public override int Count => _table.Count;
            public override bool IsReadOnly => _table.IsReadOnly;
            public override bool IsFixedSize => _table.IsFixedSize;
            public override bool IsSynchronized => true;

            public override object this[object key]
            {
                get => _table[key];
                set { lock (_table.SyncRoot) _table[key] = value; }
            }

            public override object SyncRoot => _table.SyncRoot;
            public override void Add(object key, object value) { lock (_table.SyncRoot) _table.Add(key, value); }
            public override void Clear() { lock (_table.SyncRoot) _table.Clear(); }
            public override bool Contains(object key) => _table.Contains(key);

            public override bool ContainsKey(object key)
            {
                ArgumentNullException.ThrowIfNull(key);
                return _table.ContainsKey(key);
            }

            public override bool ContainsValue(object key) { lock (_table.SyncRoot) return _table.ContainsValue(key); }
            public override void CopyTo(Array array, int arrayIndex) { lock (_table.SyncRoot) _table.CopyTo(array, arrayIndex); }
            public override object Clone() { lock (_table.SyncRoot) return Synchronized((Hashtable)_table.Clone()); }
            IEnumerator IEnumerable.GetEnumerator() => _table.GetEnumerator();
            public override IDictionaryEnumerator GetEnumerator() => _table.GetEnumerator();
            public override ICollection Keys { get { lock (_table.SyncRoot) return _table.Keys; } }
            public override ICollection Values { get { lock (_table.SyncRoot) return _table.Values; } }
            public override void Remove(object key) { lock (_table.SyncRoot) _table.Remove(key); }
        }

        protected virtual int GetHash(object key) => _keycomparer != null ? _keycomparer.GetHashCode(key) : key.GetHashCode();

        public virtual bool IsReadOnly => false;
        public virtual bool IsFixedSize => false;
        public virtual bool IsSynchronized => false;

        protected virtual bool KeyEquals(object item, object key)
        {
            if (ReferenceEquals(_buckets, item)) return false;
            if (ReferenceEquals(item, key)) return true;
            if (_keycomparer != null) return _keycomparer.Equals(item, key);
            return item != null && item.Equals(key);
        }

        public virtual ICollection Keys => _keys ??= new KeyCollection(this);
        public virtual ICollection Values => _values ??= new ValueCollection(this);

        private void Insert(object key, object nvalue, bool add)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (_count >= _loadsize) Expand();
            else if (_occupancy > _loadsize && _count > 100) Rehash();

            uint hashcode = InitHash(key, _buckets.Length, out uint seed, out uint incr);
            int ntry = 0;
            int emptySlotNumber = -1;
            int bucketNumber = (int)(seed % (uint)_buckets.Length);
            do
            {
                if (emptySlotNumber == -1 && _buckets[bucketNumber].key == _buckets && _buckets[bucketNumber].hash_coll < 0)
                    emptySlotNumber = bucketNumber;
                if (_buckets[bucketNumber].key == null
                    || (_buckets[bucketNumber].key == _buckets && (_buckets[bucketNumber].hash_coll & unchecked((int)0x80000000)) == 0))
                {
                    if (emptySlotNumber != -1) bucketNumber = emptySlotNumber;
                    _buckets[bucketNumber].val = nvalue;
                    _buckets[bucketNumber].key = key;
                    _buckets[bucketNumber].hash_coll |= (int)hashcode;
                    _count++;
                    _version++;
                    return;
                }
                if (((_buckets[bucketNumber].hash_coll & 0x7FFFFFFF) == hashcode) && KeyEquals(_buckets[bucketNumber].key, key))
                {
                    if (add)
                        throw new ArgumentException("Item has already been added. Key in dictionary: '" + _buckets[bucketNumber].key + "'  Key being added: '" + key + "'");
                    _buckets[bucketNumber].val = nvalue;
                    _version++;
                    return;
                }
                if (emptySlotNumber == -1 && _buckets[bucketNumber].hash_coll >= 0)
                {
                    _buckets[bucketNumber].hash_coll |= unchecked((int)0x80000000);
                    _occupancy++;
                }
                bucketNumber = (int)(((long)bucketNumber + incr) % (uint)_buckets.Length);
            } while (++ntry < _buckets.Length);

            if (emptySlotNumber != -1)
            {
                _buckets[emptySlotNumber].val = nvalue;
                _buckets[emptySlotNumber].key = key;
                _buckets[emptySlotNumber].hash_coll |= (int)hashcode;
                _count++;
                _version++;
                return;
            }
            throw new InvalidOperationException("Hashtable insert failed. Load factor too high. The most common cause is multiple threads writing to the Hashtable simultaneously.");
        }

        private void PutEntry(Bucket[] newBuckets, object key, object nvalue, int hashcode)
        {
            uint seed = (uint)hashcode;
            uint incr = 1 + ((seed * HashPrime) % ((uint)newBuckets.Length - 1));
            int bucketNumber = (int)(seed % (uint)newBuckets.Length);
            while (true)
            {
                if (newBuckets[bucketNumber].key == null || newBuckets[bucketNumber].key == _buckets)
                {
                    newBuckets[bucketNumber].val = nvalue;
                    newBuckets[bucketNumber].key = key;
                    newBuckets[bucketNumber].hash_coll |= hashcode;
                    return;
                }
                if (newBuckets[bucketNumber].hash_coll >= 0)
                {
                    newBuckets[bucketNumber].hash_coll |= unchecked((int)0x80000000);
                    _occupancy++;
                }
                bucketNumber = (int)(((long)bucketNumber + incr) % (uint)newBuckets.Length);
            }
        }

        public virtual void Remove(object key)
        {
            if (key == null) throw new ArgumentNullException("key");
            uint hashcode = InitHash(key, _buckets.Length, out uint seed, out uint incr);
            int ntry = 0;
            Bucket b;
            int bn = (int)(seed % (uint)_buckets.Length);
            do
            {
                b = _buckets[bn];
                if (((b.hash_coll & 0x7FFFFFFF) == hashcode) && KeyEquals(b.key, key))
                {
                    _buckets[bn].hash_coll &= unchecked((int)0x80000000);
                    _buckets[bn].key = _buckets[bn].hash_coll != 0 ? _buckets : null;
                    _buckets[bn].val = null;
                    _count--;
                    _version++;
                    return;
                }
                bn = (int)(((long)bn + incr) % (uint)_buckets.Length);
            } while (b.hash_coll < 0 && ++ntry < _buckets.Length);
        }

        public virtual object SyncRoot => this;
        public virtual int Count => _count;

        private sealed class KeyCollection : ICollection
        {
            private readonly HashtableImpl _hashtable;
            internal KeyCollection(HashtableImpl hashtable) { _hashtable = hashtable; }

            public void CopyTo(Array array, int arrayIndex)
            {
                CheckCopyTo(array, arrayIndex, _hashtable._count);
                _hashtable.CopyKeys(array, arrayIndex);
            }

            public IEnumerator GetEnumerator() => new HashtableEnumerator(_hashtable, HashtableEnumerator.Keys);
            public bool IsSynchronized => false;
            public object SyncRoot => _hashtable;
            public int Count => _hashtable._count;
        }

        private sealed class ValueCollection : ICollection
        {
            private readonly HashtableImpl _hashtable;
            internal ValueCollection(HashtableImpl hashtable) { _hashtable = hashtable; }

            public void CopyTo(Array array, int arrayIndex)
            {
                CheckCopyTo(array, arrayIndex, _hashtable._count);
                _hashtable.CopyValues(array, arrayIndex);
            }

            public IEnumerator GetEnumerator() => new HashtableEnumerator(_hashtable, HashtableEnumerator.Values);
            public bool IsSynchronized => false;
            public object SyncRoot => _hashtable;
            public int Count => _hashtable._count;
        }

        private sealed class HashtableEnumerator : IDictionaryEnumerator
        {
            private readonly HashtableImpl _hashtable;
            private int _bucket;
            private readonly int _version;
            private bool _current;
            private readonly int _getObjectRetType;
            private object _currentKey;
            private object _currentValue;

            internal const int Keys = 1;
            internal const int Values = 2;
            internal const int DictEntry = 3;

            internal HashtableEnumerator(HashtableImpl hashtable, int getObjRetType)
            {
                _hashtable = hashtable;
                _bucket = hashtable._buckets.Length;
                _version = hashtable._version;
                _getObjectRetType = getObjRetType;
            }

            public object Key
            {
                get
                {
                    if (!_current) throw new InvalidOperationException("Enumeration has not started. Call MoveNext.");
                    return _currentKey;
                }
            }

            public bool MoveNext()
            {
                if (_version != _hashtable._version) throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                while (_bucket > 0)
                {
                    _bucket--;
                    object keyv = _hashtable._buckets[_bucket].key;
                    if (keyv != null && keyv != _hashtable._buckets)
                    {
                        _currentKey = keyv;
                        _currentValue = _hashtable._buckets[_bucket].val;
                        _current = true;
                        return true;
                    }
                }
                _current = false;
                return false;
            }

            public DictionaryEntry Entry
            {
                get
                {
                    if (!_current) throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    return new DictionaryEntry(_currentKey, _currentValue);
                }
            }

            public object Current
            {
                get
                {
                    if (!_current) throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    if (_getObjectRetType == Keys) return _currentKey;
                    if (_getObjectRetType == Values) return _currentValue;
                    return new DictionaryEntry(_currentKey, _currentValue);
                }
            }

            public object Value
            {
                get
                {
                    if (!_current) throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                    return _currentValue;
                }
            }

            public void Reset()
            {
                if (_version != _hashtable._version) throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                _current = false;
                _bucket = _hashtable._buckets.Length;
                _currentKey = null;
                _currentValue = null;
            }
        }
    }
}
