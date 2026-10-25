// Member reflection over compile-time metadata (docs/adr/0018-reflection-metadata.md): the public
// System.Reflection base classes (ports of dotnet/runtime's MemberInfo, MethodBase, ConstructorInfo,
// MethodInfo, ParameterInfo, CustomAttributeData and its argument structs, release/11.0, MIT license)
// and csharpc's runtime implementations reading the tables ModuleCodeGen.Reflection.cs generates
// (csrt_ReflectionInfo & co. in src/runtime/csrt.h). Like NativeAOT, csharpc keeps metadata only for
// what the program can reach through [DynamicallyAccessedMembers]: asking for members it didn't keep
// throws NotSupportedException instead of answering with an empty list.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Reflection.MemberInfo")]
    public abstract class MemberInfoImpl : ICustomAttributeProvider
    {
        protected MemberInfoImpl() { }
        public abstract MemberTypes MemberType { get; }
        public abstract string Name { get; }
        public abstract Type DeclaringType { get; }
        public abstract Type ReflectedType { get; }
        public virtual bool HasSameMetadataDefinitionAs(MemberInfo other) => throw new NotImplementedException();
        public abstract bool IsDefined(Type attributeType, bool inherit);
        public abstract object[] GetCustomAttributes(bool inherit);
        public abstract object[] GetCustomAttributes(Type attributeType, bool inherit);
        public virtual IEnumerable<CustomAttributeData> CustomAttributes => GetCustomAttributesData();
        public virtual IList<CustomAttributeData> GetCustomAttributesData() => throw new NotImplementedException();
        public virtual bool IsCollectible => true;
        public virtual int MetadataToken => throw new InvalidOperationException();
        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => base.GetHashCode();

        public static bool operator ==(MemberInfoImpl left, MemberInfoImpl right)
        {
            if ((object)right == null) return (object)left == null;
            if (ReferenceEquals(left, right)) return true;
            return (object)left != null && left.Equals(right);
        }
        public static bool operator !=(MemberInfoImpl left, MemberInfoImpl right) => !(left == right);
    }

    [Shadows("System.Reflection.MethodBase")]
    public abstract class MethodBaseImpl : MemberInfo
    {
        protected MethodBaseImpl() { }
        public abstract ParameterInfo[] GetParameters();
        public abstract MethodAttributes Attributes { get; }
        public virtual MethodImplAttributes MethodImplementationFlags => GetMethodImplementationFlags();
        public abstract MethodImplAttributes GetMethodImplementationFlags();
        public virtual CallingConventions CallingConvention => CallingConventions.Standard;
        public bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;
        public bool IsConstructor => this is ConstructorInfo && !IsStatic && (Attributes & MethodAttributes.RTSpecialName) == MethodAttributes.RTSpecialName;
        public bool IsFinal => (Attributes & MethodAttributes.Final) != 0;
        public bool IsHideBySig => (Attributes & MethodAttributes.HideBySig) != 0;
        public bool IsSpecialName => (Attributes & MethodAttributes.SpecialName) != 0;
        public bool IsStatic => (Attributes & MethodAttributes.Static) != 0;
        public bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;
        public bool IsAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Assembly;
        public bool IsFamily => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family;
        public bool IsFamilyAndAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamANDAssem;
        public bool IsFamilyOrAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamORAssem;
        public bool IsPrivate => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private;
        public bool IsPublic => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
        public virtual bool IsConstructedGenericMethod => IsGenericMethod && !IsGenericMethodDefinition;
        public virtual bool IsGenericMethod => false;
        public virtual bool IsGenericMethodDefinition => false;
        public virtual Type[] GetGenericArguments() => throw new NotSupportedException("Derived classes must provide an implementation.");
        public virtual bool ContainsGenericParameters => false;
        public object Invoke(object obj, object[] parameters) => Invoke(obj, BindingFlags.Default, null, parameters, null);
        public abstract object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture);
        public abstract RuntimeMethodHandle MethodHandle { get; }
        public virtual bool IsSecurityCritical => throw new NotImplementedException();
        public virtual bool IsSecuritySafeCritical => throw new NotImplementedException();
        public virtual bool IsSecurityTransparent => throw new NotImplementedException();
        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => base.GetHashCode();

        public static bool operator ==(MethodBaseImpl left, MethodBaseImpl right)
        {
            if ((object)right == null) return (object)left == null;
            if (ReferenceEquals(left, right)) return true;
            return (object)left != null && left.Equals(right);
        }
        public static bool operator !=(MethodBaseImpl left, MethodBaseImpl right) => !(left == right);
    }

    [Shadows("System.Reflection.ConstructorInfo")]
    public abstract class ConstructorInfoImpl : MethodBase
    {
        public static readonly string ConstructorName = ".ctor";
        public static readonly string TypeConstructorName = ".cctor";

        protected ConstructorInfoImpl() { }
        public override MemberTypes MemberType => MemberTypes.Constructor;
        public override Type[] GetGenericArguments() => new Type[0];
        public object Invoke(object[] parameters) => Invoke(BindingFlags.Default, null, parameters, null);
        public abstract object Invoke(BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture);
        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => base.GetHashCode();

        public static bool operator ==(ConstructorInfoImpl left, ConstructorInfoImpl right)
        {
            if ((object)right == null) return (object)left == null;
            if (ReferenceEquals(left, right)) return true;
            return (object)left != null && left.Equals(right);
        }
        public static bool operator !=(ConstructorInfoImpl left, ConstructorInfoImpl right) => !(left == right);
    }

    [Shadows("System.Reflection.MethodInfo")]
    public abstract class MethodInfoImpl : MethodBase
    {
        protected MethodInfoImpl() { }
        public override MemberTypes MemberType => MemberTypes.Method;
        public virtual ParameterInfo ReturnParameter => throw new NotImplementedException();
        public virtual Type ReturnType => throw new NotImplementedException();
        public override Type[] GetGenericArguments() => throw new NotSupportedException("Derived classes must provide an implementation.");
        public virtual MethodInfo GetGenericMethodDefinition() => throw new NotSupportedException("Derived classes must provide an implementation.");
        public virtual MethodInfo MakeGenericMethod(params Type[] typeArguments) => throw new NotSupportedException("Derived classes must provide an implementation.");
        public abstract MethodInfo GetBaseDefinition();
        public abstract ICustomAttributeProvider ReturnTypeCustomAttributes { get; }
        public virtual Delegate CreateDelegate(Type delegateType) => throw new NotSupportedException("Derived classes must provide an implementation.");
        public virtual Delegate CreateDelegate(Type delegateType, object target) => throw new NotSupportedException("Derived classes must provide an implementation.");
        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => base.GetHashCode();

        public static bool operator ==(MethodInfoImpl left, MethodInfoImpl right)
        {
            if ((object)right == null) return (object)left == null;
            if (ReferenceEquals(left, right)) return true;
            return (object)left != null && left.Equals(right);
        }
        public static bool operator !=(MethodInfoImpl left, MethodInfoImpl right) => !(left == right);
    }

    [Shadows("System.Reflection.ParameterInfo")]
    public class ParameterInfoImpl : ICustomAttributeProvider
    {
        protected ParameterAttributes AttrsImpl;
        protected Type ClassImpl;
        protected object DefaultValueImpl;
        protected MemberInfo MemberImpl;
        protected string NameImpl;
        protected int PositionImpl;

        protected ParameterInfoImpl() { }
        public virtual ParameterAttributes Attributes => AttrsImpl;
        public virtual MemberInfo Member => MemberImpl;
        public virtual string Name => NameImpl;
        public virtual Type ParameterType => ClassImpl;
        public virtual int Position => PositionImpl;
        public bool IsIn => (Attributes & ParameterAttributes.In) != 0;
        public bool IsLcid => (Attributes & ParameterAttributes.Lcid) != 0;
        public bool IsOptional => (Attributes & ParameterAttributes.Optional) != 0;
        public bool IsOut => (Attributes & ParameterAttributes.Out) != 0;
        public bool IsRetval => (Attributes & ParameterAttributes.Retval) != 0;
        public virtual object DefaultValue => throw new NotImplementedException();
        public virtual object RawDefaultValue => throw new NotImplementedException();
        public virtual bool HasDefaultValue => throw new NotImplementedException();
        public virtual bool IsDefined(Type attributeType, bool inherit)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            return false;
        }
        public virtual IEnumerable<CustomAttributeData> CustomAttributes => GetCustomAttributesData();
        public virtual IList<CustomAttributeData> GetCustomAttributesData() => throw new NotImplementedException();
        public virtual object[] GetCustomAttributes(bool inherit) => new object[0];
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            return new object[0];
        }
        public virtual Type[] GetOptionalCustomModifiers() => new Type[0];
        public virtual Type[] GetRequiredCustomModifiers() => new Type[0];
        public virtual int MetadataToken => 0x08000000;
        public override string ToString()
        {
            string typeName = ReflectionRuntime.FormatTypeName(ParameterType);
            string name = Name;
            return name is null ? typeName : typeName + " " + name;
        }
    }

    [Shadows("System.Reflection.CustomAttributeData")]
    public class CustomAttributeDataImpl
    {
        public static IList<CustomAttributeData> GetCustomAttributes(MemberInfo target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return target.GetCustomAttributesData();
        }

        public static IList<CustomAttributeData> GetCustomAttributes(ParameterInfo target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return target.GetCustomAttributesData();
        }

        protected CustomAttributeDataImpl() { }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(AttributeType.FullName).Append('(');
            bool first = true;
            IList<CustomAttributeTypedArgument> constructorArguments = ConstructorArguments;
            for (int i = 0; i < constructorArguments.Count; i++)
            {
                if (!first) sb.Append(", ");
                sb.Append(constructorArguments[i].ToString());
                first = false;
            }
            IList<CustomAttributeNamedArgument> namedArguments = NamedArguments;
            for (int i = 0; i < namedArguments.Count; i++)
            {
                if (!first) sb.Append(", ");
                sb.Append(namedArguments[i].ToString());
                first = false;
            }
            sb.Append(")]");
            return sb.ToString();
        }

        public virtual Type AttributeType => Constructor.DeclaringType;
        public virtual ConstructorInfo Constructor => null;
        public virtual IList<CustomAttributeTypedArgument> ConstructorArguments => null;
        public virtual IList<CustomAttributeNamedArgument> NamedArguments => null;
    }

    [Shadows("System.Reflection.CustomAttributeTypedArgument")]
    public readonly struct CustomAttributeTypedArgumentImpl : IEquatable<CustomAttributeTypedArgument>
    {
        private readonly object _value;
        private readonly Type _argumentType;

        public CustomAttributeTypedArgumentImpl(Type argumentType, object value)
        {
            ArgumentNullException.ThrowIfNull(argumentType);
            _value = value;
            _argumentType = argumentType;
        }

        public CustomAttributeTypedArgumentImpl(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _value = value;
            _argumentType = value.GetType();
        }

        public static bool operator ==(CustomAttributeTypedArgumentImpl left, CustomAttributeTypedArgumentImpl right) => left.Equals((CustomAttributeTypedArgument)(object)right);
        public static bool operator !=(CustomAttributeTypedArgumentImpl left, CustomAttributeTypedArgumentImpl right) => !left.Equals((CustomAttributeTypedArgument)(object)right);

        public Type ArgumentType => _argumentType;
        public object Value => _value;

        public override string ToString() => ToString(false);

        internal string ToString(bool typed)
        {
            if (_argumentType is null) return "System.Reflection.CustomAttributeTypedArgument";
            if (_argumentType.IsEnum) return typed ? Format(_value) : "(" + _argumentType.FullName + ")" + Format(_value);
            if (_value is null) return typed ? "null" : "(" + _argumentType.Name + ")null";
            if (_argumentType == typeof(string)) return "\"" + _value + "\"";
            if (_argumentType == typeof(char)) return "'" + _value + "'";
            if (_argumentType == typeof(Type)) return "typeof(" + ((Type)_value).FullName + ")";
            if (_argumentType.IsArray)
            {
                IList<CustomAttributeTypedArgument> array = (IList<CustomAttributeTypedArgument>)_value;
                Type elementType = _argumentType.GetElementType();
                var result = new StringBuilder();
                result.Append("new ").Append(elementType.IsEnum ? elementType.FullName : elementType.Name).Append('[').Append(array.Count).Append("] { ");
                for (int i = 0; i < array.Count; i++)
                {
                    if (i != 0) result.Append(", ");
                    result.Append(((CustomAttributeTypedArgumentImpl)(object)array[i]).ToString(elementType != typeof(object)));
                }
                result.Append(" }");
                return result.ToString();
            }
            return typed ? Format(_value) : "(" + _argumentType.Name + ")" + Format(_value);
        }

        private static string Format(object value) => value is IFormattable f ? f.ToString(null, null) : value?.ToString();

        public override int GetHashCode() => base.GetHashCode();
        public override bool Equals(object obj) => obj is CustomAttributeTypedArgument other && Equals(other);
        public bool Equals(CustomAttributeTypedArgument other)
        {
            var o = (CustomAttributeTypedArgumentImpl)(object)other;
            return _value == o._value && _argumentType == o._argumentType;
        }
    }

    /// <summary>
    /// csharpc keeps a named argument's member name and type, not its FieldInfo/PropertyInfo (no field or
    /// property metadata): MemberInfo throws NotSupportedException.
    /// </summary>
    [Shadows("System.Reflection.CustomAttributeNamedArgument")]
    public readonly struct CustomAttributeNamedArgumentImpl : IEquatable<CustomAttributeNamedArgument>
    {
        private readonly string _name;
        private readonly bool _isField;
        private readonly Type _memberType;
        private readonly CustomAttributeTypedArgument _value;

        internal CustomAttributeNamedArgumentImpl(string name, bool isField, Type memberType, CustomAttributeTypedArgument value)
        {
            _name = name;
            _isField = isField;
            _memberType = memberType;
            _value = value;
        }

        public static bool operator ==(CustomAttributeNamedArgumentImpl left, CustomAttributeNamedArgumentImpl right) => left.Equals((CustomAttributeNamedArgument)(object)right);
        public static bool operator !=(CustomAttributeNamedArgumentImpl left, CustomAttributeNamedArgumentImpl right) => !left.Equals((CustomAttributeNamedArgument)(object)right);

        public MemberInfo MemberInfo => throw new NotSupportedException("CustomAttributeNamedArgument.MemberInfo is not supported by csharpc yet (no field or property metadata).");
        public CustomAttributeTypedArgument TypedValue => _value;
        public string MemberName => _name;
        public bool IsField => _isField;

        public override string ToString()
        {
            if (_name is null) return "System.Reflection.CustomAttributeNamedArgument";
            return _name + " = " + ((CustomAttributeTypedArgumentImpl)(object)_value).ToString(_memberType != typeof(object));
        }

        public override int GetHashCode() => base.GetHashCode();
        public override bool Equals(object obj) => obj is CustomAttributeNamedArgument other && Equals(other);
        public bool Equals(CustomAttributeNamedArgument other)
        {
            var o = (CustomAttributeNamedArgumentImpl)(object)other;
            return _name == o._name && _isField == o._isField && _value == o._value;
        }
    }

    [Shadows("System.Reflection.TargetInvocationException")]
    public sealed class TargetInvocationExceptionImpl : ApplicationException
    {
        public TargetInvocationExceptionImpl(Exception inner) : base("Exception has been thrown by the target of an invocation.", inner) { }
        public TargetInvocationExceptionImpl(string message, Exception inner) : base(message, inner) { }
    }

    // The pseudo-custom attributes ParameterInfo.GetCustomAttributes returns for In/Out/Optional parameters.
    [Shadows("System.Runtime.InteropServices.InAttribute")]
    public sealed class InAttributeImpl : Attribute { public InAttributeImpl() { } }

    [Shadows("System.Runtime.InteropServices.OutAttribute")]
    public sealed class OutAttributeImpl : Attribute { public OutAttributeImpl() { } }

    [Shadows("System.Runtime.InteropServices.OptionalAttribute")]
    public sealed class OptionalAttributeImpl : Attribute { public OptionalAttributeImpl() { } }

    [Shadows("System.Reflection.Missing")]
    public sealed class MissingImpl
    {
        private static MissingImpl s_value;
        private MissingImpl() { }
        /// <summary>Missing.Value and Type.Missing (static readonly fields in .NET, read through BclValueTypes' field table).</summary>
        internal static object GetValue() => s_value ??= new MissingImpl();
    }

    [Shadows("System.DBNull")]
    public sealed class DBNullImpl
    {
        private static DBNullImpl s_value;
        private DBNullImpl() { }
        /// <summary>DBNull.Value (a static readonly field in .NET, read through BclValueTypes' field table).</summary>
        internal static object GetValue() => s_value ??= new DBNullImpl();
        public override string ToString() => string.Empty;
        public string ToString(IFormatProvider provider) => string.Empty;
    }

    // ==========================================================================================
    // csharpc's implementations over the compile-time tables
    // ==========================================================================================

    [Shadows("System.Reflection.RuntimeCustomAttributeData")]
    internal sealed class RuntimeCustomAttributeDataImpl : CustomAttributeData
    {
        private readonly Type _attributeType;
        private readonly Type[] _parameterTypes;
        private readonly IList<CustomAttributeTypedArgument> _arguments;
        private readonly IList<CustomAttributeNamedArgument> _named;

        private RuntimeCustomAttributeDataImpl(Type attributeType, Type[] parameterTypes, IList<CustomAttributeTypedArgument> arguments, IList<CustomAttributeNamedArgument> named)
        {
            _attributeType = attributeType;
            _parameterTypes = parameterTypes;
            _arguments = arguments;
            _named = named;
        }

        /// <summary>
        /// Called by the generated data factories (FunctionCodeGen.Reflection.cs): enums and primitives arrive
        /// boxed as their underlying primitive, arrays as object[] of their elements; named argument names are
        /// prefixed "F:" (field) or "P:" (property).
        /// </summary>
        public static CustomAttributeData Create(Type attributeType, Type[] parameterTypes, Type[] argumentTypes, object[] argumentValues,
            string[] namedNames, Type[] namedTypes, object[] namedValues)
        {
            var arguments = new CustomAttributeTypedArgument[argumentTypes.Length];
            for (int i = 0; i < arguments.Length; i++) arguments[i] = Typed(argumentTypes[i], argumentValues[i]);
            var named = new CustomAttributeNamedArgument[namedNames.Length];
            for (int i = 0; i < named.Length; i++)
                named[i] = (CustomAttributeNamedArgument)(object)new CustomAttributeNamedArgumentImpl(namedNames[i].Substring(2), namedNames[i][0] == 'F', namedTypes[i], Typed(namedTypes[i], namedValues[i]));
            return new RuntimeCustomAttributeDataImpl(attributeType, parameterTypes,
                new ReadOnlyCollection<CustomAttributeTypedArgument>(arguments), new ReadOnlyCollection<CustomAttributeNamedArgument>(named));
        }

        private static CustomAttributeTypedArgument Typed(Type type, object value)
        {
            if (value is object[] elements && type.IsArray)
            {
                Type elementType = type.GetElementType();
                var items = new CustomAttributeTypedArgument[elements.Length];
                for (int i = 0; i < items.Length; i++) items[i] = Typed(elementType, elements[i]);
                value = new ReadOnlyCollection<CustomAttributeTypedArgument>(items);
            }
            return new CustomAttributeTypedArgument(type, value);
        }

        public override Type AttributeType => _attributeType;
        public override ConstructorInfo Constructor =>
            throw new NotSupportedException("CustomAttributeData.Constructor of '" + _attributeType.FullName + "' is not supported by csharpc yet.");
        public override IList<CustomAttributeTypedArgument> ConstructorArguments => _arguments;
        public override IList<CustomAttributeNamedArgument> NamedArguments => _named;
    }

    /// <summary>Reads the generated records (csrt.h; offsets in pointer-size units, so wasm32's 4-byte pointers work too).</summary>
    internal static unsafe class ReflectionRecords
    {
        internal static readonly int P = sizeof(nint);
        // csrt_MethodRecord
        internal static int MethodSize => 7 * P + 16;
        internal static nint MethodFunction(byte* m) => *(nint*)(m + 6 * P + 16);
        internal static byte* MethodName(byte* m) => (byte*)*(nint*)m;
        internal static nint MethodDeclaring(byte* m) => *(nint*)(m + P);
        internal static nint MethodReturnType(byte* m) => *(nint*)(m + 2 * P);
        internal static nint MethodInvoke(byte* m) => *(nint*)(m + 3 * P);
        internal static byte* MethodParameters(byte* m) => (byte*)*(nint*)(m + 4 * P);
        internal static byte* MethodAttributes(byte* m) => (byte*)*(nint*)(m + 5 * P);
        internal static int MethodParameterCount(byte* m) => *(int*)(m + 6 * P);
        internal static int MethodAttributeCount(byte* m) => *(int*)(m + 6 * P + 4);
        internal static int MethodFlags(byte* m) => *(int*)(m + 6 * P + 8);
        internal static int MethodExtraFlags(byte* m) => *(int*)(m + 6 * P + 12);
        // csrt_ParameterRecord
        internal static int ParameterSize => 4 * P + 8;
        internal static byte* ParameterName(byte* p) => (byte*)*(nint*)p;
        internal static nint ParameterType(byte* p) => *(nint*)(p + P);
        internal static nint ParameterDefault(byte* p) => *(nint*)(p + 2 * P);
        internal static byte* ParameterAttributes(byte* p) => (byte*)*(nint*)(p + 3 * P);
        internal static int ParameterAttributeCount(byte* p) => *(int*)(p + 4 * P);
        internal static int ParameterFlags(byte* p) => *(int*)(p + 4 * P + 4);
        // csrt_AttributeRecord
        internal static int AttributeSize => 3 * P;
        internal static nint AttributeType(byte* a) => *(nint*)a;
        internal static nint AttributeCreate(byte* a) => *(nint*)(a + P);
        internal static nint AttributeData(byte* a) => *(nint*)(a + 2 * P);
        // csrt_ReflectionInfo
        internal static byte* InfoConstructors(byte* i) => (byte*)*(nint*)(i + P);
        internal static byte* InfoMethods(byte* i) => (byte*)*(nint*)(i + 2 * P);
        internal static byte* InfoAttributes(byte* i) => (byte*)*(nint*)(i + 3 * P);
        internal static byte* InfoProperties(byte* i) => (byte*)*(nint*)(i + 4 * P);
        internal static int InfoConstructorCount(byte* i) => *(int*)(i + 5 * P);
        internal static int InfoMethodCount(byte* i) => *(int*)(i + 5 * P + 4);
        internal static int InfoAttributeCount(byte* i) => *(int*)(i + 5 * P + 8);
        internal static int InfoDescribed(byte* i) => *(int*)(i + 5 * P + 12);
        internal static int InfoPropertyCount(byte* i) => *(int*)(i + 5 * P + 16);
        // csrt_PropertyRecord
        internal static int PropertySize => 5 * P + 8;
        internal static byte* PropertyName(byte* p) => (byte*)*(nint*)p;
        internal static nint PropertyType(byte* p) => *(nint*)(p + P);
        internal static byte* PropertyGetter(byte* p) => (byte*)*(nint*)(p + 2 * P);
        internal static byte* PropertySetter(byte* p) => (byte*)*(nint*)(p + 3 * P);
        internal static byte* PropertyAttributes(byte* p) => (byte*)*(nint*)(p + 4 * P);
        internal static int PropertyAttributeCount(byte* p) => *(int*)(p + 5 * P);
        internal static int PropertyFlags(byte* p) => *(int*)(p + 5 * P + 4);
    }

    [Shadows("System.Reflection.RuntimeConstructorInfo")]
    internal sealed unsafe class RuntimeConstructorInfoImpl : ConstructorInfo
    {
        internal readonly byte* _record;
        private ParameterInfo[] _parameters;

        internal RuntimeConstructorInfoImpl(byte* record) { _record = record; }

        public override RuntimeMethodHandle MethodHandle => throw new NotSupportedException("MethodBase.MethodHandle is not supported by csharpc yet.");

        public override string Name => ".ctor";
        public override Type DeclaringType => ReflectionRuntime.TypeOf(ReflectionRecords.MethodDeclaring(_record));
        public override Type ReflectedType => DeclaringType;
        public override MethodAttributes Attributes => (MethodAttributes)ReflectionRecords.MethodFlags(_record);
        public override MethodImplAttributes GetMethodImplementationFlags() => MethodImplAttributes.IL;
        public override CallingConventions CallingConvention => IsStatic ? CallingConventions.Standard : CallingConventions.Standard | CallingConventions.HasThis;
        public override ParameterInfo[] GetParameters() => (ParameterInfo[])ReflectionRuntime.Parameters(_record, this, ref _parameters).Clone();
        internal ParameterInfo[] ParametersNoCopy => ReflectionRuntime.Parameters(_record, this, ref _parameters);
        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture) =>
            throw new NotSupportedException("Invoking a constructor on an existing object is not supported by csharpc yet.");

        public override object Invoke(BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            Type declaring = DeclaringType;
            if (declaring.IsAbstract) throw new MemberAccessException("Cannot create an abstract class.");
            return ReflectionRuntime.Invoke(_record, ParametersNoCopy, null, invokeAttr, parameters, this);
        }

        public override bool IsDefined(Type attributeType, bool inherit) => ReflectionRuntime.IsDefined(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record), attributeType);
        public override object[] GetCustomAttributes(bool inherit) => ReflectionRuntime.CreateAttributes(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record), typeof(object));
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => ReflectionRuntime.CreateAttributes(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record), attributeType);
        public override IList<CustomAttributeData> GetCustomAttributesData() => ReflectionRuntime.AttributeData(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record));

        public override string ToString() => "Void " + ReflectionRuntime.NameAndSignature(".ctor", ParametersNoCopy);
    }

    [Shadows("System.Reflection.RuntimeMethodInfo")]
    internal sealed unsafe class RuntimeMethodInfoImpl : MethodInfo
    {
        internal readonly byte* _record;
        private ParameterInfo[] _parameters;
        private ParameterInfo _returnParameter;
        private string _name;

        internal RuntimeMethodInfoImpl(byte* record) { _record = record; }

        public override Delegate CreateDelegate(Type delegateType) => CreateDelegate(delegateType, null);

        /// <summary>
        /// A delegate over this instance method closed over <paramref name="target"/>: the method's own function,
        /// checked against the delegate type's Invoke signature (kept for delegate types the program names).
        /// </summary>
        public override Delegate CreateDelegate(Type delegateType, object target)
        {
            ArgumentNullException.ThrowIfNull(delegateType);
            if (!typeof(Delegate).IsAssignableFrom(delegateType) || delegateType == typeof(Delegate) || delegateType == typeof(MulticastDelegate))
                throw new ArgumentException("Type must derive from Delegate.", "delegateType");
            byte* signature = ReflectionRuntime.DelegateSignature(delegateType);
            nint fn = ReflectionRecords.MethodFunction(_record);
            if (signature == null || fn == 0 || target == null)
                throw new NotSupportedException("MethodInfo.CreateDelegate of '" + this + "' to '" + delegateType + "'" + (target == null ? " without a target" : "") + " is not supported by csharpc yet.");
            if (!DeclaringType.IsInstanceOfType(target) || !Compatible(signature))
                throw new ArgumentException("Cannot bind to the target method because its signature is not compatible with that of the delegate type.");
            if (IsVirtual && !IsFinal && target.GetType() != DeclaringType)
                throw new NotSupportedException("MethodInfo.CreateDelegate of the virtual method '" + this + "' on a derived type is not supported by csharpc yet.");
            return CoreIntrinsics.ReinterpretAs<Delegate>(CoreIntrinsics.PointerToObject((nint)CoreIntrinsics.csrt_delegate_create(CoreIntrinsics.csrt_type_handle(delegateType), CoreIntrinsics.ObjectToPointer(target), fn)));
        }

        // The delegate's parameters must be passable to the method's (the same type, or a reference type the
        // method's accepts) and the method's return must be the delegate's (or a reference type assignable to it).
        private bool Compatible(byte* signature)
        {
            int count = *(int*)(signature + 3 * ReflectionRecords.P);
            if (count != ReflectionRecords.MethodParameterCount(_record)) return false;
            nint* delegateParameters = (nint*)*(nint*)(signature + 2 * ReflectionRecords.P);
            byte* parameters = ReflectionRecords.MethodParameters(_record);
            for (int i = 0; i < count; i++)
            {
                byte* p = parameters + i * ReflectionRecords.ParameterSize;
                if ((ReflectionRecords.ParameterFlags(p) & (1 << 17)) != 0) return false; // by-ref
                if (!Passable(delegateParameters[i], ReflectionRecords.ParameterType(p))) return false;
            }
            nint delegateReturn = *(nint*)(signature + ReflectionRecords.P);
            nint methodReturn = ReflectionRecords.MethodReturnType(_record);
            if (delegateReturn == 0 || methodReturn == 0) return delegateReturn == methodReturn;
            return Passable(methodReturn, delegateReturn);
        }

        private static bool Passable(nint from, nint to)
        {
            if (from == to) return true;
            Type source = ReflectionRuntime.TypeOf(from), destination = ReflectionRuntime.TypeOf(to);
            return !source.IsValueType && !destination.IsValueType && destination.IsAssignableFrom(source);
        }

        public override RuntimeMethodHandle MethodHandle => throw new NotSupportedException("MethodBase.MethodHandle is not supported by csharpc yet.");

        private bool IsSourceLambda => (ReflectionRecords.MethodExtraFlags(_record) & 1) != 0;

        public override string Name
        {
            get
            {
                if (IsSourceLambda) throw new NotSupportedException("The name of a lambda or local function compiled from source is not supported by csharpc yet.");
                return _name ??= Utf8.FromNative(ReflectionRecords.MethodName(_record));
            }
        }

        public override Type DeclaringType => ReflectionRuntime.TypeOf(ReflectionRecords.MethodDeclaring(_record));
        public override Type ReflectedType => DeclaringType;
        public override MethodAttributes Attributes => (MethodAttributes)ReflectionRecords.MethodFlags(_record);
        public override MethodImplAttributes GetMethodImplementationFlags() => MethodImplAttributes.IL;
        public override CallingConventions CallingConvention => IsStatic ? CallingConventions.Standard : CallingConventions.Standard | CallingConventions.HasThis;
        public override Type ReturnType
        {
            get
            {
                nint mt = ReflectionRecords.MethodReturnType(_record);
                return mt == 0 ? typeof(void) : ReflectionRuntime.TypeOf(mt);
            }
        }

        public override ParameterInfo ReturnParameter =>
            _returnParameter ??= new RuntimeParameterInfoImpl(null, this, -1, ReturnType);
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => ReturnParameter;
        public override ParameterInfo[] GetParameters() => (ParameterInfo[])ReflectionRuntime.Parameters(_record, this, ref _parameters).Clone();
        internal ParameterInfo[] ParametersNoCopy => ReflectionRuntime.Parameters(_record, this, ref _parameters);
        public override bool IsGenericMethod => false;
        public override bool IsGenericMethodDefinition => false;
        public override Type[] GetGenericArguments() => new Type[0];

        public override MethodInfo GetBaseDefinition()
        {
            // Only methods that start a vtable slot (or aren't virtual) are their own base definition.
            if (!IsVirtual || (Attributes & MethodAttributes.NewSlot) != 0) return this;
            throw new NotSupportedException("MethodInfo.GetBaseDefinition of an override is not supported by csharpc yet.");
        }

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            if (!IsStatic)
            {
                if (obj == null) throw new TargetException("Non-static method requires a target.");
                if (!DeclaringType.IsInstanceOfType(obj)) throw new TargetException("Object does not match target type.");
            }
            return ReflectionRuntime.Invoke(_record, ParametersNoCopy, IsStatic ? null : obj, invokeAttr, parameters, this);
        }

        public override bool IsDefined(Type attributeType, bool inherit) => ReflectionRuntime.IsDefined(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record), attributeType);
        public override object[] GetCustomAttributes(bool inherit) => GetCustomAttributes(typeof(object), inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (inherit && IsVirtual && (Attributes & MethodAttributes.NewSlot) == 0)
                throw new NotSupportedException("GetCustomAttributes(inherit: true) of an override is not supported by csharpc yet.");
            return ReflectionRuntime.CreateAttributes(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record), attributeType);
        }
        public override IList<CustomAttributeData> GetCustomAttributesData() => ReflectionRuntime.AttributeData(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record));

        public override string ToString() => ReflectionRuntime.FormatTypeName(ReturnType) + " " + ReflectionRuntime.NameAndSignature(Name, ParametersNoCopy);
    }

    [Shadows("System.Reflection.RuntimeParameterInfo")]
    internal sealed unsafe class RuntimeParameterInfoImpl : ParameterInfo
    {
        private readonly byte* _record; // null: a return parameter

        internal RuntimeParameterInfoImpl(byte* record, MemberInfo member, int position, Type type)
        {
            _record = record;
            MemberImpl = member;
            PositionImpl = position;
            ClassImpl = type;
            if (record != null)
            {
                byte* name = ReflectionRecords.ParameterName(record);
                NameImpl = name == null ? null : Utf8.FromNative(name);
                AttrsImpl = (ParameterAttributes)(ReflectionRecords.ParameterFlags(record) & 0xFFFF);
            }
            else AttrsImpl = ParameterAttributes.None;
        }

        private bool HasConstant => _record != null && (ReflectionRecords.ParameterFlags(_record) & (1 << 16)) != 0;

        public override bool HasDefaultValue => HasConstant;

        public override object DefaultValue
        {
            get
            {
                if (_record == null) return null;
                if (HasConstant) return ReflectionRuntime.CallFactory(ReflectionRecords.ParameterDefault(_record));
                return IsOptional ? Missing.Value : DBNull.Value;
            }
        }

        public override object RawDefaultValue
        {
            get
            {
                if (HasConstant && ParameterType.IsEnum)
                    throw new NotSupportedException("ParameterInfo.RawDefaultValue of an enum parameter is not supported by csharpc yet.");
                return DefaultValue;
            }
        }

        public override bool IsDefined(Type attributeType, bool inherit) =>
            _record == null ? base.IsDefined(attributeType, inherit) : ReflectionRuntime.IsDefined(ReflectionRecords.ParameterAttributes(_record), ReflectionRecords.ParameterAttributeCount(_record), attributeType);
        public override object[] GetCustomAttributes(bool inherit) => GetCustomAttributes(typeof(object), inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (_record == null) throw new NotSupportedException("The custom attributes of a return parameter are not supported by csharpc yet.");
            return ReflectionRuntime.CreateAttributes(ReflectionRecords.ParameterAttributes(_record), ReflectionRecords.ParameterAttributeCount(_record), attributeType);
        }
        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            if (_record == null) throw new NotSupportedException("The custom attributes of a return parameter are not supported by csharpc yet.");
            return ReflectionRuntime.AttributeData(ReflectionRecords.ParameterAttributes(_record), ReflectionRecords.ParameterAttributeCount(_record));
        }
    }

    /// <summary>The reflection entry points (BclValueTypes.Reflection.cs maps Type/Array/Delegate members here).</summary>
    internal static unsafe class ReflectionRuntime
    {

        private static readonly object s_lock = new object();
        private static Dictionary<nint, MemberInfo> s_members;

        internal static Type TypeOf(nint mt) => CoreIntrinsics.csrt_type_from_mt((void*)mt);

        private static Type[] s_emptyTypes;
        /// <summary>Type.EmptyTypes (a static readonly field in .NET, read through BclValueTypes' field table).</summary>
        public static Type[] EmptyTypes() => s_emptyTypes ??= new Type[0];

        internal static byte* Info(Type type, string what) => InfoOf(type, what);
        internal static void Require(byte* info, Type type, BindingFlags flags, int publicBit, int nonPublicBit, string what) =>
            RequireDescribed(info, type, flags, publicBit, nonPublicBit, what);

        private static byte* InfoOf(Type type, string what)
        {
            ArgumentNullException.ThrowIfNull(type);
            return CoreIntrinsics.csrt_reflection_info(type, CoreIntrinsics.ReflectionTableAddress());
        }

        private static NotSupportedException NotKept(Type type, string what) => new NotSupportedException(
            "The " + what + " of '" + type + "' are not available: csharpc keeps reflection metadata only for types the program passes to [DynamicallyAccessedMembers] locations (as NativeAOT does). Not supported by csharpc yet.");

        /// <summary>One MemberInfo per record, as RuntimeType caches them (== holds between calls).</summary>
        internal static MemberInfo Member(byte* record, bool constructor)
        {
            lock (s_lock)
            {
                s_members ??= new Dictionary<nint, MemberInfo>();
                if (!s_members.TryGetValue((nint)record, out var member))
                {
                    member = constructor ? new RuntimeConstructorInfoImpl(record) : new RuntimeMethodInfoImpl(record);
                    s_members[(nint)record] = member;
                }
                return member;
            }
        }

        private static bool Matches(MethodAttributes attributes, BindingFlags flags)
        {
            bool isPublic = (attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
            bool isStatic = (attributes & MethodAttributes.Static) != 0;
            if (!((isPublic && (flags & BindingFlags.Public) != 0) || (!isPublic && (flags & BindingFlags.NonPublic) != 0))) return false;
            return isStatic ? (flags & BindingFlags.Static) != 0 : (flags & BindingFlags.Instance) != 0;
        }

        private static void RequireDescribed(byte* info, Type type, BindingFlags flags, int publicBit, int nonPublicBit, string what)
        {
            if (info == null) throw NotKept(type, what);
            int described = ReflectionRecords.InfoDescribed(info);
            if ((flags & BindingFlags.Public) != 0 && (described & publicBit) == 0) throw NotKept(type, "public " + what);
            if ((flags & BindingFlags.NonPublic) != 0 && (described & nonPublicBit) == 0) throw NotKept(type, "non-public " + what);
        }

        /// <summary>csharpc has no Type objects for by-ref types (an explicit error, never a wrong type).</summary>
        public static Type MakeByRefType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new NotSupportedException("Type.MakeByRefType() ('" + type + "&') is not supported by csharpc yet.");
        }

        public static ConstructorInfo[] GetConstructorsAll(Type type) => GetConstructors(type, BindingFlags.Public | BindingFlags.Instance);
        public static ConstructorInfo[] GetConstructorsFlags(Type type, BindingFlags flags) => GetConstructors(type, flags);

        internal static ConstructorInfo[] GetConstructors(Type type, BindingFlags flags)
        {
            byte* info = InfoOf(type, "constructors");
            // Type initializers (.cctor) are never described; Static alone finds none.
            if ((flags & BindingFlags.Instance) == 0) return new ConstructorInfo[0];
            RequireDescribed(info, type, flags, 1, 2, "constructors");
            var list = new List<ConstructorInfo>();
            byte* records = ReflectionRecords.InfoConstructors(info);
            int count = ReflectionRecords.InfoConstructorCount(info);
            for (int i = 0; i < count; i++)
            {
                byte* record = records + i * ReflectionRecords.MethodSize;
                if (Matches((MethodAttributes)ReflectionRecords.MethodFlags(record), flags)) list.Add((ConstructorInfo)Member(record, true));
            }
            return list.ToArray();
        }

        public static ConstructorInfo GetConstructorTypes(Type type, Type[] types) =>
            GetConstructor(type, BindingFlags.Public | BindingFlags.Instance, null, types, null);
        public static ConstructorInfo GetConstructorFlagsTypes(Type type, BindingFlags flags, Type[] types) => GetConstructor(type, flags, null, types, null);

        public static ConstructorInfo GetConstructor(Type type, BindingFlags flags, Binder binder, Type[] types, ParameterModifier[] modifiers)
        {
            ArgumentNullException.ThrowIfNull(types);
            for (int i = 0; i < types.Length; i++) ArgumentNullException.ThrowIfNull(types[i], "types");
            if (binder != null) throw new NotSupportedException("A custom Binder in GetConstructor is not supported by csharpc yet.");
            foreach (var c in GetConstructors(type, flags))
                if (SameParameterTypes(((RuntimeConstructorInfoImpl)(object)c).ParametersNoCopy, types)) return c;
            return null;
        }

        private static bool SameParameterTypes(ParameterInfo[] parameters, Type[] types)
        {
            if (parameters.Length != types.Length) return false;
            for (int i = 0; i < types.Length; i++) if (parameters[i].ParameterType != types[i]) return false;
            return true;
        }

        public static MethodInfo[] GetMethodsAll(Type type) => GetMethods(type, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        public static MethodInfo[] GetMethodsFlags(Type type, BindingFlags flags) => GetMethods(type, flags);

        internal static MethodInfo[] GetMethods(Type type, BindingFlags flags)
        {
            byte* info = InfoOf(type, "methods");
            RequireDescribed(info, type, flags, 4, 8, "methods");
            var list = new List<MethodInfo>();
            byte* records = ReflectionRecords.InfoMethods(info);
            int count = ReflectionRecords.InfoMethodCount(info);
            bool declaredOnly = (flags & BindingFlags.DeclaredOnly) != 0;
            for (int i = 0; i < count; i++)
            {
                byte* record = records + i * ReflectionRecords.MethodSize;
                var attributes = (MethodAttributes)ReflectionRecords.MethodFlags(record);
                if (!Matches(attributes, flags)) continue;
                bool inherited = ReflectionRecords.MethodDeclaring(record) != CoreIntrinsics.csrt_type_handle(type);
                if (inherited && (declaredOnly || ((attributes & MethodAttributes.Static) != 0 && (flags & BindingFlags.FlattenHierarchy) == 0))) continue;
                list.Add((MethodInfo)Member(record, false));
            }
            return list.ToArray();
        }

        public static MethodInfo GetMethodName(Type type, string name) => GetMethod(type, name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        public static MethodInfo GetMethodNameFlags(Type type, string name, BindingFlags flags) => GetMethod(type, name, flags);

        internal static MethodInfo GetMethod(Type type, string name, BindingFlags flags)
        {
            ArgumentNullException.ThrowIfNull(name);
            MethodInfo found = null;
            foreach (var m in GetMethods(type, flags))
            {
                if (!string.Equals(m.Name, name, (flags & BindingFlags.IgnoreCase) != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                if (found != null) throw new AmbiguousMatchException("Ambiguous match found for '" + type + " " + m + "'.");
                found = m;
            }
            return found;
        }

        public static MethodInfo GetMethodNameTypes(Type type, string name, Type[] types) => GetMethodNameFlagsTypes(type, name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, types);

        public static MethodInfo GetMethodNameFlagsTypes(Type type, string name, BindingFlags flags, Type[] types)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(types);
            foreach (var m in GetMethods(type, flags))
                if (m.Name == name && SameParameterTypes(((RuntimeMethodInfoImpl)(object)m).ParametersNoCopy, types)) return m;
            return null;
        }

        internal static ParameterInfo[] Parameters(byte* record, MemberInfo member, ref ParameterInfo[] cache)
        {
            var parameters = Volatile.Read(ref cache);
            if (parameters != null) return parameters;
            int count = ReflectionRecords.MethodParameterCount(record);
            byte* records = ReflectionRecords.MethodParameters(record);
            parameters = new ParameterInfo[count];
            for (int i = 0; i < count; i++)
            {
                byte* p = records + i * ReflectionRecords.ParameterSize;
                parameters[i] = new RuntimeParameterInfoImpl(p, member, i, TypeOf(ReflectionRecords.ParameterType(p)));
            }
            Interlocked.CompareExchange(ref cache, parameters, null);
            return Volatile.Read(ref cache);
        }

        /// <summary>MethodBase.Invoke: .NET's argument checks, then the generated stub; exceptions wrapped unless DoNotWrapExceptions.</summary>
        internal static object Invoke(byte* record, ParameterInfo[] parameters, object target, BindingFlags invokeAttr, object[] arguments, MethodBase method)
        {
            nint invoker = ReflectionRecords.MethodInvoke(record);
            if (invoker == 0) throw new NotSupportedException("Invoking '" + method + "' through reflection is not supported by csharpc yet.");
            int count = arguments == null ? 0 : arguments.Length;
            if (count != parameters.Length) throw new TargetParameterCountException("Parameter count mismatch.");
            object[] checkedArguments = parameters.Length == 0 ? new object[0] : new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                object value = arguments[i];
                Type type = parameters[i].ParameterType;
                if (value == Type.Missing)
                {
                    if (!parameters[i].HasDefaultValue) throw new ArgumentException("Missing parameter does not have a default value. (Parameter 'parameters')");
                    value = parameters[i].DefaultValue;
                }
                if (value != null && !type.IsInstanceOfType(value) && !IsNullableOf(type, value))
                    throw new ArgumentException("Object of type '" + value.GetType() + "' cannot be converted to type '" + type + "'.");
                checkedArguments[i] = value;
            }
            var call = (delegate*<object, object[], object>)invoker;
            if ((invokeAttr & BindingFlags.DoNotWrapExceptions) != 0) return call(target, checkedArguments);
            try { return call(target, checkedArguments); }
            catch (Exception e) { throw new TargetInvocationException(e); }
        }

        private static bool IsNullableOf(Type nullable, object value) =>
            nullable.IsGenericType && !nullable.IsGenericTypeDefinition && nullable.GetGenericTypeDefinition() == typeof(Nullable<>)
            && nullable.GetGenericArguments()[0] == value.GetType();

        internal static object CallFactory(nint factory) => factory == 0 ? null : ((delegate*<object>)factory)();

        internal static object[] CreateAttributes(byte* records, int count, Type attributeType)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            bool all = attributeType == typeof(object);
            if (!all && attributeType != typeof(Attribute) && !attributeType.IsSubclassOf(typeof(Attribute)) && !attributeType.IsInterface)
                throw new ArgumentException("Type passed in must be derived from System.Attribute or System.Attribute itself.");
            var list = new List<object>();
            for (int i = 0; i < count; i++)
            {
                byte* a = records + i * ReflectionRecords.AttributeSize;
                if (!all && !attributeType.IsAssignableFrom(TypeOf(ReflectionRecords.AttributeType(a)))) continue;
                list.Add(CallFactory(ReflectionRecords.AttributeCreate(a)));
            }
            // CoreCLR returns an array of the requested type (object[] when unfiltered).
            // (A reinterpretation rather than a cast: csharpc's castclass has no array covariance.)
            object[] result = all ? new object[list.Count] : CoreIntrinsics.ReinterpretAs<object[]>(Array.CreateInstance(attributeType, list.Count));
            for (int i = 0; i < list.Count; i++) result[i] = list[i];
            return result;
        }

        internal static bool IsDefined(byte* records, int count, Type attributeType)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            for (int i = 0; i < count; i++)
                if (attributeType.IsAssignableFrom(TypeOf(ReflectionRecords.AttributeType(records + i * ReflectionRecords.AttributeSize)))) return true;
            return false;
        }

        internal static IList<CustomAttributeData> AttributeData(byte* records, int count)
        {
            var list = new CustomAttributeData[count];
            for (int i = 0; i < count; i++) list[i] = (CustomAttributeData)CallFactory(ReflectionRecords.AttributeData(records + i * ReflectionRecords.AttributeSize));
            return new ReadOnlyCollection<CustomAttributeData>(list);
        }

        /// <summary>RuntimeType.FormatTypeName: primitives and nested types by Name, the rest by ToString().</summary>
        internal static string FormatTypeName(Type type)
        {
            Type element = type;
            while (element.HasElementType) element = element.GetElementType();
            if (element.IsPrimitive || element.IsNested || element == typeof(void)) return type.Name;
            return type.ToString();
        }

        internal static string NameAndSignature(string name, ParameterInfo[] parameters)
        {
            var sb = new StringBuilder(name).Append('(');
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatTypeName(parameters[i].ParameterType));
            }
            return sb.Append(')').ToString();
        }

        // ---- Type members over the reflection table ---------------------------------------------

        internal static object[] GetTypeCustomAttributes(Type type, Type attributeType)
        {
            byte* info = InfoOf(type, "custom attributes");
            if (info == null || (ReflectionRecords.InfoDescribed(info) & 16) == 0) throw NotKept(type, "custom attributes");
            return CreateAttributes(ReflectionRecords.InfoAttributes(info), ReflectionRecords.InfoAttributeCount(info), attributeType);
        }

        public static object[] TypeAttributesAll(Type type, bool inherit) => GetTypeCustomAttributes(type, typeof(object));
        public static object[] TypeAttributesOf(Type type, Type attributeType, bool inherit) => GetTypeCustomAttributes(type, attributeType);

        public static bool IsTypeDefined(Type type, Type attributeType, bool inherit)
        {
            byte* info = InfoOf(type, "custom attributes");
            if (info == null || (ReflectionRecords.InfoDescribed(info) & 16) == 0) throw NotKept(type, "custom attributes");
            return IsDefined(ReflectionRecords.InfoAttributes(info), ReflectionRecords.InfoAttributeCount(info), attributeType);
        }

        public static IList<CustomAttributeData> GetTypeCustomAttributesData(Type type)
        {
            byte* info = InfoOf(type, "custom attributes");
            if (info == null || (ReflectionRecords.InfoDescribed(info) & 16) == 0) throw NotKept(type, "custom attributes");
            return AttributeData(ReflectionRecords.InfoAttributes(info), ReflectionRecords.InfoAttributeCount(info));
        }

        // ---- Arrays ------------------------------------------------------------------------------

        public static Array CreateInstance(Type elementType, int length)
        {
            ArgumentNullException.ThrowIfNull(elementType);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            return CoreIntrinsics.csrt_array_create_instance(elementType, length, CoreIntrinsics.TypeInfoTableAddress());
        }

        public static void SetValue(Array array, object value, int index) =>
            CoreIntrinsics.csrt_array_set_value(array, value, index, CoreIntrinsics.TypeInfoTableAddress());

        // ---- Delegates ---------------------------------------------------------------------------

        /// <summary>The csrt_DelegateSignature of a delegate type, or null when the program never names it.</summary>
        internal static byte* DelegateSignature(Type delegateType)
        {
            byte* table = (byte*)CoreIntrinsics.DelegateSignatureTableAddress();
            long count = *(long*)table;
            byte* entries = (byte*)*(nint*)(table + 8);
            nint mt = CoreIntrinsics.csrt_type_handle(delegateType);
            int size = 3 * ReflectionRecords.P + 8;
            for (long i = 0; i < count; i++)
                if (*(nint*)(entries + i * size) == mt) return entries + i * size;
            return null;
        }

        public static MethodInfo DelegateMethod(Delegate d)
        {
            ArgumentNullException.ThrowIfNull(d);
            nint fn = CoreIntrinsics.DelegateFunction(d);
            byte* record = CoreIntrinsics.csrt_reflection_delegate_method(fn, CoreIntrinsics.DelegateMethodTableAddress());
            if (record == null)
                throw new NotSupportedException("Delegate.Method of a delegate to a virtual method (or one csharpc doesn't describe) is not supported by csharpc yet.");
            return (MethodInfo)Member(record, false);
        }

        /// <summary>Delegate.Target: csharpc's own closures (lambdas compiled from source) are not .NET's display-class objects.</summary>
        public static object DelegateTarget(Delegate d)
        {
            ArgumentNullException.ThrowIfNull(d);
            nint fn = CoreIntrinsics.DelegateFunction(d);
            byte* record = CoreIntrinsics.csrt_reflection_delegate_method(fn, CoreIntrinsics.DelegateMethodTableAddress());
            if (record != null && (ReflectionRecords.MethodExtraFlags(record) & 1) != 0)
                throw new NotSupportedException("Delegate.Target of a lambda or local function compiled from source is not supported by csharpc yet.");
            return CoreIntrinsics.DelegateTargetObject(d);
        }
    }
}
