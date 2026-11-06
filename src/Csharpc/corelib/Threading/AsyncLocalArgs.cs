// AsyncLocalValueChangedArgs<T>: the argument of an AsyncLocal<T>'s value-changed handler. The struct exists so that
// code naming the handler type (System.Security.Principal.Windows' WindowsIdentity) compiles; csharpc's execution-
// context flow doesn't notify handlers yet, so constructing an AsyncLocal<T> *with* a handler is an explicit
// NotSupportedException (AsyncLocalImpl in Tasks/ExecutionFlow.cs).
#nullable disable
namespace CsharpcCoreLib
{
    [Shadows("System.Threading.AsyncLocalValueChangedArgs`1")]
    public readonly struct AsyncLocalValueChangedArgsImpl<T>
    {
        private readonly T _previousValue;
        private readonly T _currentValue;
        private readonly bool _threadContextChanged;

        public AsyncLocalValueChangedArgsImpl(T previousValue, T currentValue, bool contextChanged)
        {
            _previousValue = previousValue;
            _currentValue = currentValue;
            _threadContextChanged = contextChanged;
        }

        public T PreviousValue => _previousValue;
        public T CurrentValue => _currentValue;
        public bool ThreadContextChanged => _threadContextChanged;
    }
}
