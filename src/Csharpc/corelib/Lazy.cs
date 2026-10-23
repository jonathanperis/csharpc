// System.Lazy<T>: libraries use it for lazily built singletons (static alphabets, tables). Semantics of
// .NET's implementation: the factory runs once; in ExecutionAndPublication and None modes an exception
// from the factory is cached and rethrown by every later Value; re-entering Value from the factory is an
// InvalidOperationException; PublicationOnly lets racing threads run the factory and keeps the first result.
#nullable disable
using System;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Lazy`1")]
    public class LazyImpl<T>
    {
        private Func<T> _factory;
        private T _value;
        private Exception _error;
        private readonly LazyThreadSafetyMode _mode;
        private volatile bool _created;
        private bool _running;

        public LazyImpl(T value)
        {
            _value = value;
            _created = true;
            _mode = LazyThreadSafetyMode.None;
        }

        public LazyImpl(Func<T> valueFactory) : this(valueFactory, LazyThreadSafetyMode.ExecutionAndPublication) { }

        public LazyImpl(Func<T> valueFactory, bool isThreadSafe)
            : this(valueFactory, isThreadSafe ? LazyThreadSafetyMode.ExecutionAndPublication : LazyThreadSafetyMode.None) { }

        public LazyImpl(Func<T> valueFactory, LazyThreadSafetyMode mode)
        {
            if (valueFactory == null) throw new ArgumentNullException("valueFactory");
            if (mode < LazyThreadSafetyMode.None || mode > LazyThreadSafetyMode.ExecutionAndPublication)
                throw new ArgumentOutOfRangeException("mode", "The mode argument specifies an invalid value.");
            _factory = valueFactory;
            _mode = mode;
        }

        public bool IsValueCreated => _created;

        public T Value => _created ? _value : Create();

        private T Create()
        {
            if (_mode == LazyThreadSafetyMode.PublicationOnly)
            {
                var factory = _factory;
                if (factory == null) return _value; // another thread published meanwhile
                var result = factory();
                lock (this)
                {
                    if (!_created) { _value = result; _factory = null; _created = true; }
                    return _value;
                }
            }
            if (_mode == LazyThreadSafetyMode.ExecutionAndPublication) Monitor.Enter(this);
            try
            {
                if (_created) return _value;
                if (_error != null) throw _error;
                if (_running) throw new InvalidOperationException("ValueFactory attempted to access the Value property of this instance.");
                _running = true;
                try
                {
                    _value = _factory();
                }
                catch (Exception e)
                {
                    _error = e;
                    throw;
                }
                finally
                {
                    _running = false;
                }
                _factory = null;
                _created = true;
                return _value;
            }
            finally
            {
                if (_mode == LazyThreadSafetyMode.ExecutionAndPublication) Monitor.Exit(this);
            }
        }

        public override string ToString() => _created ? (_value == null ? "" : _value.ToString()) : "Value is not created.";
    }
}
