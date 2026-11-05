// System.RuntimeMethodHandle and System.RuntimeFieldHandle as values: a pointer-sized token that
// defaults to zero, compares by that token and has no function pointer without a method. Code like
// Microsoft.macOS's Runtime.GetNSObject<T>(nint) forwards `default(RuntimeMethodHandle)` for error
// messages. csharpc has no handles for real members yet (MethodBase.MethodHandle and
// FieldInfo.FieldHandle are explicit NotSupportedExceptions), so a handle is only ever the default one.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.RuntimeMethodHandle")]
    public readonly struct RuntimeMethodHandleImpl : IEquatable<RuntimeMethodHandle>
    {
        private readonly IntPtr _value;

        public IntPtr Value => _value;

        public IntPtr GetFunctionPointer()
        {
            if (_value == IntPtr.Zero) throw new ArgumentNullException(null, "Invalid handle.");
            throw new NotSupportedException("RuntimeMethodHandle.GetFunctionPointer is not supported by csharpc yet.");
        }

        public override bool Equals(object obj) => obj is RuntimeMethodHandle other && _value == ((RuntimeMethodHandleImpl)(object)other)._value;
        public bool Equals(RuntimeMethodHandle handle) => _value == ((RuntimeMethodHandleImpl)(object)handle)._value;
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(RuntimeMethodHandleImpl left, RuntimeMethodHandleImpl right) => left._value == right._value;
        public static bool operator !=(RuntimeMethodHandleImpl left, RuntimeMethodHandleImpl right) => left._value != right._value;
    }

    [Shadows("System.RuntimeFieldHandle")]
    public readonly struct RuntimeFieldHandleImpl : IEquatable<RuntimeFieldHandle>
    {
        private readonly IntPtr _value;

        public IntPtr Value => _value;

        public override bool Equals(object obj) => obj is RuntimeFieldHandle other && _value == ((RuntimeFieldHandleImpl)(object)other)._value;
        public bool Equals(RuntimeFieldHandle handle) => _value == ((RuntimeFieldHandleImpl)(object)handle)._value;
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(RuntimeFieldHandleImpl left, RuntimeFieldHandleImpl right) => left._value == right._value;
        public static bool operator !=(RuntimeFieldHandleImpl left, RuntimeFieldHandleImpl right) => left._value != right._value;
    }
}
