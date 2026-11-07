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

        /// <summary>
        /// .NET (5+): the exception's stack trace becomes the current stack, without throwing it (System.Net.Http
        /// builds exceptions this way on its error paths). The current frames are not materialized here: the
        /// exception keeps the trace it had, and Throw() records the frames from where it is thrown.
        /// </summary>
        public static Exception SetCurrentStackTrace(Exception source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return source;
        }

        public Exception SourceException => _source;

        public void Throw() { CoreIntrinsics.ThrowWithStackTrace(_source, _frames); }
    }
}
