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
        /// <summary>The member's assembly's one module (a Type's Module is lowered in BclValueTypes.Reflection.cs).</summary>
        public virtual Module Module => DeclaringType.Module;
        /// <summary>Tokens of a program's members aren't kept: .NET answers with the metadata token.</summary>
        public virtual int MetadataToken => throw new NotSupportedException("MemberInfo.MetadataToken is not supported by csharpc yet.");
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
        public T CreateDelegate<T>() where T : Delegate => (T)CreateDelegate(typeof(T));
        public T CreateDelegate<T>(object target) where T : Delegate => (T)CreateDelegate(typeof(T), target);
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
            // A Type (Microsoft.Extensions.Logging's ProviderAliasUtilities asks for a provider type's): Type objects
            // are runtime objects, whose members are called through Type, not the MemberInfo slots.
            if (target is Type type)
            {
                CoreIntrinsics.TypeAttributesQueried();
                return ReflectionRuntime.GetTypeCustomAttributesData(type);
            }
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
        internal static int MethodSize => 10 * P + 24;
        internal static nint MethodIdentity(byte* m) => *(nint*)(m + 7 * P + 16);
        internal static nint MethodFunction(byte* m) => *(nint*)(m + 6 * P + 16);
        internal static nint MethodOpenFunction(byte* m) => *(nint*)(m + 8 * P + 16);
        internal static byte* MethodName(byte* m) => (byte*)*(nint*)m;
        internal static nint MethodDeclaring(byte* m) => *(nint*)(m + P);
        internal static nint MethodReturnType(byte* m) => *(nint*)(m + 2 * P);
        internal static nint MethodInvoke(byte* m) => *(nint*)(m + 3 * P);
        internal static byte* MethodParameters(byte* m) => (byte*)*(nint*)(m + 4 * P);
        internal static byte* MethodAttributes(byte* m) => (byte*)*(nint*)(m + 5 * P);
        internal static byte* MethodReturnAttributes(byte* m) => (byte*)*(nint*)(m + 9 * P + 16);
        internal static int MethodReturnAttributeCount(byte* m) => *(int*)(m + 10 * P + 16);
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
        internal static int InfoFieldCount(byte* i) => *(int*)(i + 5 * P + 20);
        internal static byte* InfoFields(byte* i) => (byte*)*(nint*)(i + 5 * P + 24);
        internal static byte* InfoEvents(byte* i) => (byte*)*(nint*)(i + 6 * P + 24);
        internal static int InfoEventCount(byte* i) => *(int*)(i + 7 * P + 24);
        internal static byte* InfoInheritedAttributes(byte* i) => (byte*)*(nint*)(i + 8 * P + 24);
        internal static int InfoInheritedAttributeCount(byte* i) => *(int*)(i + 9 * P + 24);
        internal static bool InfoInheritedKnown(byte* i) => *(int*)(i + 9 * P + 28) != 0;
        // csrt_EventRecord
        internal static int EventSize => 7 * P + 16;
        internal static byte* EventName(byte* e) => (byte*)*(nint*)e;
        internal static nint EventDeclaring(byte* e) => *(nint*)(e + P);
        internal static byte* EventAdd(byte* e) => (byte*)*(nint*)(e + 3 * P);
        internal static byte* EventRemove(byte* e) => (byte*)*(nint*)(e + 4 * P);
        internal static byte* EventRaise(byte* e) => (byte*)*(nint*)(e + 5 * P);
        internal static byte* EventAttributeRecords(byte* e) => (byte*)*(nint*)(e + 6 * P);
        internal static int EventAttributeCount(byte* e) => *(int*)(e + 7 * P);
        internal static int EventFlags(byte* e) => *(int*)(e + 7 * P + 4);
        internal static int EventAttributes(byte* e) => *(int*)(e + 7 * P + 8);
        // csrt_FieldRecord
        internal static int FieldSize => 6 * P + 8;
        internal static byte* FieldName(byte* f) => (byte*)*(nint*)f;
        internal static nint FieldDeclaring(byte* f) => *(nint*)(f + P);
        internal static nint FieldType(byte* f) => *(nint*)(f + 2 * P);
        internal static nint FieldGet(byte* f) => *(nint*)(f + 3 * P);
        internal static nint FieldSet(byte* f) => *(nint*)(f + 4 * P);
        internal static byte* FieldAttributes(byte* f) => (byte*)*(nint*)(f + 5 * P);
        internal static int FieldAttributeCount(byte* f) => *(int*)(f + 6 * P);
        internal static int FieldFlags(byte* f) => *(int*)(f + 6 * P + 4);
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

        public override string Name => IsStatic ? ".cctor" : ".ctor";
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
            if (IsStatic) throw new MemberAccessException("Type initializer was not callable.");
            if (declaring.IsAbstract) throw new MemberAccessException("Cannot create an abstract class.");
            return ReflectionRuntime.Invoke(_record, ParametersNoCopy, null, invokeAttr, parameters, this);
        }

        public override bool IsDefined(Type attributeType, bool inherit) => ReflectionRuntime.IsDefined(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record), attributeType);
        public override object[] GetCustomAttributes(bool inherit) => ReflectionRuntime.CreateAttributes(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record), typeof(object));
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => ReflectionRuntime.CreateAttributes(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record), attributeType);
        public override IList<CustomAttributeData> GetCustomAttributesData() => ReflectionRuntime.AttributeData(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record));

        public override string ToString() => "Void " + ReflectionRuntime.NameAndSignature(Name, ParametersNoCopy);
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
            CheckNotGenericDefinition("MethodInfo.CreateDelegate");
            if (!typeof(Delegate).IsAssignableFrom(delegateType) || delegateType == typeof(Delegate) || delegateType == typeof(MulticastDelegate))
                throw new ArgumentException("Type must derive from Delegate.", "delegateType");
            byte* signature = ReflectionRuntime.DelegateSignature(delegateType);
            nint fn = ReflectionRecords.MethodFunction(_record);
            // A static method: open over no argument (its function is a thunk ignoring the target).
            if (IsStatic && signature != null && fn != 0 && target == null)
            {
                if (!Compatible(signature))
                    throw new ArgumentException("Cannot bind to the target method because its signature is not compatible with that of the delegate type.");
                return CoreIntrinsics.ReinterpretAs<Delegate>(CoreIntrinsics.PointerToObject((nint)CoreIntrinsics.csrt_delegate_create(CoreIntrinsics.csrt_type_handle(delegateType), 0, fn)));
            }
            // An instance method without a target: an open instance delegate, whose first parameter is the receiver.
            if (!IsStatic && target == null && signature != null && ReflectionRecords.MethodOpenFunction(_record) is var open and not 0)
            {
                if (!CompatibleOpen(signature))
                    throw new ArgumentException("Cannot bind to the target method because its signature is not compatible with that of the delegate type.");
                return CoreIntrinsics.ReinterpretAs<Delegate>(CoreIntrinsics.PointerToObject((nint)CoreIntrinsics.csrt_delegate_create(CoreIntrinsics.csrt_type_handle(delegateType), 0, open)));
            }
            if (signature == null || fn == 0 || target == null || IsStatic)
                throw new NotSupportedException("MethodInfo.CreateDelegate of '" + this + "' to '" + delegateType + "'" + (target == null ? " without a target" : "") + " is not supported by csharpc yet.");
            if (!DeclaringType.IsInstanceOfType(target) || !Compatible(signature))
                throw new ArgumentException("Cannot bind to the target method because its signature is not compatible with that of the delegate type.");
            if (IsVirtual && !IsFinal && target.GetType() != DeclaringType)
                throw new NotSupportedException("MethodInfo.CreateDelegate of the virtual method '" + this + "' on a derived type is not supported by csharpc yet.");
            return CoreIntrinsics.ReinterpretAs<Delegate>(CoreIntrinsics.PointerToObject((nint)CoreIntrinsics.csrt_delegate_create(CoreIntrinsics.csrt_type_handle(delegateType), CoreIntrinsics.ObjectToPointer(target), fn)));
        }

        // The delegate's parameters must be passable to the method's (the same type, or a reference type the
        // method's accepts) and the method's return must be the delegate's (or a reference type assignable to it).
        private bool Compatible(byte* signature) => Compatible(signature, 0);

        // An open instance delegate's first parameter is the receiver: any type the declaring type is assignable from.
        private bool CompatibleOpen(byte* signature) => Compatible(signature, 1);

        private bool Compatible(byte* signature, int receivers)
        {
            int count = *(int*)(signature + 3 * ReflectionRecords.P);
            if (count != ReflectionRecords.MethodParameterCount(_record) + receivers) return false;
            nint* delegateParameters = (nint*)*(nint*)(signature + 2 * ReflectionRecords.P);
            if (receivers == 1 && !Passable(delegateParameters[0], ReflectionRecords.MethodDeclaring(_record))) return false;
            byte* parameters = ReflectionRecords.MethodParameters(_record);
            for (int i = 0; i < count - receivers; i++)
            {
                byte* p = parameters + i * ReflectionRecords.ParameterSize;
                if ((ReflectionRecords.ParameterFlags(p) & (1 << 17)) != 0) return false; // by-ref
                if (!Passable(delegateParameters[i + receivers], ReflectionRecords.ParameterType(p))) return false;
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
            // An enum and its underlying type are interchangeable (the CLR compares the types' reduced forms).
            if (source.IsEnum && destination.IsEnum) return false;
            if (source.IsEnum || destination.IsEnum)
                return source.IsValueType && destination.IsValueType
                    && (source.IsEnum ? Enum.GetUnderlyingType(source) : source) == (destination.IsEnum ? Enum.GetUnderlyingType(destination) : destination);
            return !source.IsValueType && !destination.IsValueType && destination.IsAssignableFrom(source);
        }

        public override RuntimeMethodHandle MethodHandle => throw new NotSupportedException("MethodBase.MethodHandle is not supported by csharpc yet.");

        private bool IsSourceLambda => (ReflectionRecords.MethodExtraFlags(_record) & 1) != 0;

        // A generic method definition (CSRT_METHOD_GENERIC_DEFINITION): csharpc has no Type objects for method type
        // parameters, so its signature, invocation and MakeGenericMethod are explicit errors.
        private bool IsGenericDefinition => (ReflectionRecords.MethodExtraFlags(_record) & 4) != 0;

        private void CheckNotGenericDefinition(string what)
        {
            if (IsGenericDefinition)
                throw new NotSupportedException(what + " of the generic method definition '" + Name + "' is not supported by csharpc yet.");
        }

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
                CheckNotGenericDefinition("MethodInfo.ReturnType");
                nint mt = ReflectionRecords.MethodReturnType(_record);
                return mt == 0 ? typeof(void) : ReflectionRuntime.TypeOf(mt);
            }
        }

        public override ParameterInfo ReturnParameter =>
            _returnParameter ??= new RuntimeParameterInfoImpl(null, this, -1, ReturnType, _record);
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => ReturnParameter;
        public override ParameterInfo[] GetParameters()
        {
            CheckNotGenericDefinition("MethodInfo.GetParameters");
            return (ParameterInfo[])ReflectionRuntime.Parameters(_record, this, ref _parameters).Clone();
        }
        internal ParameterInfo[] ParametersNoCopy => ReflectionRuntime.Parameters(_record, this, ref _parameters);
        public override bool IsGenericMethod => IsGenericDefinition;
        public override bool IsGenericMethodDefinition => IsGenericDefinition;
        public override bool ContainsGenericParameters => IsGenericDefinition;
        public override Type[] GetGenericArguments()
        {
            CheckNotGenericDefinition("MethodInfo.GetGenericArguments");
            return new Type[0];
        }

        public override MethodInfo MakeGenericMethod(params Type[] typeArguments)
        {
            if (!IsGenericDefinition) throw new InvalidOperationException(this + " is not a GenericMethodDefinition. MakeGenericMethod may only be called on a method for which MethodBase.IsGenericMethodDefinition is true.");
            throw new NotSupportedException("MethodInfo.MakeGenericMethod of '" + Name + "' is not supported by csharpc yet.");
        }

        public override MethodInfo GetBaseDefinition()
        {
            // Only methods that start a vtable slot (or aren't virtual) are their own base definition.
            if (!IsVirtual || (Attributes & MethodAttributes.NewSlot) != 0) return this;
            throw new NotSupportedException("MethodInfo.GetBaseDefinition of an override is not supported by csharpc yet.");
        }

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            if (IsGenericDefinition) throw new InvalidOperationException("Late bound operations cannot be performed on types or methods for which ContainsGenericParameters is true.");
            if (!IsStatic)
            {
                if (obj == null) throw new TargetException("Non-static method requires a target.");
                if (!DeclaringType.IsInstanceOfType(obj)) throw new TargetException("Object type " + DeclaringType + " does not match target type " + obj.GetType() + ".");
            }
            return ReflectionRuntime.Invoke(_record, ParametersNoCopy, IsStatic ? null : obj, invokeAttr, parameters, this);
        }

        public override bool IsDefined(Type attributeType, bool inherit) => ReflectionRuntime.IsDefined(ReflectionRecords.MethodAttributes(_record), ReflectionRecords.MethodAttributeCount(_record), attributeType);
        public override object[] GetCustomAttributes(bool inherit) => GetCustomAttributes(typeof(object), inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            // CSRT_METHOD_NO_INHERITED_ATTRIBUTES: the overridden methods have no inheritable attributes.
            if (inherit && IsVirtual && (Attributes & MethodAttributes.NewSlot) == 0 && (ReflectionRecords.MethodExtraFlags(_record) & 2) == 0)
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
        private readonly byte* _methodRecord; // a return parameter: the method's record, which lists its attributes

        internal RuntimeParameterInfoImpl(byte* record, MemberInfo member, int position, Type type, byte* methodRecord = null)
        {
            _record = record;
            _methodRecord = methodRecord;
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

        /// <summary>A ref, in or out parameter: ParameterType is the element type (csharpc has no Type objects for by-ref types).</summary>
        internal bool IsByRefParameter => _record != null && (ReflectionRecords.ParameterFlags(_record) & (1 << 17)) != 0;

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
            _record == null ? ReflectionRuntime.IsDefined(ReflectionRecords.MethodReturnAttributes(_methodRecord), ReflectionRecords.MethodReturnAttributeCount(_methodRecord), attributeType)
                : ReflectionRuntime.IsDefined(ReflectionRecords.ParameterAttributes(_record), ReflectionRecords.ParameterAttributeCount(_record), attributeType);
        public override object[] GetCustomAttributes(bool inherit) => GetCustomAttributes(typeof(object), inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (_record == null) return ReflectionRuntime.CreateAttributes(ReflectionRecords.MethodReturnAttributes(_methodRecord), ReflectionRecords.MethodReturnAttributeCount(_methodRecord), attributeType);
            return ReflectionRuntime.CreateAttributes(ReflectionRecords.ParameterAttributes(_record), ReflectionRecords.ParameterAttributeCount(_record), attributeType);
        }
        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            try
            {
                if (_record == null) return ReflectionRuntime.AttributeData(ReflectionRecords.MethodReturnAttributes(_methodRecord), ReflectionRecords.MethodReturnAttributeCount(_methodRecord));
                return ReflectionRuntime.AttributeData(ReflectionRecords.ParameterAttributes(_record), ReflectionRecords.ParameterAttributeCount(_record));
            }
            catch (NotSupportedException e)
            {
                throw new NotSupportedException(e.Message + " (" + (_record == null ? "the return value" : "parameter '" + Name + "'") + " of '" + MemberImpl.DeclaringType + "." + MemberImpl.Name + "')");
            }
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
                // Keyed by the method's identity: its records in a type's list, a property's accessors and an
                // expression tree's ldtoken are one MethodInfo (== holds, as for RuntimeMethodInfo).
                nint key = ReflectionRecords.MethodIdentity(record);
                if (key == 0) key = (nint)record;
                if (!s_members.TryGetValue(key, out var member))
                {
                    member = constructor ? new RuntimeConstructorInfoImpl(record) : new RuntimeMethodInfoImpl(record);
                    s_members[key] = member;
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

        /// <summary>TypeInfo.GenericTypeParameters: a definition's parameters, no types for anything else.</summary>
        public static Type[] GenericTypeParameters(Type type)
        {
            if (type == null) throw new NullReferenceException();
            return type.IsGenericTypeDefinition ? type.GetGenericArguments() : Type.EmptyTypes;
        }

        // TypeInfo's Declared* members: DeclaredOnly with every visibility and both instance and static members.
        private const BindingFlags DeclaredFlags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static IEnumerable<MethodInfo> GetDeclaredMethods(Type type, string name)
        {
            if (type == null) throw new NullReferenceException();
            var matches = new List<MethodInfo>();
            foreach (var method in GetMethods(type, DeclaredFlags))
                if (method.Name == name) matches.Add(method);
            return matches.ToArray();
        }

        public static MethodInfo GetDeclaredMethod(Type type, string name)
        {
            if (type == null) throw new NullReferenceException();
            return GetMethod(type, name, DeclaredFlags);
        }

        public static IEnumerable<ConstructorInfo> DeclaredConstructors(Type type)
        {
            if (type == null) throw new NullReferenceException();
            return GetConstructors(type, DeclaredFlags);
        }

        public static ConstructorInfo[] GetConstructorsAll(Type type) => GetConstructors(type, BindingFlags.Public | BindingFlags.Instance);
        public static ConstructorInfo[] GetConstructorsFlags(Type type, BindingFlags flags) => GetConstructors(type, flags);

        internal static ConstructorInfo[] GetConstructors(Type type, BindingFlags flags)
        {
            byte* info = InfoOf(type, "constructors");
            // Primitives, enums and interfaces have no instance constructors in metadata (IntPtr and UIntPtr do), whether or not the type is described.
            if (info == null && (flags & BindingFlags.Static) == 0 && (type.IsEnum || type.IsInterface || (type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr))))
                return new ConstructorInfo[0];
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
            // Arrays (their constructors take the lengths) and string (no default constructor) have no parameterless constructor.
            if (types.Length == 0 && (type.IsArray || type == typeof(string)) && InfoOf(type, "constructors") == null) return null;
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

        /// <summary>
        /// GetMethod("op_...") on a type with no reflection entry in a program that keeps every type's operators
        /// (ModuleCodeGen.DescribeDelegateInvokes): the type has none.
        /// </summary>
        private static bool NoOperator(Type type, string name) =>
            name.StartsWith("op_", StringComparison.Ordinal) && CoreIntrinsics.OperatorsKept() && InfoOf(type, "methods") == null;

        internal static MethodInfo GetMethod(Type type, string name, BindingFlags flags)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (NoOperator(type, name)) return null;
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
            if (NoOperator(type, name)) return null;
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
            object[] checkedArguments = parameters.Length == 0 ? Array.Empty<object>() : new object[parameters.Length];
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

        // An attribute list's count carries more than the number of records (ModuleCodeGen.AttributeCount):
        // bits 0-23 the records, bits 24-29 how many of the last records are pseudo-custom attributes (CoreCLR
        // lists those after the metadata's attributes in GetCustomAttributes but before them in
        // GetCustomAttributesData), bit 30 that csc synthesizes attributes csharpc can't reproduce for the member.
        internal const int AttributesUnknown = 0x40000000;
        /// <summary>The list is incomplete (csc adds attributes csharpc cannot reproduce) but its [Nullable] attributes are exact.</summary>
        internal const int AttributesNullableExplicit = unchecked((int)0x80000000);
        [ThreadStatic] private static int s_nullabilityReads;
        /// <summary>NullabilityInfoContext reads attribute lists: those whose [Nullable] attributes are exact are readable.</summary>
        internal static void EnterNullabilityRead() => s_nullabilityReads++;
        internal static void ExitNullabilityRead() => s_nullabilityReads--;
        private static bool UnknownForReader(int raw) =>
            (raw & AttributesUnknown) != 0 && s_partialReads == 0 && !(s_nullabilityReads > 0 && (raw & AttributesNullableExplicit) != 0);

        // The framework's own IL asking for the attributes of a member (System.Text.Json looks for [SetsRequiredMembers], [JsonConstructor]... by name):
        // a list csharpc cannot complete (the attributes csc synthesizes) answers with the attributes it models, where the program's own call gets
        // the explicit NotSupportedException. The framework filters for classes csc never synthesizes.
        [ThreadStatic] private static int s_partialReads;

        public static object[] FrameworkAttributes(object provider, bool inherit) => FrameworkAttributesOf(provider, typeof(object), inherit);

        public static object[] FrameworkAttributesOf(object provider, Type attributeType, bool inherit)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider is Type type) return GetTypeCustomAttributesFramework(type, attributeType, inherit);
            s_partialReads++;
            try
            {
                bool all = attributeType == typeof(object);
                if (provider is MemberInfo member) return all ? member.GetCustomAttributes(inherit) : member.GetCustomAttributes(attributeType, inherit);
                if (provider is ParameterInfo parameter) return all ? parameter.GetCustomAttributes(inherit) : parameter.GetCustomAttributes(attributeType, inherit);
                var other = (ICustomAttributeProvider)provider;
                return all ? other.GetCustomAttributes(inherit) : other.GetCustomAttributes(attributeType, inherit);
            }
            finally { s_partialReads--; }
        }

        public static bool FrameworkIsDefined(object provider, Type attributeType, bool inherit)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider is Type type) return IsTypeDefinedFramework(type, attributeType, inherit);
            s_partialReads++;
            try
            {
                if (provider is MemberInfo member) return member.IsDefined(attributeType, inherit);
                if (provider is ParameterInfo parameter) return parameter.IsDefined(attributeType, inherit);
                return ((ICustomAttributeProvider)provider).IsDefined(attributeType, inherit);
            }
            finally { s_partialReads--; }
        }

        /// <summary>
        /// Whether a typed query could have matched an attribute csc synthesizes into metadata (nullable annotations,
        /// [CompilerGenerated], [ParamArray], state machines, [IsReadOnly], ...): those are System attributes, and
        /// an untyped query (object, Attribute, an interface) asks for all of them.
        /// </summary>
        private static bool MaybeSynthesized(Type attributeType) =>
            attributeType == typeof(object) || attributeType == typeof(Attribute) || attributeType.IsInterface
            || attributeType.Namespace == "System.Runtime.CompilerServices" || attributeType.Namespace == "System.Diagnostics"
            || attributeType == typeof(ParamArrayAttribute);

        private static NotSupportedException UnknownAttributes() =>
            new NotSupportedException("The custom attributes of this member are not supported by csharpc yet: it can't reproduce the attributes " +
                "the C# compiler synthesizes for it (nullable annotations, state machines, readonly members, ...).");

        internal static object[] CreateAttributes(byte* records, int raw, Type attributeType)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            int count = raw & 0xFFFFFF;
            bool all = attributeType == typeof(object);
            if (!all && attributeType != typeof(Attribute) && !attributeType.IsSubclassOf(typeof(Attribute)) && !attributeType.IsInterface)
                throw new ArgumentException("Type passed in must be derived from System.Attribute or System.Attribute itself.");
            if ((raw & AttributesUnknown) != 0 && s_partialReads == 0 && MaybeSynthesized(attributeType)) throw UnknownAttributes();
            var list = new List<object>();
            for (int i = 0; i < count; i++)
            {
                byte* a = records + i * ReflectionRecords.AttributeSize;
                if (!all && !attributeType.IsAssignableFrom(TypeOf(ReflectionRecords.AttributeType(a)))) continue;
                list.Add(CallFactory(ReflectionRecords.AttributeCreate(a)));
            }
            // CoreCLR returns an array of the requested type (object[] / Attribute[] / TagAttribute[]).
            // CreateInstance only for a concrete Attribute subclass: those already have T[] from
            // attribute metadata emission. Do not CreateInstance(typeof(Attribute)) here in a way that
            // notes every TypeInfo type for array keep — Attribute[] is allocated directly.
            object[] result = all ? new object[list.Count]
                : attributeType == typeof(Attribute) ? CoreIntrinsics.ReinterpretAs<object[]>(new Attribute[list.Count])
                : CoreIntrinsics.ReinterpretAs<object[]>(Array.CreateInstance(attributeType, list.Count));
            for (int i = 0; i < list.Count; i++) result[i] = list[i];
            return result;
        }

        internal static bool IsDefined(byte* records, int raw, Type attributeType)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            int count = raw & 0xFFFFFF;
            for (int i = 0; i < count; i++)
                if (attributeType.IsAssignableFrom(TypeOf(ReflectionRecords.AttributeType(records + i * ReflectionRecords.AttributeSize)))) return true;
            if ((raw & AttributesUnknown) != 0 && s_partialReads == 0 && MaybeSynthesized(attributeType)) throw UnknownAttributes();
            return false;
        }

        /// <summary>Assembly.GetCustomAttributesData() over an assembly's compiled-in attribute records.</summary>
        public static IList<CustomAttributeData> AssemblyAttributesData(byte* records, int raw) => AttributeData(records, raw);

        /// <summary>CustomAttributeData lists the pseudo-custom attributes (In, Optional, MarshalAs, ...) before the metadata's.</summary>
        internal static IList<CustomAttributeData> AttributeData(byte* records, int raw)
        {
            if (UnknownForReader(raw)) throw UnknownAttributes();
            int count = raw & 0xFFFFFF;
            int pseudo = (raw >> 24) & 0x3F;
            var list = new CustomAttributeData[count];
            for (int i = 0; i < count; i++)
            {
                int slot = i < pseudo ? count - pseudo + i : i - pseudo;
                list[i] = (CustomAttributeData)CallFactory(ReflectionRecords.AttributeData(records + slot * ReflectionRecords.AttributeSize));
            }
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

        // A type's own attributes (ModuleCodeGen.DescribeTypeAttributes): once the program asks for them, the
        // program's types, the types kept for [DynamicallyAccessedMembers] and every generic parameter with
        // attributes have an entry listing them (CSRT_RI_ATTRIBUTES); a generic parameter without one has none.
        // CSRT_RI_ATTRIBUTES_UNKNOWN marks types whose list csharpc can't reproduce exactly (pseudo-attributes,
        // reference-assembly types, attributes the C# compiler synthesizes). Null: no attributes.
        private static byte* TypeAttributeInfo(Type type)
        {
            byte* info = InfoOf(type, "custom attributes");
            if (info == null)
            {
                if (type.IsGenericParameter) return null;
                throw NotKept(type, "custom attributes");
            }
            uint described = (uint)ReflectionRecords.InfoDescribed(info);
            // 4096: incomplete, but its [Nullable] attributes are exact (what NullabilityInfoContext reads).
            if ((described & 128) != 0 && !(s_nullabilityReads > 0 && (described & 4096) != 0))
                throw new NotSupportedException("The custom attributes of '" + type + "' are not supported by csharpc yet: it can't reproduce the list .NET reports for this type.");
            if ((described & 16) == 0) throw NotKept(type, "custom attributes");
            return info;
        }

        /// <summary>inherit: true adds the base types' inheritable attributes; without attributes on any base type the answer is exact.</summary>
        private static void CheckInherited(Type type, bool inherit)
        {
            if (!inherit || type.IsInterface) return;
            for (Type b = type.BaseType; b != null; b = b.BaseType)
            {
                // Object / other bases with no attribute table entry have nothing to inherit — don't
                // demand DynamicallyAccessedMembers metadata for them (Attribute.GetCustomAttributes
                // defaults to inherit: true; Type.GetCustomAttributes(true) did the same).
                if (IsCoreLibType(b)) continue; // csharpc keeps no attributes for a CoreLib base
                byte* info = InfoOf(b, "custom attributes");
                if (info == null) continue;
                uint described = (uint)ReflectionRecords.InfoDescribed(info);
                if ((described & 128) != 0)
                    throw new NotSupportedException("Inherited custom attributes of '" + type + "' (from '" + b + "') are not supported by csharpc yet.");
                if ((described & 16) == 0) continue;
                if (ReflectionRecords.InfoAttributeCount(info) > 0)
                    throw new NotSupportedException("Inherited custom attributes of '" + type + "' (from '" + b + "') are not supported by csharpc yet.");
            }
        }

        /// <summary>
        /// Whether `type` is a System.Private.CoreLib type and `attributeType` an attribute class from another assembly: CoreLib
        /// references no other assembly, so none of its types carries such an attribute and the exact answer is "none", whether or
        /// not the type is described (a query for CoreLib's own attribute classes, or for all of them, stays unanswerable).
        /// </summary>
        private static bool CannotCarry(Type type, Type attributeType)
        {
            // An array type's attributes are System.Array's, whatever its element type.
            try
            {
                if ((type.IsArray || IsCoreLib(type.Assembly)) && !IsCoreLib(attributeType.Assembly)) return true;
                // A framework type carries attribute classes of its own assembly and of the assemblies it references; the framework's
                // assemblies reference each other in layers, so a class of a higher one (System.Text.Json's) is never on a lower one's types.
                if (IsCoreLib(attributeType.Assembly) || type.IsGenericParameter) return false;
                string typeAssembly = AssemblyName(type.Assembly), attributeAssembly = AssemblyName(attributeType.Assembly);
                return typeAssembly != attributeAssembly && !IsProgramName(typeAssembly) && ReferencesOf(typeAssembly) is { } references && !references.Contains(attributeAssembly);
            }
            catch (NotSupportedException) { return false; }
        }

        private static string AssemblyName(Assembly assembly)
        {
            string full = assembly.FullName;
            int comma = full.IndexOf(',');
            return comma < 0 ? full : full.Substring(0, comma);
        }

        private static bool IsProgramName(string name) => !(name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal) || name == "netstandard");

        private static Dictionary<string, HashSet<string>> s_references;

        /// <summary>The assemblies a framework assembly references (compile-time table); null where the table does not list it.</summary>
        private static HashSet<string> ReferencesOf(string assembly)
        {
            if (s_references == null)
            {
                var table = new Dictionary<string, HashSet<string>>();
                byte* text = *(byte**)CoreIntrinsics.AssemblyReferencesAddress();
                if (text != null)
                    foreach (string entry in Utf8.FromNative(text).Split(';'))
                    {
                        int colon = entry.IndexOf(':');
                        if (colon <= 0) continue;
                        table[entry.Substring(0, colon)] = new HashSet<string>(entry.Substring(colon + 1).Split(','));
                    }
                s_references = table;
            }
            return s_references.TryGetValue(assembly, out var references) ? references : null;
        }

        private static bool IsCoreLib(Assembly assembly) => assembly.FullName.StartsWith("System.Private.CoreLib,", StringComparison.Ordinal);

        internal static object[] GetTypeCustomAttributes(Type type, Type attributeType, bool inherit)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            if (CannotCarry(type, attributeType)) return CreateAttributes(null, 0, attributeType);
            byte* info = TypeAttributeInfo(type);
            // inherit: true lists the base classes' inheritable attributes too, which the compiler worked out for the class.
            if (inherit && info != null && ReflectionRecords.InfoInheritedKnown(info))
                return CreateAttributes(ReflectionRecords.InfoInheritedAttributes(info), ReflectionRecords.InfoInheritedAttributeCount(info), attributeType);
            CheckInherited(type, inherit);
            return info == null ? CreateAttributes(null, 0, attributeType)
                : CreateAttributes(ReflectionRecords.InfoAttributes(info), ReflectionRecords.InfoAttributeCount(info), attributeType);
        }

        /// <summary>Whether `type` or a base type carries an attribute named `fullName`: 1 yes, 0 no (every
        /// level's attributes are described), -1 unknown (metadata not kept). Never throws.</summary>
        internal static int HasTypeAttributeNamed(Type type, string fullName)
        {
            for (Type t = type; t != null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
            {
                byte* info = InfoOf(t, "custom attributes");
                if (info == null) return -1;
                uint described = (uint)ReflectionRecords.InfoDescribed(info);
                if ((described & 128) != 0 || (described & 16) == 0) return -1;
                byte* records = ReflectionRecords.InfoAttributes(info);
                int count = ReflectionRecords.InfoAttributeCount(info);
                for (int i = 0; i < count; i++)
                    if (TypeOf(ReflectionRecords.AttributeType(records + i * ReflectionRecords.AttributeSize)).FullName == fullName) return 1;
            }
            return 0;
        }

        /// <summary>
        /// Type.GetCustomAttributes(inherit) called from the framework's IL: for a System.Private.CoreLib type whose list csharpc can't
        /// reproduce, the attributes it does describe (none for a type it doesn't) instead of an error, since the framework asks for
        /// classes of other assemblies (F# ones), which CoreLib types never carry.
        /// </summary>
        public static object[] TypeAttributesAllFramework(Type type, bool inherit) => GetTypeCustomAttributesFramework(type, typeof(object), inherit);
        public static object[] TypeAttributesOfFramework(Type type, Type attributeType, bool inherit) => GetTypeCustomAttributesFramework(type, attributeType, inherit);

        public static bool IsTypeDefinedFramework(Type type, Type attributeType, bool inherit)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            if (CannotCarry(type, attributeType)) return false;
            byte* info = InfoOf(type, "custom attributes");
            if (info == null) return !(type.IsGenericParameter || IsCoreLibType(type) || !IsProgramAssembly(type)) && IsTypeDefined(type, attributeType, inherit);
            uint described = (uint)ReflectionRecords.InfoDescribed(info);
            if (PartialAnswer(described, type))
                return (described & 16) != 0 && IsDefined(ReflectionRecords.InfoAttributes(info), ReflectionRecords.InfoAttributeCount(info), attributeType);
            return IsTypeDefined(type, attributeType, inherit);
        }

        private static object[] GetTypeCustomAttributesFramework(Type type, Type attributeType, bool inherit)
        {
            s_partialReads++;
            try { return GetTypeCustomAttributesFrameworkCore(type, attributeType, inherit); }
            finally { s_partialReads--; }
        }

        private static object[] GetTypeCustomAttributesFrameworkCore(Type type, Type attributeType, bool inherit)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            if (CannotCarry(type, attributeType)) return CreateAttributes(null, 0, attributeType);
            byte* info = InfoOf(type, "custom attributes");
            // A type nothing describes (a framework type, a generic parameter without attributes): none. A CoreLib type whose list
            // csharpc cannot reproduce: the part it describes.
            if (info == null) return type.IsGenericParameter || IsCoreLibType(type) || !IsProgramAssembly(type) ? CreateAttributes(null, 0, attributeType) : GetTypeCustomAttributes(type, attributeType, inherit);
            uint described = (uint)ReflectionRecords.InfoDescribed(info);
            if (PartialAnswer(described, type))
                return (described & 16) == 0 ? CreateAttributes(null, 0, attributeType)
                    : CreateAttributes(ReflectionRecords.InfoAttributes(info), ReflectionRecords.InfoAttributeCount(info), attributeType);
            return GetTypeCustomAttributes(type, attributeType, inherit);
        }

        /// <summary>Whether `type` belongs to the program (its sources, or its assembly as IL) rather than to a framework or library assembly.</summary>
        private static bool IsProgramAssembly(Type type)
        {
            try { return !IsFrameworkAssembly(type.Assembly); }
            catch (NotSupportedException) { return false; }
        }

        private static bool IsFrameworkAssembly(Assembly assembly)
        {
            string name = assembly.FullName;
            return name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal) || name.StartsWith("netstandard,", StringComparison.Ordinal);
        }

        /// <summary>A framework type whose list csharpc cannot give (unknown, or not kept): the framework's own query takes the part it describes.</summary>
        private static bool PartialAnswer(uint described, Type type) =>
            ((described & 128) != 0 || (described & 16) == 0) && (IsCoreLibType(type) || !IsProgramAssembly(type));

        private static bool IsCoreLibType(Type type)
        {
            if (type.IsArray) return true; // the attributes of an array type are System.Array's
            try { return IsCoreLib(type.Assembly); }
            catch (NotSupportedException) { return false; }
        }

        public static object[] TypeAttributesAll(Type type, bool inherit) => GetTypeCustomAttributes(type, typeof(object), inherit);
        public static object[] TypeAttributesOf(Type type, Type attributeType, bool inherit) => GetTypeCustomAttributes(type, attributeType, inherit);

        public static bool IsTypeDefined(Type type, Type attributeType, bool inherit)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            if (CannotCarry(type, attributeType)) return false;
            byte* info = TypeAttributeInfo(type);
            if (inherit && info != null && ReflectionRecords.InfoInheritedKnown(info))
                return IsDefined(ReflectionRecords.InfoInheritedAttributes(info), ReflectionRecords.InfoInheritedAttributeCount(info), attributeType);
            CheckInherited(type, inherit);
            return info != null && IsDefined(ReflectionRecords.InfoAttributes(info), ReflectionRecords.InfoAttributeCount(info), attributeType);
        }

        /// <summary>Type.GetCustomAttributesData() called from the framework's IL: the attributes csharpc models where it cannot complete the list.</summary>
        public static IList<CustomAttributeData> GetTypeCustomAttributesDataFramework(Type type)
        {
            s_partialReads++;
            try
            {
                byte* info = InfoOf(type, "custom attributes");
                bool framework = type.IsGenericParameter || IsCoreLibType(type) || !IsProgramAssembly(type);
                if (info == null) return framework ? AttributeData(null, 0) : GetTypeCustomAttributesData(type);
                uint described = (uint)ReflectionRecords.InfoDescribed(info);
                if (PartialAnswer(described, type))
                    return (described & 16) == 0 ? AttributeData(null, 0) : AttributeData(ReflectionRecords.InfoAttributes(info), ReflectionRecords.InfoAttributeCount(info));
                return GetTypeCustomAttributesData(type);
            }
            finally { s_partialReads--; }
        }

        public static IList<CustomAttributeData> GetTypeCustomAttributesData(Type type)
        {
            byte* info = TypeAttributeInfo(type);
            return info == null ? AttributeData(null, 0) : AttributeData(ReflectionRecords.InfoAttributes(info), ReflectionRecords.InfoAttributeCount(info));
        }

        // ---- Arrays ------------------------------------------------------------------------------

        public static Array CreateInstance(Type elementType, int length)
        {
            ArgumentNullException.ThrowIfNull(elementType);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            return CoreIntrinsics.csrt_array_create_instance(elementType, length, CoreIntrinsics.TypeInfoTableAddress());
        }

        /// <summary>Activator.CreateInstance(Type): public parameterless constructor via kept metadata, with
        /// RuntimeType.CreateInstanceDefaultCtor's checks and messages.</summary>
        /// <summary>
        /// RuntimeHelpers.GetUninitializedObject(type): a zeroed instance without running a constructor (for
        /// Nullable&lt;T&gt;, a boxed default T), after CoreCLR's checks in its order and with its exceptions
        /// (ReflectionInvocation_GetUninitializedObject); the allocation is csrt's.
        /// </summary>
        public static object GetUninitializedObject(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type == typeof(void) || type.IsArray || type.IsPointer || type.IsByRef || type.IsGenericParameter || type.IsFunctionPointer
                || type.IsSubclassOf(typeof(MulticastDelegate)))
                throw new ArgumentException("Type is not supported.");
            if (type == typeof(string)) throw new ArgumentException("Uninitialized Strings cannot be created.");
            if (type.IsInterface) throw new MemberAccessException("Cannot create an instance of an interface.");
            if (type.IsAbstract) throw new MemberAccessException("Cannot create an abstract class.");
            if (type.ContainsGenericParameters) throw new MemberAccessException("Cannot create a type for which Type.ContainsGenericParameters is true.");
            if (type.IsByRefLike) throw new NotSupportedException("Cannot create boxed ByRef-like values.");
            if (Nullable.GetUnderlyingType(type) is Type underlying) type = underlying;
            return CoreIntrinsics.csrt_type_uninitialized_object(type, CoreIntrinsics.TypeInfoTableAddress());
        }

        // Activator.CreateInstance(Type)'s parameterless constructors by type, copied on write (read without a
        // lock). RazorSlices creates every slice this way where code can't be generated (NativeAOT, csharpc):
        // looking the constructor up each time cost a List, two arrays and the checks per TechEmpower request.
        private static Dictionary<Type, ConstructorInfo> s_defaultConstructors = new Dictionary<Type, ConstructorInfo>();
        private static readonly object s_defaultConstructorsLock = new object();

        public static object ActivatorCreateInstance(Type type) => ActivatorCreateInstance(type, false);

        /// <summary>Activator.CreateInstance(Type, bool nonPublic): the parameterless constructor, a non-public one too when asked;
        /// a value type without one is a boxed default, as in .NET (RuntimeType.CreateInstanceDefaultCtor).</summary>
        public static object ActivatorCreateInstance(Type type, bool nonPublic)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (!nonPublic && Volatile.Read(ref s_defaultConstructors).TryGetValue(type, out ConstructorInfo known)) return known.Invoke(null);
            if (type.ContainsGenericParameters)
                throw new ArgumentException("Cannot create an instance of " + type + " because Type.ContainsGenericParameters is true.");
            if (type.IsInterface)
                throw new MissingMethodException("Cannot dynamically create an instance of type '" + type + "'. Reason: Cannot create an instance of an interface.");
            if (type.IsAbstract)
                throw new MissingMethodException("Cannot dynamically create an instance of type '" + type + "'. Reason: Cannot create an abstract class.");
            if (type.IsArray || type.IsPointer || type.IsByRef || type.IsSubclassOf(typeof(Delegate)))
                throw new NotSupportedException("Activator.CreateInstance(" + type + ") is not supported by csharpc yet.");
            ConstructorInfo ctor = GetConstructor(type, (nonPublic ? BindingFlags.Public | BindingFlags.NonPublic : BindingFlags.Public) | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (ctor != null)
            {
                if (nonPublic) return ctor.Invoke(null);
                lock (s_defaultConstructorsLock)
                {
                    var grown = new Dictionary<Type, ConstructorInfo>(s_defaultConstructors);
                    grown[type] = ctor;
                    Volatile.Write(ref s_defaultConstructors, grown);
                }
                return ctor.Invoke(null);
            }
            // .NET returns a boxed default for a struct without a parameterless constructor.
            if (type.IsValueType) return GetUninitializedObject(type);
            // A non-public parameterless constructor exists when the metadata kept for the type says so (without it, nothing is known of it).
            bool hasNonPublic;
            try { hasNonPublic = !nonPublic && GetConstructor(type, BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null) != null; }
            catch (NotSupportedException) { hasNonPublic = false; }
            if (hasNonPublic) throw new MissingMethodException("No parameterless constructor defined for type '" + type + "'.");
            throw new MissingMethodException("Cannot dynamically create an instance of type '" + type + "'. Reason: No parameterless constructor defined.");
        }

        /// <summary>
        /// Type.GetType(typeName[, throwOnError[, ignoreCase]]): a type the program kept, by its full name; an
        /// assembly qualification is accepted and ignored. Like NativeAOT, a type the compiler didn't keep is
        /// not found (null, or TypeLoadException when throwOnError).
        /// </summary>
        public static Type GetTypeByName(string typeName, bool throwOnError, bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(typeName);
            string normalized = NormalizeTypeName(typeName);
            Type found = normalized == null ? null : CoreIntrinsics.csrt_type_find(normalized, ignoreCase, CoreIntrinsics.TypeInfoTableAddress());
            if (found == null && throwOnError)
                throw new TypeLoadException("Could not resolve type '" + StripAssembly(typeName) + "' in assembly '" + System.Reflection.Assembly.GetEntryAssembly()?.FullName + "'.");
            return found;
        }

        public static Type GetTypeByName(string typeName) => GetTypeByName(typeName, false, false);
        public static Type GetTypeByName(string typeName, bool throwOnError) => GetTypeByName(typeName, throwOnError, false);

        private static string StripAssembly(string name)
        {
            int depth = 0;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '[') depth++;
                else if (c == ']') depth--;
                else if (c == ',' && depth == 0) return name.Substring(0, i).Trim();
            }
            return name.Trim();
        }

        // "Ns.Gen`2[[A, Asm],[B, Asm]], Asm" -> "Ns.Gen`2[A,B]" (the ToString form csharpc names types by).
        private static string NormalizeTypeName(string name)
        {
            name = StripAssembly(name);
            int open = name.IndexOf('[');
            if (open < 0) return name;
            // Array suffixes ("[]", "[,]") follow the name directly.
            if (open + 1 < name.Length && (name[open + 1] == ']' || name[open + 1] == ','))
            {
                string element = NormalizeTypeName(name.Substring(0, open));
                return element == null ? null : element + name.Substring(open);
            }
            int close = MatchingBracket(name, open);
            if (close < 0) return null;
            var args = new System.Collections.Generic.List<string>();
            int start = open + 1, depth = 0;
            for (int i = open + 1; i < close; i++)
            {
                char c = name[i];
                if (c == '[') depth++;
                else if (c == ']') depth--;
                else if (c == ',' && depth == 0) { args.Add(name.Substring(start, i - start)); start = i + 1; }
            }
            args.Add(name.Substring(start, close - start));
            var sb = new System.Text.StringBuilder(name.Substring(0, open)).Append('[');
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i].Trim();
                if (a.Length >= 2 && a[0] == '[' && a[a.Length - 1] == ']') a = a.Substring(1, a.Length - 2);
                string n = NormalizeTypeName(a);
                if (n == null) return null;
                if (i > 0) sb.Append(',');
                sb.Append(n);
            }
            sb.Append(']');
            string rest = name.Substring(close + 1);
            return sb.Append(rest).ToString();
        }

        private static int MatchingBracket(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '[') depth++;
                else if (s[i] == ']' && --depth == 0) return i;
            }
            return -1;
        }

        /// <summary>Type.GetTypeCode(Type): the primitive's code, an enum's underlying type's, Empty for null, else Object.</summary>
        public static TypeCode GetTypeCode(Type type)
        {
            if (type == null) return TypeCode.Empty;
            if (type.IsEnum) type = type.GetEnumUnderlyingType();
            if (type == typeof(bool)) return TypeCode.Boolean;
            if (type == typeof(char)) return TypeCode.Char;
            if (type == typeof(sbyte)) return TypeCode.SByte;
            if (type == typeof(byte)) return TypeCode.Byte;
            if (type == typeof(short)) return TypeCode.Int16;
            if (type == typeof(ushort)) return TypeCode.UInt16;
            if (type == typeof(int)) return TypeCode.Int32;
            if (type == typeof(uint)) return TypeCode.UInt32;
            if (type == typeof(long)) return TypeCode.Int64;
            if (type == typeof(ulong)) return TypeCode.UInt64;
            if (type == typeof(float)) return TypeCode.Single;
            if (type == typeof(double)) return TypeCode.Double;
            if (type == typeof(decimal)) return TypeCode.Decimal;
            if (type == typeof(DateTime)) return TypeCode.DateTime;
            if (type == typeof(string)) return TypeCode.String;
            if (type == typeof(DBNull)) return TypeCode.DBNull;
            return TypeCode.Object;
        }

        /// <summary>
        /// Activator.CreateInstance(Type, BindingFlags, Binder, object[], CultureInfo) with the default binder:
        /// the constructor whose parameters accept the arguments (null fits any reference or nullable type).
        /// More than one candidate would need the binder's best-match rules: reported, never guessed.
        /// </summary>
        public static object ActivatorCreateInstanceWith(Type type, BindingFlags bindingAttr, Binder binder, object[] args, System.Globalization.CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (binder != null) throw new NotSupportedException("Activator.CreateInstance with a custom Binder is not supported by csharpc yet.");
            if ((bindingAttr & (BindingFlags.Public | BindingFlags.NonPublic)) == 0) bindingAttr |= BindingFlags.Public;
            bindingAttr = (bindingAttr | BindingFlags.Instance) & ~BindingFlags.Static;
            args ??= Array.Empty<object>();
            if (args.Length == 0 && type.IsValueType && GetConstructor(type, bindingAttr, null, Type.EmptyTypes, null) == null)
                throw new NotSupportedException("Activator.CreateInstance of value type '" + type + "' is not supported by csharpc yet.");
            ConstructorInfo match = null;
            foreach (ConstructorInfo ctor in GetConstructors(type, bindingAttr))
            {
                ParameterInfo[] parameters = ctor.GetParameters();
                if (parameters.Length != args.Length) continue;
                bool fits = true;
                for (int i = 0; i < parameters.Length && fits; i++)
                {
                    Type p = parameters[i].ParameterType;
                    fits = args[i] == null ? !p.IsValueType || Nullable.GetUnderlyingType(p) != null : p.IsInstanceOfType(args[i]);
                }
                if (!fits) continue;
                if (match != null)
                    throw new NotSupportedException("Activator.CreateInstance on '" + type + "' with arguments several constructors accept is not supported by csharpc yet.");
                match = ctor;
            }
            if (match == null) throw new MissingMethodException("Constructor on type '" + type.FullName + "' not found.");
            return match.Invoke(args);
        }

        public static void SetValue(Array array, object value, int index) =>
            CoreIntrinsics.csrt_array_set_value(array, value, index, CoreIntrinsics.TypeInfoTableAddress());

        public static object GetValue(Array array, int index) =>
            CoreIntrinsics.csrt_array_get_value(array, index, CoreIntrinsics.TypeInfoTableAddress());

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
