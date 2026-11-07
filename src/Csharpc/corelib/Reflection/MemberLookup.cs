// Type.GetMember / GetMembers and TypeInfo's Declared* lookups, composed from the per-kind queries (GetMethods,
// GetConstructors, GetProperties, GetEvents, GetFields) the way RuntimeType does: candidates of each requested kind
// in the order methods, constructors, properties, events, fields, filtered by name (exact, ignoring case on request,
// or a prefix when the name ends in '*'). Nested types are not described by csharpc's reflection metadata yet:
// asking for them alone throws, and `MemberTypes.All` leaves them out.
#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CsharpcCoreLib
{
    internal static class MemberLookup
    {
        private const BindingFlags DeclaredOnlyLookup = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const BindingFlags PublicLookup = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

        public static MemberInfo[] GetMemberAll(Type type, string name) => GetMemberTypesFlags(type, name, MemberTypes.All, PublicLookup);

        public static MemberInfo[] GetMemberFlags(Type type, string name, BindingFlags flags) => GetMemberTypesFlags(type, name, MemberTypes.All, flags);

        public static MemberInfo[] GetMembersAll(Type type) => Collect(type, null, MemberTypes.All, PublicLookup);

        public static MemberInfo[] GetMembersFlags(Type type, BindingFlags flags) => Collect(type, null, MemberTypes.All, flags);

        public static MemberInfo[] GetMemberTypesFlags(Type type, string name, MemberTypes memberTypes, BindingFlags flags)
        {
            if (name == null) throw new ArgumentNullException("name");
            return Collect(type, name, memberTypes, flags);
        }

        private static MemberInfo[] Collect(Type type, string name, MemberTypes types, BindingFlags flags)
        {
            if (type == null) throw new NullReferenceException();
            if (types == MemberTypes.NestedType)
                throw new NotSupportedException("Type.GetMember for nested types is not supported by csharpc yet.");
            bool ignoreCase = (flags & BindingFlags.IgnoreCase) != 0;
            bool prefix = false;
            string filter = name;
            if (filter != null && filter.Length > 0 && filter[filter.Length - 1] == '*')
            {
                filter = filter.Substring(0, filter.Length - 1);
                prefix = true;
            }
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            bool Matches(string candidate) => filter == null || (prefix ? candidate.StartsWith(filter, comparison) : string.Equals(candidate, filter, comparison));

            var methods = new List<MethodInfo>();
            var constructors = new List<ConstructorInfo>();
            var properties = new List<PropertyInfo>();
            var events = new List<EventInfo>();
            var fields = new List<FieldInfo>();
            if ((types & MemberTypes.Method) != 0)
                foreach (var m in type.GetMethods(flags)) if (Matches(m.Name)) methods.Add(m);
            if ((types & MemberTypes.Constructor) != 0)
                foreach (var c in type.GetConstructors(flags)) if (Matches(c.Name)) constructors.Add(c);
            if ((types & MemberTypes.Property) != 0)
                foreach (var p in type.GetProperties(flags)) if (Matches(p.Name)) properties.Add(p);
            if ((types & MemberTypes.Event) != 0)
                foreach (var e in type.GetEvents(flags)) if (Matches(e.Name)) events.Add(e);
            if ((types & MemberTypes.Field) != 0)
                foreach (var f in type.GetFields(flags)) if (Matches(f.Name)) fields.Add(f);

            // RuntimeType hands back an array of the one kind asked for (a MethodInfo[] for Method). csharpc's arrays carry their
            // element type and a MethodInfo[] is not an IEnumerable<MemberInfo> there (docs/12-corelib.md, "Arrays and strings as
            // interfaces"), which would break `members.Where(...)`: the result is always a MemberInfo[].
            var all = new MemberInfo[methods.Count + constructors.Count + properties.Count + events.Count + fields.Count];
            int at = 0;
            foreach (var m in methods) all[at++] = m;
            foreach (var c in constructors) all[at++] = c;
            foreach (var p in properties) all[at++] = p;
            foreach (var e in events) all[at++] = e;
            foreach (var f in fields) all[at++] = f;
            return all;
        }

        // ---- TypeInfo ----

        public static PropertyInfo GetDeclaredProperty(Type type, string name)
        {
            if (name == null) throw new ArgumentNullException("name");
            return type.GetProperty(name, DeclaredOnlyLookup);
        }

        public static FieldInfo GetDeclaredField(Type type, string name)
        {
            if (name == null) throw new ArgumentNullException("name");
            return type.GetField(name, DeclaredOnlyLookup);
        }

        public static EventInfo GetDeclaredEvent(Type type, string name)
        {
            if (name == null) throw new ArgumentNullException("name");
            return type.GetEvent(name, DeclaredOnlyLookup);
        }

        public static TypeInfo GetDeclaredNestedType(Type type, string name)
            => throw new NotSupportedException("TypeInfo.GetDeclaredNestedType is not supported by csharpc yet.");

        public static IEnumerable<PropertyInfo> DeclaredProperties(Type type) => type.GetProperties(DeclaredOnlyLookup);

        public static IEnumerable<FieldInfo> DeclaredFields(Type type) => type.GetFields(DeclaredOnlyLookup);

        public static IEnumerable<EventInfo> DeclaredEvents(Type type) => type.GetEvents(DeclaredOnlyLookup);

        public static IEnumerable<MethodInfo> DeclaredMethods(Type type) => type.GetMethods(DeclaredOnlyLookup);

        public static IEnumerable<TypeInfo> DeclaredNestedTypes(Type type)
            => throw new NotSupportedException("TypeInfo.DeclaredNestedTypes is not supported by csharpc yet.");

        public static IEnumerable<MemberInfo> DeclaredMembers(Type type) => Collect(type, null, MemberTypes.All, DeclaredOnlyLookup);
    }
}
