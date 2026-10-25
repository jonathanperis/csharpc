// System.Diagnostics.StackFrame: csharpc creates no StackTrace objects yet (StackTraceObjects.cs), so
// there are no frames to read; creating one directly is an explicit NotSupportedException.
#nullable disable
using System;
using System.Reflection;

namespace CsharpcCoreLib
{
    [Shadows("System.Diagnostics.StackFrame")]
    public class StackFrameImpl
    {
        private static NotSupportedException NotSupported() => new NotSupportedException("System.Diagnostics.StackFrame is not supported by csharpc yet.");

        public StackFrameImpl() => throw NotSupported();
        public StackFrameImpl(bool needFileInfo) => throw NotSupported();
        public StackFrameImpl(int skipFrames) => throw NotSupported();
        public StackFrameImpl(int skipFrames, bool needFileInfo) => throw NotSupported();

        public virtual MethodBase GetMethod() => throw NotSupported();
        public virtual string GetFileName() => throw NotSupported();
        public virtual int GetFileLineNumber() => throw NotSupported();
        public virtual int GetFileColumnNumber() => throw NotSupported();
        public virtual int GetILOffset() => throw NotSupported();
        public virtual int GetNativeOffset() => throw NotSupported();
    }
}
