// System.Diagnostics.StackTrace objects (frames with their methods, files and lines) are not supported by
// csharpc yet: creating one is an explicit NotSupportedException. Exception.StackTrace (text) is supported.
#nullable disable
using System;
using System.Diagnostics;

namespace CsharpcCoreLib
{
    [Shadows("System.Diagnostics.StackTrace")]
    public class StackTraceImpl
    {
        private static NotSupportedException NotSupported() =>
            new NotSupportedException("System.Diagnostics.StackTrace objects are not supported by csharpc yet (Exception.StackTrace is).");

        public StackTraceImpl() => throw NotSupported();
        public StackTraceImpl(bool fNeedFileInfo) => throw NotSupported();
        public StackTraceImpl(int skipFrames) => throw NotSupported();
        public StackTraceImpl(int skipFrames, bool fNeedFileInfo) => throw NotSupported();
        public StackTraceImpl(Exception e) => throw NotSupported();
        public StackTraceImpl(Exception e, bool fNeedFileInfo) => throw NotSupported();
        public StackTraceImpl(Exception e, int skipFrames) => throw NotSupported();
        public StackTraceImpl(Exception e, int skipFrames, bool fNeedFileInfo) => throw NotSupported();

        public virtual int FrameCount => throw NotSupported();
        public virtual StackFrame GetFrame(int index) => throw NotSupported();
        public virtual StackFrame[] GetFrames() => throw NotSupported();
    }
}
