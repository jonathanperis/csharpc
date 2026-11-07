// ParameterModifier and RuntimeTypeHandle, and the DefaultValueAttribute that System.ComponentModel's descriptors
// look for. ParameterModifier is .NET's own struct; RuntimeTypeHandle holds the Type object (csharpc's runtime Type
// objects are unique per type, so the handle compares and hashes as .NET's does and `Value` is a stable non-zero
// address), with Type.TypeHandle / Type.GetTypeFromHandle / ldtoken mapped in BclValueTypes.Reflection.cs.
#nullable disable
using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace CsharpcCoreLib
{
    [Shadows("System.Reflection.ParameterModifier")]
    public readonly struct ParameterModifierImpl
    {
        private readonly bool[] _byRef;

        public ParameterModifierImpl(int parameterCount)
        {
            if (parameterCount <= 0) throw new ArgumentException("Must specify one or more parameters.");
            _byRef = new bool[parameterCount];
        }

        public bool this[int index]
        {
            get => _byRef[index];
            set => _byRef[index] = value;
        }
    }

    [Shadows("System.RuntimeTypeHandle")]
    public readonly struct RuntimeTypeHandleImpl : IEquatable<RuntimeTypeHandle>
    {
        private readonly IntPtr _type;

        internal RuntimeTypeHandleImpl(IntPtr type) { _type = type; }

        public IntPtr Value => _type;

        public override bool Equals(object obj) => obj is RuntimeTypeHandle handle && handle.Value == _type;

        public bool Equals(RuntimeTypeHandle handle) => handle.Value == _type;

        public override int GetHashCode() => _type == IntPtr.Zero ? 0 : CoreIntrinsics.PointerToObject(_type).GetHashCode();

        public static bool operator ==(RuntimeTypeHandleImpl left, object right) => left.Equals(right);

        public static bool operator ==(object left, RuntimeTypeHandleImpl right) => right.Equals(left);

        public static bool operator !=(RuntimeTypeHandleImpl left, object right) => !left.Equals(right);

        public static bool operator !=(object left, RuntimeTypeHandleImpl right) => !right.Equals(left);
    }

    /// <summary>Type.TypeHandle, Type.GetTypeFromHandle and the handle an IL ldtoken of a type pushes.</summary>
    internal static class TypeHandles
    {
        public static RuntimeTypeHandleImpl HandleOf(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return new RuntimeTypeHandleImpl(CoreIntrinsics.ObjectToPointer(type));
        }

        public static RuntimeTypeHandleImpl HandleOfObject(object o)
        {
            if (o == null) throw new ArgumentNullException("o");
            return HandleOf(o.GetType());
        }

        public static Type TypeOf(RuntimeTypeHandleImpl handle)
        {
            IntPtr value = handle.Value;
            return value == IntPtr.Zero ? null : (Type)CoreIntrinsics.PointerToObject(value);
        }
    }

    [Shadows("System.ComponentModel.DefaultValueAttribute")]
    public class DefaultValueAttributeImpl : Attribute
    {
        private object _value;

        // The feature switch `System.ComponentModel.DefaultValueAttribute.IsSupported` (off in file-based apps, as under PublishAot):
        // a (Type, string) attribute then refuses to give its value.
        private static bool IsSupported => AppContext.TryGetSwitch("System.ComponentModel.DefaultValueAttribute.IsSupported", out bool supported) ? supported : true;
        private static object s_throwSentinel;
        private static object ThrowSentinel => s_throwSentinel ??= new object();

        public DefaultValueAttributeImpl(Type type, string value)
        {
            if (!IsSupported)
            {
                _value = ThrowSentinel;
                return;
            }
            if (type == null) return;
            TypeConverter converter = null;
            try { converter = TypeDescriptor.GetConverter(type); }
            catch (NotSupportedException) { throw; } // csharpc's TypeDescriptor covers a subset of the types: say so
            catch { }
            try
            {
                object converted = null;
                bool done = false;
                if (converter != null)
                {
                    try { converted = converter.ConvertFromInvariantString(value); done = true; }
                    catch { }
                }
                if (done) _value = converted;
                else if (type.IsSubclassOf(typeof(Enum)) && value != null) _value = Enum.Parse(type, value, true);
                else if (type == typeof(TimeSpan) && value != null) _value = TimeSpan.Parse(value);
                else _value = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            }
            catch { }
        }

        public DefaultValueAttributeImpl(char value) { _value = value; }
        public DefaultValueAttributeImpl(byte value) { _value = value; }
        public DefaultValueAttributeImpl(short value) { _value = value; }
        public DefaultValueAttributeImpl(int value) { _value = value; }
        public DefaultValueAttributeImpl(long value) { _value = value; }
        public DefaultValueAttributeImpl(float value) { _value = value; }
        public DefaultValueAttributeImpl(double value) { _value = value; }
        public DefaultValueAttributeImpl(bool value) { _value = value; }
        public DefaultValueAttributeImpl(string value) { _value = value; }
        public DefaultValueAttributeImpl(object value) { _value = value; }
        public DefaultValueAttributeImpl(sbyte value) { _value = value; }
        public DefaultValueAttributeImpl(ushort value) { _value = value; }
        public DefaultValueAttributeImpl(uint value) { _value = value; }
        public DefaultValueAttributeImpl(ulong value) { _value = value; }

        public virtual object Value
        {
            get
            {
                if (!IsSupported && ReferenceEquals(_value, ThrowSentinel))
                    throw new ArgumentException("Runtime instantiation of this attribute is not allowed.");
                return _value;
            }
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, this)) return true;
            if (obj is not DefaultValueAttribute other) return false;
            if (Value == null) return other.Value == null;
            return Value.Equals(other.Value);
        }

        public override int GetHashCode() => base.GetHashCode();

        protected void SetValue(object value) { _value = value; }
    }
}
