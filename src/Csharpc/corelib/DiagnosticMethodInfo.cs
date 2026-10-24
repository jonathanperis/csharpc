// System.Diagnostics.DiagnosticMethodInfo describes a delegate's target method by name. csharpc keeps no
// method metadata at run time yet, and returning null (what NativeAOT does without stack-trace data)
// would make output silently differ from the JIT's, so Create throws.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.Diagnostics.DiagnosticMethodInfo")]
    public sealed class DiagnosticMethodInfoImpl
    {
        private DiagnosticMethodInfoImpl() { }

        public string Name => null;
        public string DeclaringTypeName => null;
        public string DeclaringAssemblyName => null;

        public static System.Diagnostics.DiagnosticMethodInfo Create(Delegate @delegate)
        {
            if (@delegate == null) throw new ArgumentNullException("delegate");
            throw new NotSupportedException("DiagnosticMethodInfo is not supported by csharpc yet (no method metadata at run time).");
        }

        public static System.Diagnostics.DiagnosticMethodInfo Create(System.Diagnostics.StackFrame frame)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            throw new NotSupportedException("DiagnosticMethodInfo is not supported by csharpc yet (no method metadata at run time).");
        }
    }
}
