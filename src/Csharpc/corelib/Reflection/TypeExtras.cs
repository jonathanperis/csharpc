// System.Type and runtime members System.Linq.Expressions' interpreter (and other reflection users) call,
// mapped here by BclValueTypes.Reflection.cs.
#nullable disable
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    internal static class TypeExtras
    {
        public static Type EnumUnderlyingType(Type enumType)
        {
            ArgumentNullException.ThrowIfNull(enumType);
            return enumType.GetEnumUnderlyingType();
        }

        public static MethodInfo GetMethodBinder(Type type, string name, BindingFlags flags, Binder binder, Type[] types, ParameterModifier[] modifiers)
        {
            if (binder != null) throw new NotSupportedException("A custom Binder in GetMethod is not supported by csharpc yet.");
            return ReflectionRuntime.GetMethodNameFlagsTypes(type, name, flags, types);
        }

        public static MemberInfo GetMemberWithSameMetadataDefinitionAs(Type type, MemberInfo member)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(member);
            throw new NotSupportedException("Type.GetMemberWithSameMetadataDefinitionAs is not supported by csharpc yet.");
        }

        public static Type MakeArrayTypeRank(Type type, int rank)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (rank <= 0) throw new IndexOutOfRangeException("Index was outside the bounds of the array.");
            throw new NotSupportedException("Type.MakeArrayType(" + rank + ") ('" + type + "') is not supported by csharpc yet.");
        }

        /// <summary>Array.CreateInstance(Type, params int[]): one dimension; rectangular arrays aren't created by reflection yet.</summary>
        public static Array CreateArrayInstance(Type elementType, int[] lengths)
        {
            ArgumentNullException.ThrowIfNull(elementType);
            ArgumentNullException.ThrowIfNull(lengths);
            if (lengths.Length == 0) throw new ArgumentException("Must provide at least one rank.");
            if (lengths.Length == 1) return Array.CreateInstance(elementType, lengths[0]);
            throw new NotSupportedException("Array.CreateInstance of a rectangular array ('" + elementType + "', rank " + lengths.Length + ") is not supported by csharpc yet.");
        }

        public static void ArraySetValueRank(Array array, object value, int[] indices)
        {
            ArgumentNullException.ThrowIfNull(array);
            throw new NotSupportedException("Array.SetValue on a rectangular array is not supported by csharpc yet.");
        }

        /// <summary>Delegate.DynamicInvoke: the delegate type's Invoke method, through reflection (TargetInvocationException included).</summary>
        public static object DynamicInvoke(Delegate d, object[] args)
        {
            if (d == null) throw new NullReferenceException();
            MethodInfo invoke = d.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
            if (invoke == null) throw new NotSupportedException("Delegate.DynamicInvoke of '" + d.GetType() + "' is not supported by csharpc yet.");
            return invoke.Invoke(d, args);
        }

        /// <summary>RuntimeHelpers.GetObjectValue: a boxed copy of a non-primitive value type, else the object itself.</summary>
        public static object GetObjectValue(object obj)
        {
            if (obj == null || obj is not ValueType || obj is Enum) return obj;
            Type type = obj.GetType();
            if (type.IsPrimitive) return obj;
            return CoreIntrinsics.csrt_object_memberwise_clone(obj);
        }
    }

    [Shadows("System.Runtime.CompilerServices.RuntimeWrappedException")]
    public sealed class RuntimeWrappedExceptionImpl : Exception
    {
        private readonly object _wrappedException;

        public RuntimeWrappedExceptionImpl(object thrownObject)
            : base("An object that does not derive from System.Exception has been wrapped in a RuntimeWrappedException.")
        {
            _wrappedException = thrownObject;
        }

        public object WrappedException => _wrappedException;
    }

    [ShadowMembers("System.Numerics.BitOperations")]
    internal static class BitOperationsMembers
    {
        public static uint RoundUpToPowerOf2(uint value)
        {
            --value;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        public static ulong RoundUpToPowerOf2(ulong value)
        {
            --value;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value |= value >> 32;
            return value + 1;
        }
    }
}
