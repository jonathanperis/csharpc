// System.ComponentModel.TypeConverter / TypeDescriptor: what Microsoft.Extensions.Configuration.Binder
// uses (GetConverter → CanConvertFrom(typeof(string)) → ConvertFromInvariantString), and the base class of
// the converters compiled from System.ComponentModel.TypeConverter's IL (PointConverter, ColorConverter, ...: that
// assembly is "mixed", FrameworkIl.MixedWithShadows, so TypeConverter is this class and its derived converters
// are the framework's own code). TypeConverter follows dotnet/runtime's member for member (the virtual
// ITypeDescriptorContext overloads, ConvertTo, CreateInstance, standard values); only InstanceDescriptor, which
// needs reflection invocation, is an explicit NotSupportedException. Shadowing TypeDescriptor keeps
// ReflectTypeDescriptionProvider and dozens of Half/Int128/COM APIs out of the closed world. The string,
// number, Boolean, Char, Guid, TimeSpan and Version converters follow .NET's (trim, hex prefixes,
// NumberStyles, exception types and messages). Any type whose .NET converter isn't reproduced here — enums,
// Nullable<T>, DateTime, Uri, a [TypeConverter] attribute, or a type whose attributes csharpc can't see — is an
// explicit NotSupportedException from GetConverter: a converter that can't convert would make Binder silently
// leave the setting at its default.
#nullable disable
using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.ComponentModel.TypeConverter")]
    public class TypeConverterImpl
    {
        private const string InstanceDescriptorName = "System.ComponentModel.Design.Serialization.InstanceDescriptor";

        public TypeConverterImpl() { }

        public bool CanConvertFrom(Type sourceType) => CanConvertFrom(null, sourceType);

        // InstanceDescriptor is compared by name so that a program that never meets one doesn't need its assembly.
        public virtual bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
            sourceType != null && sourceType.FullName == InstanceDescriptorName;

        public bool CanConvertTo(Type destinationType) => CanConvertTo(null, destinationType);

        public virtual bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) => destinationType == typeof(string);

        public object ConvertFrom(object value) => ConvertFrom(null, CultureInfo.CurrentCulture, value);

        public virtual object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value != null && value.GetType().FullName == InstanceDescriptorName)
                throw new NotSupportedException("Converting an InstanceDescriptor (System.ComponentModel.TypeConverter.ConvertFrom) is not supported by csharpc yet: it invokes a member through reflection.");
            throw GetConvertFromException(value);
        }

        public object ConvertFromInvariantString(string text) => ConvertFromString(null, CultureInfo.InvariantCulture, text);

        public object ConvertFromInvariantString(ITypeDescriptorContext context, string text) =>
            ConvertFromString(context, CultureInfo.InvariantCulture, text);

        public object ConvertFromString(string text) => ConvertFrom(null, null, text);

        public object ConvertFromString(ITypeDescriptorContext context, string text) =>
            ConvertFrom(context, CultureInfo.CurrentCulture, text);

        public object ConvertFromString(ITypeDescriptorContext context, CultureInfo culture, string text) =>
            ConvertFrom(context, culture, text);

        public object ConvertTo(object value, Type destinationType) => ConvertTo(null, null, value, destinationType);

        public virtual object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(destinationType);
            if (destinationType == typeof(string))
            {
                if (value == null) return string.Empty;
                if (culture != null && culture != CultureInfo.CurrentCulture && value is IFormattable formattable)
                    return formattable.ToString(null, culture);
                return value.ToString();
            }
            throw GetConvertToException(value, destinationType);
        }

        public string ConvertToInvariantString(object value) => ConvertToString(null, CultureInfo.InvariantCulture, value);

        public string ConvertToInvariantString(ITypeDescriptorContext context, object value) =>
            ConvertToString(context, CultureInfo.InvariantCulture, value);

        public string ConvertToString(object value) => (string)ConvertTo(null, CultureInfo.CurrentCulture, value, typeof(string));

        public string ConvertToString(ITypeDescriptorContext context, object value) =>
            (string)ConvertTo(context, CultureInfo.CurrentCulture, value, typeof(string));

        public string ConvertToString(ITypeDescriptorContext context, CultureInfo culture, object value) =>
            (string)ConvertTo(context, culture, value, typeof(string));

        public object CreateInstance(IDictionary propertyValues) => CreateInstance(null, propertyValues);

        public virtual object CreateInstance(ITypeDescriptorContext context, IDictionary propertyValues) => null;

        protected Exception GetConvertFromException(object value)
        {
            string valueTypeName = value == null ? "(null)" : value.GetType().FullName;
            throw new NotSupportedException(GetType().Name + " cannot convert from " + valueTypeName + ".");
        }

        protected Exception GetConvertToException(object value, Type destinationType)
        {
            string valueTypeName = value == null ? "(null)" : value.GetType().FullName;
            throw new NotSupportedException("'" + GetType().Name + "' is unable to convert '" + valueTypeName + "' to '" + destinationType.FullName + "'.");
        }

        public bool GetCreateInstanceSupported() => GetCreateInstanceSupported(null);

        public virtual bool GetCreateInstanceSupported(ITypeDescriptorContext context) => false;

        public PropertyDescriptorCollection GetProperties(object value) => GetProperties(null, value);

        public PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value) =>
            GetProperties(context, value, new Attribute[] { BrowsableAttribute.Yes });

        public virtual PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes) => null;

        public bool GetPropertiesSupported() => GetPropertiesSupported(null);

        public virtual bool GetPropertiesSupported(ITypeDescriptorContext context) => false;

        public ICollection GetStandardValues() => GetStandardValues(null);

        public virtual TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) => null;

        public bool GetStandardValuesExclusive() => GetStandardValuesExclusive(null);

        public virtual bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

        public bool GetStandardValuesSupported() => GetStandardValuesSupported(null);

        public virtual bool GetStandardValuesSupported(ITypeDescriptorContext context) => false;

        public bool IsValid(object value) => IsValid(null, value);

        public virtual bool IsValid(ITypeDescriptorContext context, object value)
        {
            bool isValid = true;
            try
            {
                // null has no type to pass to CanConvertFrom; NullableConverter-like converters accept it.
                if (value == null || CanConvertFrom(context, value.GetType())) ConvertFrom(context, CultureInfo.InvariantCulture, value);
                else isValid = false;
            }
            catch
            {
                isValid = false;
            }
            return isValid;
        }

        protected PropertyDescriptorCollection SortProperties(PropertyDescriptorCollection props, string[] names)
        {
            props.Sort(names);
            return props;
        }

        internal static string InvalidPrimitive(string text, string type) => text + " is not a valid value for " + type + ".";

        /// <summary>A collection of standard values (EnumConverter, ColorConverter, ...).</summary>
        [Shadows("System.ComponentModel.TypeConverter+StandardValuesCollection")]
        public class StandardValuesCollectionImpl : ICollection
        {
            private readonly ICollection _values;
            private Array _valueArray;

            public StandardValuesCollectionImpl(ICollection values)
            {
                values ??= Array.Empty<object>();
                if (values is Array a) _valueArray = a;
                _values = values;
            }

            public int Count => _valueArray != null ? _valueArray.Length : _values.Count;

            public object this[int index]
            {
                get
                {
                    if (_valueArray != null) return _valueArray.GetValue(index);
                    if (_values is IList list) return list[index];
                    // No other choice but to enumerate the collection.
                    _valueArray = new object[_values.Count];
                    _values.CopyTo(_valueArray, 0);
                    return _valueArray.GetValue(index);
                }
            }

            public void CopyTo(Array array, int index) => _values.CopyTo(array, index);

            public IEnumerator GetEnumerator() => _values.GetEnumerator();

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => null;
        }

        /// <summary>Provides properties for objects that have none (the base of ArrayConverter's element descriptors).</summary>
        [Shadows("System.ComponentModel.TypeConverter+SimplePropertyDescriptor")]
        protected abstract class SimplePropertyDescriptorImpl : PropertyDescriptor
        {
            protected SimplePropertyDescriptorImpl(Type componentType, string name, Type propertyType) : this(componentType, name, propertyType, Array.Empty<Attribute>())
            {
            }

            protected SimplePropertyDescriptorImpl(Type componentType, string name, Type propertyType, Attribute[] attributes) : base(name, attributes)
            {
                ComponentType = componentType;
                PropertyType = propertyType;
            }

            public override Type ComponentType { get; }

            public override bool IsReadOnly => Attributes.Contains(ReadOnlyAttribute.Yes);

            public override Type PropertyType { get; }

            public override bool CanResetValue(object component)
            {
                DefaultValueAttribute attr = (DefaultValueAttribute)Attributes[typeof(DefaultValueAttribute)];
                if (attr == null) return false;
                return attr.Value.Equals(GetValue(component));
            }

            public override void ResetValue(object component)
            {
                DefaultValueAttribute attr = (DefaultValueAttribute)Attributes[typeof(DefaultValueAttribute)];
                if (attr != null) SetValue(component, attr.Value);
            }

            public override bool ShouldSerializeValue(object component) => false;
        }
    }


    [Shadows("System.ComponentModel.TypeDescriptor")]
    public static class TypeDescriptorImpl
    {
        // Return type is the shadow; CoreLib matching ignores return types (docs/12-corelib.md).
        public static TypeConverterImpl GetConverter(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type == typeof(string)) return new ComponentModelConverters.StringConverter();
            if (type == typeof(int)) return new ComponentModelConverters.Int32Converter();
            if (type == typeof(bool)) return new ComponentModelConverters.BooleanConverter();
            if (type == typeof(long)) return new ComponentModelConverters.Int64Converter();
            if (type == typeof(double)) return new ComponentModelConverters.DoubleConverter();
            if (type == typeof(uint)) return new ComponentModelConverters.UInt32Converter();
            if (type == typeof(ulong)) return new ComponentModelConverters.UInt64Converter();
            if (type == typeof(short)) return new ComponentModelConverters.Int16Converter();
            if (type == typeof(ushort)) return new ComponentModelConverters.UInt16Converter();
            if (type == typeof(byte)) return new ComponentModelConverters.ByteConverter();
            if (type == typeof(sbyte)) return new ComponentModelConverters.SByteConverter();
            if (type == typeof(float)) return new ComponentModelConverters.SingleConverter();
            if (type == typeof(decimal)) return new ComponentModelConverters.DecimalConverter();
            if (type == typeof(char)) return new ComponentModelConverters.CharConverter();
            if (type == typeof(Guid)) return new ComponentModelConverters.GuidConverter();
            if (type == typeof(TimeSpan)) return new ComponentModelConverters.TimeSpanConverter();
            if (type == typeof(Version)) return new ComponentModelConverters.VersionConverter();
            // typeof(object) and types csharpc can see carry no [TypeConverter]: .NET's converter for them
            // (TypeConverter, ArrayConverter, CollectionConverter, ReferenceConverter) can't convert from a
            // string without a context either. Enums, Nullable<T> and the remaining BCL converters (DateTime, DateTimeOffset, DateOnly, TimeOnly, Uri, CultureInfo, Half, Int128, UInt128) can.
            if (type == typeof(object)) return new TypeConverterImpl();
            // [TypeConverter] attributes, enums, Nullable<T> and the other BCL converters: the front end generated them for
            // the types the program names in typeof(...) next to a GetConverter call.
            if (CoreIntrinsics.TypeConverterHook(type) is object hooked) return (TypeConverterImpl)hooked;
            if (!type.IsEnum && Nullable.GetUnderlyingType(type) == null && !type.IsPrimitive && type.Namespace != "System"
                && ReflectionRuntime.HasTypeAttributeNamed(type, "System.ComponentModel.TypeConverterAttribute") == 0)
                return new TypeConverterImpl();
            throw new NotSupportedException("TypeDescriptor.GetConverter(" + type + ") is not supported by csharpc yet.");
        }

        /// <summary>csharpc has no type description providers: a type's reflection type is itself.</summary>
        public static Type GetReflectionType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return type;
        }

        /// <summary>The internal overload the framework's own converters call (PointConverter's int converter).</summary>
        internal static TypeConverterImpl GetConverterTrimUnsafe(Type type) => GetConverter(type);

        public static TypeConverterImpl GetConverter(object component)
        {
            ArgumentNullException.ThrowIfNull(component);
            return GetConverter(component.GetType());
        }
    }
}

namespace CsharpcCoreLib.ComponentModelConverters
{
        /// <summary>The names below are .NET's (GetType().Name appears in ConvertFrom's NotSupportedException).</summary>
        internal sealed class StringConverter : CsharpcCoreLib.TypeConverterImpl
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                if (value is string) return value;
                if (value == null) return string.Empty;
                return base.ConvertFrom(context, culture, value);
            }
        }

        internal sealed class BooleanConverter : CsharpcCoreLib.TypeConverterImpl
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                if (value is string s)
                {
                    try { return bool.Parse(s.Trim()); }
                    catch (FormatException e) { throw new FormatException(CsharpcCoreLib.TypeConverterImpl.InvalidPrimitive(s, "Boolean"), e); }
                }
                return base.ConvertFrom(context, culture, value);
            }
        }

        internal sealed class CharConverter : CsharpcCoreLib.TypeConverterImpl
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                if (value is string text)
                {
                    if (text.Length > 1) text = text.Trim();
                    if (text.Length > 0)
                    {
                        if (text.Length != 1) throw new FormatException(CsharpcCoreLib.TypeConverterImpl.InvalidPrimitive(text, "Char"));
                        return text[0];
                    }
                    return '\0';
                }
                return base.ConvertFrom(context, culture, value);
            }
        }

        internal sealed class TimeSpanConverter : CsharpcCoreLib.TypeConverterImpl
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                if (value is string s)
                {
                    try { return TimeSpan.Parse(s.Trim(), culture); }
                    catch (FormatException e) { throw new FormatException(CsharpcCoreLib.TypeConverterImpl.InvalidPrimitive(s, "TimeSpan"), e); }
                }
                return base.ConvertFrom(context, culture, value);
            }
        }

        internal sealed class VersionConverter : CsharpcCoreLib.TypeConverterImpl
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                if (value is string s)
                {
                    try { return Version.Parse(s); }
                    catch (Exception e) { throw new FormatException(CsharpcCoreLib.TypeConverterImpl.InvalidPrimitive(s, "Version"), e); }
                }
                return base.ConvertFrom(context, culture, value);
            }
        }

        internal sealed class GuidConverter : CsharpcCoreLib.TypeConverterImpl
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) =>
                value is string s ? new Guid(s.Trim()) : base.ConvertFrom(context, culture, value);
        }

        /// <summary>.NET's BaseNumberConverter: trims, "#"/"0x"/"&amp;h" select hex for integers, else
        /// Parse with the culture's NumberFormatInfo; any failure becomes an ArgumentException.</summary>
        internal abstract class BaseNumberConverter : CsharpcCoreLib.TypeConverterImpl
        {
            internal virtual bool AllowHex => true;
            internal abstract string TargetName { get; }
            internal abstract object FromString(string value, int radix);
            internal abstract object FromString(string value, NumberFormatInfo formatInfo);

            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                if (value is string s)
                {
                    string text = s.Trim();
                    try
                    {
                        if (AllowHex && text[0] == '#') return FromString(text.Substring(1), 16);
                        if (AllowHex && (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || text.StartsWith("&h", StringComparison.OrdinalIgnoreCase)))
                            return FromString(text.Substring(2), 16);
                        return FromString(text, NumberFormatInfo.GetInstance(culture ?? CultureInfo.CurrentCulture));
                    }
                    catch (Exception e)
                    {
                        throw new ArgumentException(CsharpcCoreLib.TypeConverterImpl.InvalidPrimitive(text, TargetName), "value", e);
                    }
                }
                return base.ConvertFrom(context, culture, value);
            }
        }

        internal sealed class ByteConverter : BaseNumberConverter
        {
            internal override string TargetName => "Byte";
            internal override object FromString(string v, int radix) => Convert.ToByte(v, radix);
            internal override object FromString(string v, NumberFormatInfo f) => byte.Parse(v, NumberStyles.Integer, f);
        }

        internal sealed class SByteConverter : BaseNumberConverter
        {
            internal override string TargetName => "SByte";
            internal override object FromString(string v, int radix) => Convert.ToSByte(v, radix);
            internal override object FromString(string v, NumberFormatInfo f) => sbyte.Parse(v, NumberStyles.Integer, f);
        }

        internal sealed class Int16Converter : BaseNumberConverter
        {
            internal override string TargetName => "Int16";
            internal override object FromString(string v, int radix) => Convert.ToInt16(v, radix);
            internal override object FromString(string v, NumberFormatInfo f) => short.Parse(v, NumberStyles.Integer, f);
        }

        internal sealed class UInt16Converter : BaseNumberConverter
        {
            internal override string TargetName => "UInt16";
            internal override object FromString(string v, int radix) => Convert.ToUInt16(v, radix);
            internal override object FromString(string v, NumberFormatInfo f) => ushort.Parse(v, NumberStyles.Integer, f);
        }

        internal sealed class Int32Converter : BaseNumberConverter
        {
            internal override string TargetName => "Int32";
            internal override object FromString(string v, int radix) => Convert.ToInt32(v, radix);
            internal override object FromString(string v, NumberFormatInfo f) => int.Parse(v, NumberStyles.Integer, f);
        }

        internal sealed class UInt32Converter : BaseNumberConverter
        {
            internal override string TargetName => "UInt32";
            internal override object FromString(string v, int radix) => Convert.ToUInt32(v, radix);
            internal override object FromString(string v, NumberFormatInfo f) => uint.Parse(v, NumberStyles.Integer, f);
        }

        internal sealed class Int64Converter : BaseNumberConverter
        {
            internal override string TargetName => "Int64";
            internal override object FromString(string v, int radix) => Convert.ToInt64(v, radix);
            internal override object FromString(string v, NumberFormatInfo f) => long.Parse(v, NumberStyles.Integer, f);
        }

        internal sealed class UInt64Converter : BaseNumberConverter
        {
            internal override string TargetName => "UInt64";
            internal override object FromString(string v, int radix) => Convert.ToUInt64(v, radix);
            internal override object FromString(string v, NumberFormatInfo f) => ulong.Parse(v, NumberStyles.Integer, f);
        }

        internal sealed class SingleConverter : BaseNumberConverter
        {
            internal override bool AllowHex => false;
            internal override string TargetName => "Single";
            internal override object FromString(string v, int radix) => Convert.ToSingle(v, CultureInfo.CurrentCulture);
            internal override object FromString(string v, NumberFormatInfo f) => float.Parse(v, NumberStyles.Float, f);
        }

        internal sealed class DoubleConverter : BaseNumberConverter
        {
            internal override bool AllowHex => false;
            internal override string TargetName => "Double";
            internal override object FromString(string v, int radix) => Convert.ToDouble(v, CultureInfo.CurrentCulture);
            internal override object FromString(string v, NumberFormatInfo f) => double.Parse(v, NumberStyles.Float, f);
        }

        internal sealed class DecimalConverter : BaseNumberConverter
        {
            internal override bool AllowHex => false;
            internal override string TargetName => "Decimal";
            internal override object FromString(string v, int radix) => Convert.ToDecimal(v, CultureInfo.CurrentCulture);
            internal override object FromString(string v, NumberFormatInfo f) => decimal.Parse(v, NumberStyles.Float, f);
        }
}
