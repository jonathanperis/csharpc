// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Text.RegularExpressions;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md).
#nullable enable annotations

using System;
using System.Text;
using System.Text.RegularExpressions;

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CsharpcCoreLib.RegularExpressions
{
    [DebuggerDisplay("Count = {Count}")]

    [Shadows("System.Text.RegularExpressions.GroupCollection")]
    public class GroupCollection : IList<Group>, IReadOnlyList<Group>, IList, IReadOnlyDictionary<string, Group>
    {
        private readonly Match _match;
        private readonly Hashtable? _captureMap;

        private Group[]? _groups;

        internal GroupCollection(Match match, Hashtable? caps)
        {
            _match = match;
            _captureMap = caps;
        }

        internal void Reset() => _groups = null;

        public bool IsReadOnly => true;

        public int Count => _match._matchcount.Length;

        public Group this[int groupnum] => GetGroup(groupnum);

        public Group this[string groupname] => _match._regex is null ?
            Group.s_emptyGroup :
            GetGroup(_match._regex.GroupNumberFromName(groupname));

        public IEnumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<Group> IEnumerable<Group>.GetEnumerator() => new Enumerator(this);

        private Group GetGroup(int groupnum)
        {
            if (_captureMap != null)
            {
                if (_captureMap.TryGetValue(groupnum, out int groupNumImpl))
                {
                    return GetGroupImpl(groupNumImpl);
                }
            }
            else if ((uint)groupnum < _match._matchcount.Length)
            {
                return GetGroupImpl(groupnum);
            }

            return Group.s_emptyGroup;
        }

        private Group GetGroupImpl(int groupnum)
        {
            if (groupnum == 0)
            {
                return _match;
            }

            // Construct all the Group objects the first time GetGroup is called
            if (_groups is null)
            {
                _groups = new Group[_match._matchcount.Length - 1];
                for (int i = 0; i < _groups.Length; i++)
                {
                    string groupname = _match._regex!.GroupNameFromNumber(i + 1);
                    _groups[i] = new Group(_match.Text, _match._matches[i + 1], _match._matchcount[i + 1], groupname);
                }
            }

            return _groups[groupnum - 1];
        }

        public bool IsSynchronized => false;

        public object SyncRoot => _match;

        public void CopyTo(Array array, int arrayIndex)
        {
            if (array is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            for (int i = arrayIndex, j = 0; j < Count; i++, j++)
            {
                ((object[])(object)array)[i] = this[j]; // csharpc: no Array.SetValue
            }
        }

        public void CopyTo(Group[] array, int arrayIndex)
        {
            if (array is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);

            if (array.Length - arrayIndex < Count)
            {
                throw new ArgumentException(SR.Arg_ArrayPlusOffTooSmall);
            }

            for (int i = arrayIndex, j = 0; j < Count; i++, j++)
            {
                array[i] = this[j];
            }
        }

        int IList<Group>.IndexOf(Group item)
        {
            for (int i = 0; i < Count; i++)
            {
                if (EqualityComparer<Group>.Default.Equals(this[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        void IList<Group>.Insert(int index, Group item) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        void IList<Group>.RemoveAt(int index) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        Group IList<Group>.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        void ICollection<Group>.Add(Group item) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        void ICollection<Group>.Clear() =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        bool ICollection<Group>.Contains(Group item) =>
            ((IList<Group>)this).IndexOf(item) >= 0;

        bool ICollection<Group>.Remove(Group item) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        int IList.Add(object? value) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        void IList.Clear() =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        bool IList.Contains(object? value) =>
            value is Group other && ((ICollection<Group>)this).Contains(other);

        int IList.IndexOf(object? value) =>
            value is Group other ? ((IList<Group>)this).IndexOf(other) : -1;

        void IList.Insert(int index, object? value) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        bool IList.IsFixedSize => true;

        void IList.Remove(object? value) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        void IList.RemoveAt(int index) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        IEnumerator<KeyValuePair<string, Group>> IEnumerable<KeyValuePair<string, Group>>.GetEnumerator() =>
            new Enumerator(this);

        public bool TryGetValue(string key, [NotNullWhen(true)] out Group? value)
        {
            Group group = this[key];
            if (group == Group.s_emptyGroup)
            {
                value = null;
                return false;
            }

            value = group;
            return true;
        }

        public bool ContainsKey(string key) => _match._regex!.GroupNumberFromName(key) >= 0;

        public IEnumerable<string> Keys
        {
            get
            {
                for (int i = 0; i < Count; ++i)
                {
                    yield return GetGroup(i).Name;
                }
            }
        }

        public IEnumerable<Group> Values
        {
            get
            {
                for (int i = 0; i < Count; ++i)
                {
                    yield return GetGroup(i);
                }
            }
        }

        private sealed class Enumerator : IEnumerator<Group>, IEnumerator<KeyValuePair<string, Group>>
        {
            private readonly GroupCollection _collection;
            private int _index;

            internal Enumerator(GroupCollection collection)
            {
                Debug.Assert(collection != null, "collection cannot be null.");

                _collection = collection;
                _index = -1;
            }

            public bool MoveNext()
            {
                int size = _collection.Count;

                if (_index >= size)
                {
                    return false;
                }

                _index++;
                return _index < size;
            }

            public Group Current
            {
                get
                {
                    if (_index < 0 || _index >= _collection.Count)
                    {
                        throw new InvalidOperationException(SR.EnumNotStarted);
                    }

                    return _collection[_index];
                }
            }

            KeyValuePair<string, Group> IEnumerator<KeyValuePair<string, Group>>.Current
            {
                get
                {
                    if ((uint)_index >= _collection.Count)
                    {
                        throw new InvalidOperationException(SR.EnumNotStarted);
                    }

                    Group value = _collection[_index];
                    return new KeyValuePair<string, Group>(value.Name, value);

                }
            }

            object IEnumerator.Current => Current;

            void IEnumerator.Reset() => _index = -1;

            void IDisposable.Dispose() { }
        }
    }
}
