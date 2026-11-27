// Type.GetNestedType(s) over the nested types csharpc keeps for reflection (ModuleCodeGen.NestedTypes.cs): those of a type whose
// nested types reach a [DynamicallyAccessedMembers(PublicNestedTypes | NonPublicNestedTypes)] location, as NativeAOT keeps them.
// RuntimeType's filter: visibility only (nested types are neither static nor instance members, and are never inherited), the
// name exact or ignoring case. A type whose nested types were not kept is an explicit NotSupportedException.
#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CsharpcCoreLib
{
    internal static unsafe class NestedTypeReflection
    {
        private const BindingFlags DefaultLookup = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

        public static Type GetNestedTypeName(Type type, string name) => GetNestedTypeNameFlags(type, name, DefaultLookup);

        public static Type GetNestedTypeNameFlags(Type type, string name, BindingFlags flags)
        {
            ArgumentNullException.ThrowIfNull(name);
            Type found = null;
            foreach (var nested in Collect(type, flags))
            {
                if (!string.Equals(nested.Name, name, (flags & BindingFlags.IgnoreCase) != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                if (found != null) throw new AmbiguousMatchException("Ambiguous match found for '" + type + " " + name + "'.");
                found = nested;
            }
            return found;
        }

        public static Type[] GetNestedTypesAll(Type type) => GetNestedTypesFlags(type, DefaultLookup);

        public static Type[] GetNestedTypesFlags(Type type, BindingFlags flags) => Collect(type, flags).ToArray();

        private static List<Type> Collect(Type type, BindingFlags flags)
        {
            var result = new List<Type>();
            if ((flags & BindingFlags.Public) != 0) Add(type, 0, result);
            if ((flags & BindingFlags.NonPublic) != 0) Add(type, 1, result);
            return result;
        }

        private static void Add(Type type, int which, List<Type> result)
        {
            void** table = CoreIntrinsics.NestedTypeTable(type, which);
            if (table == null)
                throw new NotSupportedException("Type.GetNestedType(s) of '" + type + "' is not supported by csharpc yet: its nested types are kept for reflection only when they reach a [DynamicallyAccessedMembers(PublicNestedTypes)] location.");
            for (; *table != null; table++) result.Add(CoreIntrinsics.csrt_type_from_mt(*table));
        }
    }
}
