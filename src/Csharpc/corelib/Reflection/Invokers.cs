// System.Reflection.ConstructorInvoker / MethodInvoker: the fast invocation API (no exception wrapping,
// arguments as a span) over the same generated stubs as MethodBase.Invoke (Reflection/Members.cs).
#nullable disable
using System;
using System.Globalization;
using System.Reflection;

namespace CsharpcCoreLib
{
    [Shadows("System.Reflection.ConstructorInvoker")]
    public sealed class ConstructorInvokerImpl
    {
        private readonly ConstructorInfo _constructor;
        private ConstructorInvokerImpl(ConstructorInfo constructor) { _constructor = constructor; }

        public static ConstructorInvoker Create(ConstructorInfo constructor)
        {
            ArgumentNullException.ThrowIfNull(constructor);
            if (constructor is not RuntimeConstructorInfoImpl)
                throw new ArgumentException("The ConstructorInfo object is not valid.", "constructor");
            return (ConstructorInvoker)(object)new ConstructorInvokerImpl(constructor);
        }

        private object Call(object[] arguments) => _constructor.Invoke(BindingFlags.DoNotWrapExceptions, null, arguments, null);

        public object Invoke() => Call(new object[0]);
        public object Invoke(object arg1) => Call(new[] { arg1 });
        public object Invoke(object arg1, object arg2) => Call(new[] { arg1, arg2 });
        public object Invoke(object arg1, object arg2, object arg3) => Call(new[] { arg1, arg2, arg3 });
        public object Invoke(object arg1, object arg2, object arg3, object arg4) => Call(new[] { arg1, arg2, arg3, arg4 });
        public object Invoke(Span<object> arguments) => Call(arguments.ToArray());
    }

    [Shadows("System.Reflection.MethodInvoker")]
    public sealed class MethodInvokerImpl
    {
        private readonly MethodBase _method;
        private MethodInvokerImpl(MethodBase method) { _method = method; }

        public static MethodInvoker Create(MethodBase method)
        {
            ArgumentNullException.ThrowIfNull(method);
            if (method is not RuntimeMethodInfoImpl && method is not RuntimeConstructorInfoImpl)
                throw new ArgumentException("The MethodBase object is not valid.", "method");
            return (MethodInvoker)(object)new MethodInvokerImpl(method);
        }

        private object Call(object obj, object[] arguments) => _method.Invoke(obj, BindingFlags.DoNotWrapExceptions, null, arguments, null);

        public object Invoke(object obj) => Call(obj, new object[0]);
        public object Invoke(object obj, object arg1) => Call(obj, new[] { arg1 });
        public object Invoke(object obj, object arg1, object arg2) => Call(obj, new[] { arg1, arg2 });
        public object Invoke(object obj, object arg1, object arg2, object arg3) => Call(obj, new[] { arg1, arg2, arg3 });
        public object Invoke(object obj, object arg1, object arg2, object arg3, object arg4) => Call(obj, new[] { arg1, arg2, arg3, arg4 });
        public object Invoke(object obj, Span<object> arguments) => Call(obj, arguments.ToArray());
    }
}
