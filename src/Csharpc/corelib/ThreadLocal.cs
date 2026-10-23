// System.Threading.ThreadLocal<T>: a value per thread (lazily created by the factory), optionally
// tracking every thread's value for Values. Per-thread storage is a [ThreadStatic] map from instance
// id to slot, so instances don't share state and disposed ids are never reused.
#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Threading.ThreadLocal`1")]
    public class ThreadLocalImpl<T> : IDisposable
    {
        private sealed class Slot { public T Value; }

        [ThreadStatic] private static Dictionary<long, Slot> t_slots;
        private static long s_nextId;

        private readonly long _id = Interlocked.Increment(ref s_nextId);
        private readonly Func<T> _factory;
        private readonly bool _trackAllValues;
        private readonly List<Slot> _all;
        private volatile bool _disposed;

        public ThreadLocalImpl() { }
        public ThreadLocalImpl(bool trackAllValues) { _trackAllValues = trackAllValues; if (trackAllValues) _all = new List<Slot>(); }
        public ThreadLocalImpl(Func<T> valueFactory)
        {
            _factory = valueFactory ?? throw new ArgumentNullException("valueFactory");
        }
        public ThreadLocalImpl(Func<T> valueFactory, bool trackAllValues) : this(trackAllValues)
        {
            _factory = valueFactory ?? throw new ArgumentNullException("valueFactory");
        }

        private Slot Find()
        {
            if (_disposed) throw Disposed();
            Slot slot = null;
            if (t_slots != null) t_slots.TryGetValue(_id, out slot);
            return slot;
        }

        private Slot Create(T value)
        {
            var slot = new Slot { Value = value };
            (t_slots ??= new Dictionary<long, Slot>())[_id] = slot;
            if (_all != null) lock (_all) _all.Add(slot);
            return slot;
        }

        public T Value
        {
            get
            {
                var slot = Find();
                if (slot != null) return slot.Value;
                T value = _factory != null ? _factory() : default(T);
                if (_disposed) throw Disposed();
                return Create(value).Value;
            }
            set
            {
                var slot = Find();
                if (slot != null) slot.Value = value;
                else Create(value);
            }
        }

        public bool IsValueCreated => Find() != null;

        public IList<T> Values
        {
            get
            {
                if (_disposed) throw Disposed();
                if (!_trackAllValues)
                    throw new InvalidOperationException("The ThreadLocal object is not tracking values. To use the Values property, use a ThreadLocal constructor that accepts the trackAllValues parameter and set the parameter to true.");
                var values = new List<T>();
                lock (_all) foreach (var slot in _all) values.Add(slot.Value);
                return values;
            }
        }

        public override string ToString() => Value.ToString();

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            _disposed = true;
            t_slots?.Remove(_id);
            if (_all != null) lock (_all) _all.Clear();
        }

        /// <summary>.NET names the type with its type argument's assembly-qualified name; that is exact for
        /// framework types (System.Private.CoreLib), not for the program's own types.</summary>
        private static ObjectDisposedException Disposed() =>
            new ObjectDisposedException("System.Threading.ThreadLocal`1[[" + typeof(T).FullName
                + ", System.Private.CoreLib, Version=11.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]");
    }
}
