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

namespace CsharpcCoreLib.RegularExpressions
{
    [DebuggerDisplay("Count = {Count}")]

    [Shadows("System.Text.RegularExpressions.MatchCollection")]
    public class MatchCollection : IList<Match>, IReadOnlyList<Match>, IList
    {
        private readonly Regex _regex;
        private readonly List<Match> _matches;
        private readonly string _input;
        private int _startat;
        private int _prevlen;
        private bool _done;

        internal MatchCollection(Regex regex, string input, int startat)
        {
            if ((uint)startat > (uint)input.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.startat, ExceptionResource.BeginIndexNotNegative);
            }

            _regex = regex;
            _input = input;
            _startat = startat;
            _prevlen = -1;
            _matches = new List<Match>();
            _done = false;
        }

        public bool IsReadOnly => true;

        public int Count
        {
            get
            {
                EnsureInitialized();
                return _matches.Count;
            }
        }

        public virtual Match this[int i]
        {
            get
            {
                Match? match = null;
                if (i < 0 || (match = GetMatch(i)) is null)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.i);
                }
                return match;
            }
        }

        public IEnumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<Match> IEnumerable<Match>.GetEnumerator() => new Enumerator(this);

        private Match? GetMatch(int i)
        {
            Debug.Assert(i >= 0, "i cannot be negative.");

            if (_matches.Count > i)
            {
                return _matches[i];
            }

            if (_done)
            {
                return null;
            }

            Match match;
            do
            {
                match = _regex.RunSingleMatch(RegexRunnerMode.FullMatchRequired, _prevlen, _input, 0, _input.Length, _startat)!;
                if (!match.Success)
                {
                    _done = true;
                    return null;
                }

                _matches.Add(match);
                _prevlen = match.Length;
                _startat = match._textpos;
            } while (_matches.Count <= i);

            return match;
        }

        private void EnsureInitialized()
        {
            if (!_done)
            {
                GetMatch(int.MaxValue);
            }
        }

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public void CopyTo(Array array, int arrayIndex)
        {
            EnsureInitialized();
            // csharpc: the corelib List<T> has no non-generic ICollection, and there's no Array.SetValue.
            if (array is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }
            for (int i = 0; i < _matches.Count; i++)
            {
                ((object[])(object)array)[arrayIndex + i] = _matches[i];
            }
        }

        public void CopyTo(Match[] array, int arrayIndex)
        {
            EnsureInitialized();
            _matches.CopyTo(array, arrayIndex);
        }

        int IList<Match>.IndexOf(Match item)
        {
            EnsureInitialized();
            return _matches.IndexOf(item);
        }

        void IList<Match>.Insert(int index, Match item) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        void IList<Match>.RemoveAt(int index) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        Match IList<Match>.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        void ICollection<Match>.Add(Match item) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        void ICollection<Match>.Clear() =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        bool ICollection<Match>.Contains(Match item)
        {
            EnsureInitialized();
            return _matches.Contains(item);
        }

        bool ICollection<Match>.Remove(Match item) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        int IList.Add(object? value) =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        void IList.Clear() =>
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);

        bool IList.Contains(object? value) =>
            value is Match match && ((ICollection<Match>)this).Contains(match);

        int IList.IndexOf(object? value) =>
            value is Match other ? ((IList<Match>)this).IndexOf(other) : -1;

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

        private sealed class Enumerator : IEnumerator<Match>
        {
            private readonly MatchCollection _collection;
            private int _index;

            internal Enumerator(MatchCollection collection)
            {
                Debug.Assert(collection != null, "collection cannot be null.");

                _collection = collection;
                _index = -1;
            }

            public bool MoveNext()
            {
                if (_index == -2)
                {
                    return false;
                }

                _index++;
                Match? match = _collection.GetMatch(_index);

                if (match is null)
                {
                    _index = -2;
                    return false;
                }

                return true;
            }

            public Match Current
            {
                get
                {
                    if (_index < 0)
                    {
                        throw new InvalidOperationException(SR.EnumNotStarted);
                    }

                    return _collection.GetMatch(_index)!;
                }
            }

            object IEnumerator.Current => Current;

            void IEnumerator.Reset() => _index = -1;

            void IDisposable.Dispose() { }
        }
    }
}
