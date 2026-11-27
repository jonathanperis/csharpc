// System.Runtime.InteropServices.ExternalException and System.ComponentModel.Win32Exception (error codes need fields,
// which the runtime's fixed exception layout doesn't have); System.Net.Primitives' SocketException derives from it.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.InteropServices.ExternalException")]
    public class ExternalExceptionImpl : SystemException
    {
        internal const int E_FAIL = unchecked((int)0x80004005);
        public ExternalExceptionImpl() : base("External component has thrown an exception.") { HResult = E_FAIL; }
        public ExternalExceptionImpl(string message) : base(message) { HResult = E_FAIL; }
        public ExternalExceptionImpl(string message, Exception inner) : base(message, inner) { HResult = E_FAIL; }
        public ExternalExceptionImpl(string message, int errorCode) : base(message) { HResult = errorCode; }
        public virtual int ErrorCode => HResult;

        /// <summary>
        /// .NET's ExternalException.ToString (DbException's, so Npgsql's): the HResult after the type name, the inner
        /// exception after " ---> " without Exception's "--- End of inner exception stack trace ---" line, then the trace.
        /// </summary>
        public override string ToString()
        {
            string message = Message;
            string s = string.Concat(GetType().ToString(), " (0x", Hex8(HResult), ")");
            if (!string.IsNullOrEmpty(message)) s = string.Concat(s, ": ", message);
            Exception inner = InnerException;
            if (inner != null) s = string.Concat(s, Environment.NewLine, " ---> ", inner.ToString());
            string stackTrace = StackTrace;
            if (stackTrace != null) s = string.Concat(s, Environment.NewLine, stackTrace);
            return s;
        }

        // HResult.ToString("X8") without number formatting: every program that compares strings with a culture can
        // construct this exception (CompareInfo's sort handles), so its ToString is in most binaries.
        private static string Hex8(int value)
        {
            Span<char> digits = stackalloc char[8];
            uint v = (uint)value;
            for (int i = 7; i >= 0; i--, v >>= 4) digits[i] = (char)((v & 0xF) < 10 ? '0' + (v & 0xF) : 'A' - 10 + (v & 0xF));
            return new string(digits);
        }
    }

    [Shadows("System.ComponentModel.Win32Exception")]
    public class Win32ExceptionImpl : System.Runtime.InteropServices.ExternalException
    {
        private const int E_FAIL = unchecked((int)0x80004005);
        public int NativeErrorCode { get; }
        private int HResultValue => HResult;

        public Win32ExceptionImpl() : this(System.Runtime.InteropServices.Marshal.GetLastPInvokeError()) { }
        public Win32ExceptionImpl(int error) : this(error, System.Runtime.InteropServices.Marshal.GetPInvokeErrorMessage(error)) { }
        public Win32ExceptionImpl(int error, string message) : base(message) { NativeErrorCode = error; }
        public Win32ExceptionImpl(string message) : this(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), message) { }
        public Win32ExceptionImpl(string message, Exception innerException) : base(message, innerException)
        {
            NativeErrorCode = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        }

        /// <summary>.NET's Win32Exception.ToString: "Type (native): message", with the HResult when it isn't E_FAIL.</summary>
        public override string ToString()
        {
            if (NativeErrorCode == 0 || NativeErrorCode == HResultValue) return base.ToString();
            string native = NativeErrorCode < 0 ? "0x" + NativeErrorCode.ToString("X8") : NativeErrorCode.ToString();
            var s = new System.Text.StringBuilder(GetType().ToString());
            if (HResultValue == E_FAIL) s.Append(" (").Append(native).Append(')');
            else s.Append(" (").Append(HResultValue.ToString("X8")).Append(", ").Append(native).Append(')');
            string message = Message;
            if (!string.IsNullOrEmpty(message)) s.Append(": ").Append(message);
            if (InnerException != null) s.Append(" ---> ").Append(InnerException.ToString());
            string stackTrace = StackTrace;
            if (stackTrace != null) s.Append(Environment.NewLine).Append(stackTrace);
            return s.ToString();
        }
    }
}
