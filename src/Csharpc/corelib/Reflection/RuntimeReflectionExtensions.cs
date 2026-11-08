// System.Reflection.RuntimeReflectionExtensions (dotnet/runtime, release/11.0): the lookups of the
// "runtime" reflection API, each one of Type's with BindingFlags.Everything (public and not public,
// static and instance, inherited members included). GetMethodInfo(Delegate) is an intrinsic.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Reflection.RuntimeReflectionExtensions")]
    internal static class RuntimeReflectionExtensionsMembers
    {
        private const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        public static IEnumerable<EventInfo> GetRuntimeEvents([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicEvents | DynamicallyAccessedMemberTypes.NonPublicEvents)] Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.GetEvents(Everything);
        }

        public static IEnumerable<FieldInfo> GetRuntimeFields([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.GetFields(Everything);
        }

        public static IEnumerable<MethodInfo> GetRuntimeMethods([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.GetMethods(Everything);
        }

        public static IEnumerable<PropertyInfo> GetRuntimeProperties([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)] Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.GetProperties(Everything);
        }

        public static EventInfo GetRuntimeEvent([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicEvents)] Type type, string name)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.GetEvent(name);
        }

        public static FieldInfo GetRuntimeField([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type type, string name)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.GetField(name);
        }

        public static MethodInfo GetRuntimeMethod([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type, string name, Type[] parameters)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.GetMethod(name, parameters);
        }

        public static PropertyInfo GetRuntimeProperty([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type, string name)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type.GetProperty(name);
        }
    }
}
