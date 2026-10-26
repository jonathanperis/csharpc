// Attribute.GetCustomAttribute(s)/IsDefined and CustomAttributeExtensions over members and parameters
// (dotnet/runtime Attribute.CoreCLR.cs, CustomAttributeExtensions.cs): argument checks, the single-
// attribute AmbiguousMatchException, results typed as the requested attribute type's array. Inherited
// attributes of overriding methods' parameters are not supported yet (NotSupportedException).
#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Attribute")]
    internal static class AttributeLookup
    {
        private static void Check(object element, Type attributeType)
        {
            ArgumentNullException.ThrowIfNull(element, "element");
            ArgumentNullException.ThrowIfNull(attributeType, "attributeType");
            if (attributeType != typeof(Attribute) && !attributeType.IsSubclassOf(typeof(Attribute)))
                throw new ArgumentException("Type passed in must be derived from System.Attribute or System.Attribute itself.");
        }

        private static Attribute Single(Attribute[] attributes, Type attributeType)
        {
            if (attributes.Length == 0) return null;
            if (attributes.Length == 1) return attributes[0];
            throw new AmbiguousMatchException("Multiple custom attributes of the same type '" + attributeType + "' found.");
        }

        public static Attribute[] GetCustomAttributes(MemberInfo element, Type attributeType) => GetCustomAttributes(element, attributeType, true);

        public static Attribute[] GetCustomAttributes(MemberInfo element, Type attributeType, bool inherit)
        {
            Check(element, attributeType);
            // csharpc's Type objects are runtime Type handles (csrt_Type_MT), not MemberInfo instances:
            // `is MemberInfo` is false and MemberInfo virtuals on them throw. Route through Type's own
            // GetCustomAttributes (BclValueTypes.Reflection), which the attributes tutorial needs after
            // GetTypeInfo() (an identity that still types the value as TypeInfo/MemberInfo at the call).
            if (element is Type type)
                return CoreIntrinsics.ReinterpretAs<Attribute[]>(type.GetCustomAttributes(attributeType, inherit));
            return CoreIntrinsics.ReinterpretAs<Attribute[]>(element.GetCustomAttributes(attributeType, inherit));
        }

        public static Attribute[] GetCustomAttributes(MemberInfo element) => GetCustomAttributes(element, typeof(Attribute), true);
        public static Attribute[] GetCustomAttributes(MemberInfo element, bool inherit) => GetCustomAttributes(element, typeof(Attribute), inherit);

        public static Attribute GetCustomAttribute(MemberInfo element, Type attributeType) => GetCustomAttribute(element, attributeType, true);
        public static Attribute GetCustomAttribute(MemberInfo element, Type attributeType, bool inherit) =>
            Single(GetCustomAttributes(element, attributeType, inherit), attributeType);

        public static bool IsDefined(MemberInfo element, Type attributeType) => IsDefined(element, attributeType, true);
        public static bool IsDefined(MemberInfo element, Type attributeType, bool inherit)
        {
            Check(element, attributeType);
            if (element is Type type) return type.IsDefined(attributeType, inherit);
            return element.IsDefined(attributeType, inherit);
        }

        public static Attribute[] GetCustomAttributes(ParameterInfo element, Type attributeType) => GetCustomAttributes(element, attributeType, true);

        public static Attribute[] GetCustomAttributes(ParameterInfo element, Type attributeType, bool inherit)
        {
            Check(element, attributeType);
            if (element.Member == null) throw new ArgumentException("The ParameterInfo object is not valid.", "element");
            if (inherit && element.Member is MethodInfo method && method.IsVirtual && (method.Attributes & MethodAttributes.NewSlot) == 0)
                throw new NotSupportedException("Inherited custom attributes of an overriding method's parameters are not supported by csharpc yet.");
            return CoreIntrinsics.ReinterpretAs<Attribute[]>(element.GetCustomAttributes(attributeType, inherit));
        }

        public static Attribute[] GetCustomAttributes(ParameterInfo element) => GetCustomAttributes(element, typeof(Attribute), true);
        public static Attribute[] GetCustomAttributes(ParameterInfo element, bool inherit) => GetCustomAttributes(element, typeof(Attribute), inherit);

        public static Attribute GetCustomAttribute(ParameterInfo element, Type attributeType) => GetCustomAttribute(element, attributeType, true);
        public static Attribute GetCustomAttribute(ParameterInfo element, Type attributeType, bool inherit) =>
            Single(GetCustomAttributes(element, attributeType, inherit), attributeType);

        public static bool IsDefined(ParameterInfo element, Type attributeType) => IsDefined(element, attributeType, true);
        public static bool IsDefined(ParameterInfo element, Type attributeType, bool inherit)
        {
            Check(element, attributeType);
            return element.IsDefined(attributeType, inherit);
        }
    }

    [ShadowMembers("System.Reflection.CustomAttributeExtensions")]
    internal static class CustomAttributeExtensionsMembers
    {
        public static Attribute GetCustomAttribute(MemberInfo element, Type attributeType) => Attribute.GetCustomAttribute(element, attributeType);
        public static Attribute GetCustomAttribute(MemberInfo element, Type attributeType, bool inherit) => Attribute.GetCustomAttribute(element, attributeType, inherit);
        public static T GetCustomAttribute<T>(MemberInfo element) where T : Attribute => (T)Attribute.GetCustomAttribute(element, typeof(T));
        public static T GetCustomAttribute<T>(MemberInfo element, bool inherit) where T : Attribute => (T)Attribute.GetCustomAttribute(element, typeof(T), inherit);
        public static IEnumerable<Attribute> GetCustomAttributes(MemberInfo element) => Attribute.GetCustomAttributes(element);
        public static IEnumerable<Attribute> GetCustomAttributes(MemberInfo element, bool inherit) => Attribute.GetCustomAttributes(element, inherit);
        public static IEnumerable<Attribute> GetCustomAttributes(MemberInfo element, Type attributeType) => Attribute.GetCustomAttributes(element, attributeType);
        public static IEnumerable<Attribute> GetCustomAttributes(MemberInfo element, Type attributeType, bool inherit) => Attribute.GetCustomAttributes(element, attributeType, inherit);
        public static IEnumerable<T> GetCustomAttributes<T>(MemberInfo element) where T : Attribute => (IEnumerable<T>)Attribute.GetCustomAttributes(element, typeof(T));
        public static IEnumerable<T> GetCustomAttributes<T>(MemberInfo element, bool inherit) where T : Attribute => (IEnumerable<T>)Attribute.GetCustomAttributes(element, typeof(T), inherit);
        public static bool IsDefined(MemberInfo element, Type attributeType) => Attribute.IsDefined(element, attributeType);
        public static bool IsDefined(MemberInfo element, Type attributeType, bool inherit) => Attribute.IsDefined(element, attributeType, inherit);

        public static Attribute GetCustomAttribute(ParameterInfo element, Type attributeType) => Attribute.GetCustomAttribute(element, attributeType);
        public static Attribute GetCustomAttribute(ParameterInfo element, Type attributeType, bool inherit) => Attribute.GetCustomAttribute(element, attributeType, inherit);
        public static T GetCustomAttribute<T>(ParameterInfo element) where T : Attribute => (T)Attribute.GetCustomAttribute(element, typeof(T));
        public static T GetCustomAttribute<T>(ParameterInfo element, bool inherit) where T : Attribute => (T)Attribute.GetCustomAttribute(element, typeof(T), inherit);
        public static IEnumerable<Attribute> GetCustomAttributes(ParameterInfo element) => Attribute.GetCustomAttributes(element);
        public static IEnumerable<Attribute> GetCustomAttributes(ParameterInfo element, bool inherit) => Attribute.GetCustomAttributes(element, inherit);
        public static IEnumerable<Attribute> GetCustomAttributes(ParameterInfo element, Type attributeType) => Attribute.GetCustomAttributes(element, attributeType);
        public static IEnumerable<Attribute> GetCustomAttributes(ParameterInfo element, Type attributeType, bool inherit) => Attribute.GetCustomAttributes(element, attributeType, inherit);
        public static IEnumerable<T> GetCustomAttributes<T>(ParameterInfo element) where T : Attribute => (IEnumerable<T>)Attribute.GetCustomAttributes(element, typeof(T));
        public static IEnumerable<T> GetCustomAttributes<T>(ParameterInfo element, bool inherit) where T : Attribute => (IEnumerable<T>)Attribute.GetCustomAttributes(element, typeof(T), inherit);
        public static bool IsDefined(ParameterInfo element, Type attributeType) => Attribute.IsDefined(element, attributeType);
        public static bool IsDefined(ParameterInfo element, Type attributeType, bool inherit) => Attribute.IsDefined(element, attributeType, inherit);
    }
}
