// ExceptionDispatchInfo: compiled `await` inside catch/finally (IL async state machines) rethrows the
// pending exception through it. Capture() keeps the exception and a copy of the stack-trace frames
// recorded so far; Throw() rethrows the same object with those frames, .NET's "--- End of stack trace
// from previous location ---" line, and the frames from the Throw() call on (docs/adr/0012-stack-traces.md).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.ExceptionServices.ExceptionDispatchInfo")]
    public sealed class ExceptionDispatchInfoImpl
    {
        private readonly Exception _source;
        private readonly object _frames;

        private ExceptionDispatchInfoImpl(Exception source)
        {
            _source = source;
            _frames = CoreIntrinsics.CaptureStackTrace(source);
        }

        public static ExceptionDispatchInfoImpl Capture(Exception source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return new ExceptionDispatchInfoImpl(source);
        }

        public static void Throw(Exception source)
        {
            if (source == null) throw new ArgumentNullException("source");
            CoreIntrinsics.ThrowWithStackTrace(source, CoreIntrinsics.CaptureStackTrace(source));
        }

        public Exception SourceException => _source;

        public void Throw() { CoreIntrinsics.ThrowWithStackTrace(_source, _frames); }
    }
}
