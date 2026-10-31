// System.Reflection.FieldInfo over csharpc's compile-time metadata (csrt_FieldRecord, kept for the types
// the program reflects over: [DynamicallyAccessedMembers(PublicFields)] locations and
// typeof(X).GetField(...) on a known type, as NativeAOT keeps them). GetValue/SetValue go through the
// record's generated accessors; the target and value checks are .NET's (RtFieldInfo).
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace CsharpcCoreLib
{
    [Shadows("System.Reflection.FieldInfo")]
    public abstract class FieldInfoImpl : MemberInfo
    {
        protected FieldInfoImpl() { }

        public override MemberTypes MemberType => MemberTypes.Field;
        public abstract FieldAttributes Attributes { get; }
        public abstract Type FieldType { get; }
        public abstract RuntimeFieldHandle FieldHandle { get; }

        public bool IsInitOnly => (Attributes & FieldAttributes.InitOnly) != 0;
        public bool IsLiteral => (Attributes & FieldAttributes.Literal) != 0;
        public bool IsNotSerialized => (Attributes & FieldAttributes.NotSerialized) != 0;
        public bool IsPinvokeImpl => (Attributes & FieldAttributes.PinvokeImpl) != 0;
        public bool IsSpecialName => (Attributes & FieldAttributes.SpecialName) != 0;
        public bool IsStatic => (Attributes & FieldAttributes.Static) != 0;
        public bool IsAssembly => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Assembly;
        public bool IsFamily => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Family;
        public bool IsFamilyAndAssembly => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.FamANDAssem;
        public bool IsFamilyOrAssembly => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.FamORAssem;
        public bool IsPrivate => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private;
        public bool IsPublic => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public;
        public virtual bool IsSecurityCritical => true;
        public virtual bool IsSecuritySafeCritical => false;
        public virtual bool IsSecurityTransparent => false;

        public abstract object GetValue(object obj);
        public void SetValue(object obj, object value) => SetValue(obj, value, BindingFlags.Default, null, null);
        public abstract void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, CultureInfo culture);
        public virtual object GetRawConstantValue() => throw new NotSupportedException("This non-CLS method is not implemented.");
        public virtual Type GetModifiedFieldType() => throw new NotSupportedException("FieldInfo.GetModifiedFieldType is not supported by csharpc yet.");
        public virtual Type[] GetOptionalCustomModifiers() => new Type[0];
        public virtual Type[] GetRequiredCustomModifiers() => new Type[0];

        public static bool operator ==(FieldInfoImpl left, FieldInfoImpl right) => ReferenceEquals(left, right) || (left is not null && left.Equals((object)right));
        public static bool operator !=(FieldInfoImpl left, FieldInfoImpl right) => !(left == right);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
    }

    [Shadows("System.Reflection.RtFieldInfo")]
    internal sealed unsafe class RuntimeFieldInfoImpl : FieldInfo
    {
        internal readonly byte* _record;
        private string _name;

        internal RuntimeFieldInfoImpl(byte* record) { _record = record; }

        public override string Name => _name ??= Utf8.FromNative(ReflectionRecords.FieldName(_record));
        public override Type FieldType => ReflectionRuntime.TypeOf(ReflectionRecords.FieldType(_record));
        public override Type DeclaringType => ReflectionRuntime.TypeOf(ReflectionRecords.FieldDeclaring(_record));
        public override Type ReflectedType => DeclaringType;
        public override FieldAttributes Attributes => (FieldAttributes)ReflectionRecords.FieldFlags(_record);
        public override RuntimeFieldHandle FieldHandle => throw new NotSupportedException("FieldInfo.FieldHandle is not supported by csharpc yet.");

        public override object GetValue(object obj)
        {
            CheckTarget(obj);
            return ((delegate*<object, object>)ReflectionRecords.FieldGet(_record))(IsStatic ? null : obj);
        }

        public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, CultureInfo culture)
        {
            if (IsLiteral) throw new FieldAccessException("Cannot set a constant field.");
            CheckTarget(obj);
            if (IsStatic && IsInitOnly)
                throw new FieldAccessException("Cannot set initonly static field '" + Name + "' after type '" + DeclaringType + "' is initialized.");
            Type type = FieldType;
            if (value != null && !type.IsInstanceOfType(value)
                && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) && type.GetGenericArguments()[0] == value.GetType()))
                throw new ArgumentException("Object of type '" + value.GetType() + "' cannot be converted to type '" + type + "'.");
            ((delegate*<object, object, void>)ReflectionRecords.FieldSet(_record))(IsStatic ? null : obj, value);
        }

        private void CheckTarget(object obj)
        {
            if (IsStatic) return;
            if (obj == null) throw new TargetException("Non-static field requires a target.");
            if (!DeclaringType.IsInstanceOfType(obj))
                throw new ArgumentException("Field '" + Name + "' defined on type '" + DeclaringType + "' is not a field on the target object which is of type '" + obj.GetType() + "'.");
        }

        public override object GetRawConstantValue()
        {
            if (!IsLiteral) throw new InvalidOperationException("Operation is not valid due to the current state of the object.");
            object value = GetValue(null);
            return value is Enum ? Convert.ChangeType(value, FieldType.GetEnumUnderlyingType(), CultureInfo.InvariantCulture) : value;
        }

        public override bool IsDefined(Type attributeType, bool inherit) =>
            ReflectionRuntime.IsDefined(ReflectionRecords.FieldAttributes(_record), ReflectionRecords.FieldAttributeCount(_record), attributeType);
        public override object[] GetCustomAttributes(bool inherit) => GetCustomAttributes(typeof(object), inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) =>
            ReflectionRuntime.CreateAttributes(ReflectionRecords.FieldAttributes(_record), ReflectionRecords.FieldAttributeCount(_record), attributeType);
        public override IList<CustomAttributeData> GetCustomAttributesData() =>
            ReflectionRuntime.AttributeData(ReflectionRecords.FieldAttributes(_record), ReflectionRecords.FieldAttributeCount(_record));

        public override string ToString() => ReflectionRuntime.FormatTypeName(FieldType) + " " + Name;
    }

    internal static unsafe class FieldReflection
    {
        private static readonly object s_lock = new object();
        private static Dictionary<nint, FieldInfo> s_fields;

        internal static FieldInfo Field(byte* record)
        {
            lock (s_lock)
            {
                s_fields ??= new Dictionary<nint, FieldInfo>();
                if (!s_fields.TryGetValue((nint)record, out var field))
                    s_fields[(nint)record] = field = new RuntimeFieldInfoImpl(record);
                return field;
            }
        }

        // RuntimeType's filter: visibility and static/instance; inherited static fields only with FlattenHierarchy.
        private static bool Matches(FieldAttributes attributes, bool inherited, BindingFlags flags)
        {
            bool isPublic = (attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public;
            bool isStatic = (attributes & FieldAttributes.Static) != 0;
            if (!((isPublic && (flags & BindingFlags.Public) != 0) || (!isPublic && (flags & BindingFlags.NonPublic) != 0))) return false;
            if (!(isStatic ? (flags & BindingFlags.Static) != 0 : (flags & BindingFlags.Instance) != 0)) return false;
            if (inherited && (flags & BindingFlags.DeclaredOnly) != 0) return false;
            if (inherited && isStatic && (flags & BindingFlags.FlattenHierarchy) == 0) return false;
            return true;
        }

        public static FieldInfo[] GetFieldsAll(Type type) => GetFields(type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        public static FieldInfo[] GetFieldsFlags(Type type, BindingFlags flags) => GetFields(type, flags);

        internal static FieldInfo[] GetFields(Type type, BindingFlags flags)
        {
            byte* info = ReflectionRuntime.Info(type, "fields");
            ReflectionRuntime.Require(info, type, flags, 256, 512, "fields");
            var list = new List<FieldInfo>();
            byte* records = ReflectionRecords.InfoFields(info);
            int count = ReflectionRecords.InfoFieldCount(info);
            nint self = CoreIntrinsics.csrt_type_handle(type);
            for (int i = 0; i < count; i++)
            {
                byte* record = records + i * ReflectionRecords.FieldSize;
                bool inherited = ReflectionRecords.FieldDeclaring(record) != self;
                if (Matches((FieldAttributes)ReflectionRecords.FieldFlags(record), inherited, flags)) list.Add(Field(record));
            }
            return list.ToArray();
        }

        public static FieldInfo GetFieldName(Type type, string name) => GetField(type, name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        public static FieldInfo GetFieldNameFlags(Type type, string name, BindingFlags flags) => GetField(type, name, flags);

        private static FieldInfo GetField(Type type, string name, BindingFlags flags)
        {
            ArgumentNullException.ThrowIfNull(name);
            var comparison = (flags & BindingFlags.IgnoreCase) != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            FieldInfo found = null;
            foreach (var f in GetFields(type, flags))
            {
                if (!string.Equals(f.Name, name, comparison)) continue;
                if (found == null) found = f;
                else if (found.DeclaringType == f.DeclaringType) throw new AmbiguousMatchException("Ambiguous match found for '" + type + " " + f.FieldType + " " + name + "'.");
            }
            return found;
        }
    }

    /// <summary>
    /// The MemberInfo objects of standalone records (ModuleCodeGen.MemberRecords.cs: expression trees' member
    /// constants, `ldtoken` + GetMethodFromHandle / GetFieldFromHandle), one per member.
    /// </summary>
    internal static unsafe class MemberRecords
    {
        public static MethodInfo Method(nint record) => (MethodInfo)ReflectionRuntime.Member((byte*)record, false);
        public static ConstructorInfo Constructor(nint record) => (ConstructorInfo)ReflectionRuntime.Member((byte*)record, true);
        public static MethodBase MethodBase(nint record, bool constructor) => (MethodBase)ReflectionRuntime.Member((byte*)record, constructor);
        public static FieldInfo Field(nint record) => FieldReflection.Field((byte*)record);
    }
}
