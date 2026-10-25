// System.Reflection.PropertyInfo over csharpc's compile-time metadata (csrt_PropertyRecord, kept for the
// types the program reflects over: [DynamicallyAccessedMembers(PublicProperties)] locations and
// typeof(X).GetProperty(...) on a known type, as NativeAOT keeps them). The accessors are the methods'
// own records, so GetValue/SetValue go through their invokers.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace CsharpcCoreLib
{
    [Shadows("System.Reflection.PropertyInfo")]
    public abstract class PropertyInfoImpl : MemberInfo
    {
        protected PropertyInfoImpl() { }

        public override MemberTypes MemberType => MemberTypes.Property;
        public abstract Type PropertyType { get; }
        public abstract PropertyAttributes Attributes { get; }
        public bool IsSpecialName => (Attributes & PropertyAttributes.SpecialName) != 0;
        public abstract bool CanRead { get; }
        public abstract bool CanWrite { get; }
        public virtual MethodInfo GetMethod => GetGetMethod(true);
        public virtual MethodInfo SetMethod => GetSetMethod(true);
        public MethodInfo GetGetMethod() => GetGetMethod(false);
        public abstract MethodInfo GetGetMethod(bool nonPublic);
        public MethodInfo GetSetMethod() => GetSetMethod(false);
        public abstract MethodInfo GetSetMethod(bool nonPublic);
        public MethodInfo[] GetAccessors() => GetAccessors(false);
        public abstract MethodInfo[] GetAccessors(bool nonPublic);
        public abstract ParameterInfo[] GetIndexParameters();

        public object GetValue(object obj) => GetValue(obj, null);
        public virtual object GetValue(object obj, object[] index) => GetValue(obj, BindingFlags.Default, null, index, null);
        public abstract object GetValue(object obj, BindingFlags invokeAttr, Binder binder, object[] index, CultureInfo culture);
        public void SetValue(object obj, object value) => SetValue(obj, value, null);
        public virtual void SetValue(object obj, object value, object[] index) => SetValue(obj, value, BindingFlags.Default, null, index, null);
        public abstract void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, object[] index, CultureInfo culture);

        public virtual object GetConstantValue() => throw new InvalidOperationException("Operation is not valid due to the current state of the object.");
        public virtual object GetRawConstantValue() => throw new InvalidOperationException("Operation is not valid due to the current state of the object.");
        public virtual Type[] GetOptionalCustomModifiers() => new Type[0];
        public virtual Type[] GetRequiredCustomModifiers() => new Type[0];

        public static bool operator ==(PropertyInfoImpl left, PropertyInfoImpl right) => ReferenceEquals(left, right) || (left is not null && left.Equals((object)right));
        public static bool operator !=(PropertyInfoImpl left, PropertyInfoImpl right) => !(left == right);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
    }

    [Shadows("System.Reflection.RuntimePropertyInfo")]
    internal sealed unsafe class RuntimePropertyInfoImpl : PropertyInfo
    {
        internal readonly byte* _record;
        private string _name;

        internal RuntimePropertyInfoImpl(byte* record) { _record = record; }

        private MethodInfo Accessor(byte* record) => record == null ? null : (MethodInfo)ReflectionRuntime.Member(record, false);
        private MethodInfo Getter => Accessor(ReflectionRecords.PropertyGetter(_record));
        private MethodInfo Setter => Accessor(ReflectionRecords.PropertySetter(_record));

        public override string Name => _name ??= Utf8.FromNative(ReflectionRecords.PropertyName(_record));
        public override Type PropertyType => ReflectionRuntime.TypeOf(ReflectionRecords.PropertyType(_record));
        public override Type DeclaringType => (Getter ?? Setter).DeclaringType;
        public override Type ReflectedType => DeclaringType;
        public override PropertyAttributes Attributes => PropertyAttributes.None;
        public override bool CanRead => Getter != null;
        public override bool CanWrite => Setter != null;

        public override MethodInfo GetGetMethod(bool nonPublic) => Getter is { } g && (nonPublic || g.IsPublic) ? g : null;
        public override MethodInfo GetSetMethod(bool nonPublic) => Setter is { } s && (nonPublic || s.IsPublic) ? s : null;

        public override MethodInfo[] GetAccessors(bool nonPublic)
        {
            var list = new List<MethodInfo>(2);
            if (GetGetMethod(nonPublic) is { } g) list.Add(g);
            if (GetSetMethod(nonPublic) is { } s) list.Add(s);
            return list.ToArray();
        }

        public override ParameterInfo[] GetIndexParameters()
        {
            if (Getter is { } g) return g.GetParameters();
            if (Setter is { } s)
            {
                ParameterInfo[] all = s.GetParameters();
                var index = new ParameterInfo[all.Length - 1];
                Array.Copy(all, index, index.Length);
                return index;
            }
            return new ParameterInfo[0];
        }

        public override object GetValue(object obj, BindingFlags invokeAttr, Binder binder, object[] index, CultureInfo culture)
        {
            MethodInfo getter = Getter ?? throw new ArgumentException("Property Get method was not found.");
            return getter.Invoke(obj, invokeAttr, binder, index, culture);
        }

        public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, object[] index, CultureInfo culture)
        {
            MethodInfo setter = Setter ?? throw new ArgumentException("Property set method not found.");
            object[] args;
            if (index == null || index.Length == 0) args = new object[] { value };
            else
            {
                args = new object[index.Length + 1];
                Array.Copy(index, args, index.Length);
                args[index.Length] = value;
            }
            setter.Invoke(obj, invokeAttr, binder, args, culture);
        }

        public override bool IsDefined(Type attributeType, bool inherit) =>
            ReflectionRuntime.IsDefined(ReflectionRecords.PropertyAttributes(_record), ReflectionRecords.PropertyAttributeCount(_record), attributeType);
        public override object[] GetCustomAttributes(bool inherit) => GetCustomAttributes(typeof(object), inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) =>
            ReflectionRuntime.CreateAttributes(ReflectionRecords.PropertyAttributes(_record), ReflectionRecords.PropertyAttributeCount(_record), attributeType);
        public override IList<CustomAttributeData> GetCustomAttributesData() =>
            ReflectionRuntime.AttributeData(ReflectionRecords.PropertyAttributes(_record), ReflectionRecords.PropertyAttributeCount(_record));

        public override string ToString() => ReflectionRuntime.FormatTypeName(PropertyType) + " " + Name;
    }

    internal static unsafe class PropertyReflection
    {
        private static readonly object s_lock = new object();
        private static Dictionary<nint, PropertyInfo> s_properties;

        private static PropertyInfo Property(byte* record)
        {
            lock (s_lock)
            {
                s_properties ??= new Dictionary<nint, PropertyInfo>();
                if (!s_properties.TryGetValue((nint)record, out var property))
                    s_properties[(nint)record] = property = new RuntimePropertyInfoImpl(record);
                return property;
            }
        }

        private static bool Matches(int flags, BindingFlags binding)
        {
            bool isPublic = (flags & 1) != 0, isStatic = (flags & 2) != 0;
            if (!((isPublic && (binding & BindingFlags.Public) != 0) || (!isPublic && (binding & BindingFlags.NonPublic) != 0))) return false;
            return isStatic ? (binding & BindingFlags.Static) != 0 : (binding & BindingFlags.Instance) != 0;
        }

        public static PropertyInfo[] GetPropertiesAll(Type type) => GetProperties(type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        public static PropertyInfo[] GetPropertiesFlags(Type type, BindingFlags flags) => GetProperties(type, flags);

        internal static PropertyInfo[] GetProperties(Type type, BindingFlags flags)
        {
            byte* info = ReflectionRuntime.Info(type, "properties");
            ReflectionRuntime.Require(info, type, flags, 32, 64, "properties");
            var list = new List<PropertyInfo>();
            byte* records = ReflectionRecords.InfoProperties(info);
            int count = ReflectionRecords.InfoPropertyCount(info);
            bool declaredOnly = (flags & BindingFlags.DeclaredOnly) != 0;
            nint self = CoreIntrinsics.csrt_type_handle(type);
            for (int i = 0; i < count; i++)
            {
                byte* record = records + i * ReflectionRecords.PropertySize;
                int f = ReflectionRecords.PropertyFlags(record);
                if (!Matches(f, flags)) continue;
                if (declaredOnly)
                {
                    byte* accessor = ReflectionRecords.PropertyGetter(record);
                    if (accessor == null) accessor = ReflectionRecords.PropertySetter(record);
                    if (ReflectionRecords.MethodDeclaring(accessor) != self) continue;
                }
                list.Add(Property(record));
            }
            return list.ToArray();
        }

        public static PropertyInfo GetPropertyName(Type type, string name) => GetProperty(type, name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, null, null);
        public static PropertyInfo GetPropertyNameFlags(Type type, string name, BindingFlags flags) => GetProperty(type, name, flags, null, null);
        public static PropertyInfo GetPropertyNameReturn(Type type, string name, Type returnType) =>
            GetProperty(type, name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, returnType, null);
        public static PropertyInfo GetPropertyNameReturnTypes(Type type, string name, Type returnType, Type[] types) =>
            GetProperty(type, name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, returnType, types ?? throw new ArgumentNullException("types"));
        public static PropertyInfo GetPropertyFull(Type type, string name, BindingFlags flags, Binder binder, Type returnType, Type[] types, ParameterModifier[] modifiers)
        {
            if (binder != null) throw new NotSupportedException("A custom Binder in GetProperty is not supported by csharpc yet.");
            return GetProperty(type, name, flags, returnType, types ?? throw new ArgumentNullException("types"));
        }

        private static PropertyInfo GetProperty(Type type, string name, BindingFlags flags, Type returnType, Type[] types)
        {
            ArgumentNullException.ThrowIfNull(name);
            var comparison = (flags & BindingFlags.IgnoreCase) != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            PropertyInfo found = null;
            foreach (var p in GetProperties(type, flags))
            {
                if (!string.Equals(p.Name, name, comparison)) continue;
                if (returnType != null && p.PropertyType != returnType) continue;
                if (types != null)
                {
                    ParameterInfo[] index = p.GetIndexParameters();
                    if (index.Length != types.Length) continue;
                    bool same = true;
                    for (int i = 0; i < types.Length && same; i++) same = index[i].ParameterType == types[i];
                    if (!same) continue;
                }
                if (found != null)
                {
                    // .NET prefers the most derived declaration when several have the name (new slots).
                    if (found.DeclaringType != p.DeclaringType && p.DeclaringType.IsAssignableFrom(found.DeclaringType)) continue;
                    throw new AmbiguousMatchException("Ambiguous match found for '" + type + " " + p + "'.");
                }
                found = p;
            }
            return found;
        }
    }
}
