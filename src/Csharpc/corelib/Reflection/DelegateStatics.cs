// Delegate.CreateDelegate over a MethodInfo (the overloads that take a name or a Type to search are not here):
// .NET's argument checks, then MethodInfo.CreateDelegate, which binds the delegate type's Invoke signature to
// the method (the same relaxed binding) and reports a mismatch as the ArgumentException .NET throws.
#nullable disable
using System;
using System.Reflection;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Delegate")]
    internal static class DelegateStaticMembers
    {
        public static Delegate CreateDelegate(Type type, MethodInfo method) => CreateDelegate(type, null, method, true, true);
        public static Delegate CreateDelegate(Type type, MethodInfo method, bool throwOnBindFailure) => CreateDelegate(type, null, method, throwOnBindFailure, true);
        public static Delegate CreateDelegate(Type type, object firstArgument, MethodInfo method) => CreateDelegate(type, firstArgument, method, true, false);
        public static Delegate CreateDelegate(Type type, object firstArgument, MethodInfo method, bool throwOnBindFailure) => CreateDelegate(type, firstArgument, method, throwOnBindFailure, false);

        private static Delegate CreateDelegate(Type type, object firstArgument, MethodInfo method, bool throwOnBindFailure, bool noTarget)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(method);
            if (!typeof(Delegate).IsAssignableFrom(type) || type == typeof(Delegate) || type == typeof(MulticastDelegate))
                throw new ArgumentException("Type must derive from Delegate.", "type");
            try { return noTarget ? method.CreateDelegate(type) : method.CreateDelegate(type, firstArgument); }
            catch (ArgumentException) when (!throwOnBindFailure) { return null; }
        }
    }
}
