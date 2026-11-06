// MemoryExtensions.Split(this ReadOnlySpan<T>, T / ReadOnlySpan<T>): the enumerator over the ranges between separators
// (MemoryExtensions.SpanSplitEnumerator<T> in .NET). It yields one range more than there are separators, empty ones
// included, and a single empty range for an empty source. SplitAny (a SearchValues or a set of separators) is not here yet.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.MemoryExtensions+SpanSplitEnumerator`1")]
    public ref struct SpanSplitEnumeratorImpl<T> : IEnumerator<Range> where T : IEquatable<T>
    {
        private readonly ReadOnlySpan<T> _span;
        private readonly T _separator;
        private readonly ReadOnlySpan<T> _separatorSequence;
        private readonly bool _sequence;
        private int _startCurrent;
        private int _endCurrent;
        private int _startNext;

        internal SpanSplitEnumeratorImpl(ReadOnlySpan<T> span, T separator, ReadOnlySpan<T> separatorSequence, bool sequence)
        {
            _span = span;
            _separator = separator;
            _separatorSequence = separatorSequence;
            _sequence = sequence;
            _startCurrent = 0;
            _endCurrent = 0;
            _startNext = 0;
        }

        public SpanSplitEnumeratorImpl<T> GetEnumerator() => this;

        public Range Current => new Range(_startCurrent, _endCurrent);

        public bool MoveNext()
        {
            if (_startNext > _span.Length) return false;
            ReadOnlySpan<T> slice = _span.Slice(_startNext);
            int separatorIndex = _sequence ? (_separatorSequence.IsEmpty ? -1 : slice.IndexOf(_separatorSequence)) : slice.IndexOf(_separator);
            int separatorLength = _sequence ? _separatorSequence.Length : 1;
            _startCurrent = _startNext;
            if (separatorIndex >= 0)
            {
                _endCurrent = _startCurrent + separatorIndex;
                _startNext = _endCurrent + separatorLength;
            }
            else
            {
                _startNext = _span.Length + 1;
                _endCurrent = _span.Length;
            }
            return true;
        }

        // .NET implements IEnumerator<Range> on the ref struct (foreach in IL disposes it through IDisposable).
        object IEnumerator.Current => Current;
        void IEnumerator.Reset() => throw new NotSupportedException();
        void IDisposable.Dispose() { }
    }
}
