// ExceptionDispatchInfo: compiled `await` inside catch/finally (IL async state machines) rethrows the
// pending exception through it. csharpc keeps no stack traces, so capturing is holding the exception
// object; Throw() throws that same object again (type and message unchanged, as in .NET).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.ExceptionServices.ExceptionDispatchInfo")]
    public sealed class ExceptionDispatchInfoImpl
    {
        private readonly Exception _source;

        private ExceptionDispatchInfoImpl(Exception source) { _source = source; }

        public static ExceptionDispatchInfoImpl Capture(Exception source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return new ExceptionDispatchInfoImpl(source);
        }

        public static void Throw(Exception source)
        {
            if (source == null) throw new ArgumentNullException("source");
            throw source;
        }

        public Exception SourceException => _source;

        public void Throw() { throw _source; }
    }
}
