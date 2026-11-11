// Attribute.GetCustomAttributes / GetCustomAttribute / IsDefined(MemberInfo, ...) as the framework's own IL calls them
// (FunctionCodeGen.FrameworkStaticAttributeQuery): the checks and the single-attribute rule of AttributeLookup, over the
// partial answers of the Framework* queries (a CoreLib type whose list csharpc cannot reproduce answers with what it models).
#nullable disable
using System;
using System.Reflection;

namespace CsharpcCoreLib
{
    internal static unsafe partial class ReflectionRuntime
    {
        private static void CheckStatic(MemberInfo element, Type attributeType)
        {
            ArgumentNullException.ThrowIfNull(element, "element");
            ArgumentNullException.ThrowIfNull(attributeType, "attributeType");
            if (attributeType != typeof(Attribute) && !attributeType.IsSubclassOf(typeof(Attribute)))
                throw new ArgumentException("Type passed in must be derived from System.Attribute or System.Attribute itself.");
        }

        public static Attribute[] StaticAttributes(MemberInfo element, Type attributeType, bool inherit)
        {
            CheckStatic(element, attributeType);
            return CoreIntrinsics.ReinterpretAs<Attribute[]>(FrameworkAttributesOf(element, attributeType, inherit));
        }

        public static Attribute[] StaticAttributesAll(MemberInfo element, bool inherit) => StaticAttributes(element, typeof(Attribute), inherit);

        public static Attribute StaticAttribute(MemberInfo element, Type attributeType, bool inherit)
        {
            Attribute[] attributes = StaticAttributes(element, attributeType, inherit);
            if (attributes.Length == 0) return null;
            if (attributes.Length == 1) return attributes[0];
            throw new AmbiguousMatchException("Multiple custom attributes of the same type '" + attributeType + "' found.");
        }

        public static bool StaticIsDefined(MemberInfo element, Type attributeType, bool inherit)
        {
            CheckStatic(element, attributeType);
            return FrameworkIsDefined(element, attributeType, inherit);
        }
    }
}
