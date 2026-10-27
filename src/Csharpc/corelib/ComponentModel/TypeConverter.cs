// System.ComponentModel.TypeConverter / TypeDescriptor: what Microsoft.Extensions.Configuration.Binder
// uses (GetConverter → CanConvertFrom(typeof(string)) → ConvertFromInvariantString). Shadowing these keeps
// System.ComponentModel.TypeConverter.dll out of the closed world (ReflectTypeDescriptionProvider and
// dozens of Half/Int128/COM APIs). The string, number, Boolean, Char and Guid converters follow .NET's
// (trim, hex prefixes, NumberStyles, exception types and messages). Any type whose .NET converter isn't
// reproduced here — enums, Nullable<T>, TimeSpan and Version (no Parse in the corelib yet), DateTime,
// Uri, a [TypeConverter] attribute, or a type whose attributes csharpc can't see — is an explicit
// NotSupportedException from GetConverter: a converter that can't convert would make Binder silently
// leave the setting at its default.
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.ComponentModel.TypeConverter")]
    public class TypeConverterImpl
    {
        public TypeConverterImpl() { }

        public bool CanConvertFrom(Type sourceType) => CanConvertFrom(null, sourceType);

        public virtual bool CanConvertFrom(object context, Type sourceType) => false;

        public object ConvertFrom(object value) => ConvertFrom(null, CultureInfo.CurrentCulture, value);

        public object ConvertFromInvariantString(string text) =>
            ConvertFromString(null, CultureInfo.InvariantCulture, text);

        public object ConvertFromString(string text) =>
            ConvertFromString(null, CultureInfo.CurrentCulture, text);

        public virtual object ConvertFromString(object context, CultureInfo culture, string text) =>
            ConvertFrom(context, culture, text);

        public virtual object ConvertFrom(object context, CultureInfo culture, object value)
        {
            throw new NotSupportedException(GetType().Name + " cannot convert from " +
                (value == null ? "(null)" : value.GetType().FullName) + ".");
        }

        internal static string InvalidPrimitive(string text, string type) => text + " is not a valid value for " + type + ".";
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
            // typeof(object) and types csharpc can see carry no [TypeConverter]: .NET's converter for them
            // (TypeConverter, ArrayConverter, CollectionConverter, ReferenceConverter) can't convert from a
            // string without a context either. Enums, Nullable<T> and the remaining BCL converters (TimeSpan,
            // Version, DateTime, DateTimeOffset, DateOnly, TimeOnly, Uri, CultureInfo, Half, Int128, UInt128) can.
            if (type == typeof(object)) return new TypeConverterImpl();
            if (!type.IsEnum && Nullable.GetUnderlyingType(type) == null && !type.IsPrimitive && type.Namespace != "System"
                && ReflectionRuntime.HasTypeAttributeNamed(type, "System.ComponentModel.TypeConverterAttribute") == 0)
                return new TypeConverterImpl();
            throw new NotSupportedException("TypeDescriptor.GetConverter(" + type + ") is not supported by csharpc yet.");
        }

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
            public override bool CanConvertFrom(object context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(object context, CultureInfo culture, object value)
            {
                if (value is string) return value;
                if (value == null) return string.Empty;
                return base.ConvertFrom(context, culture, value);
            }
        }

        internal sealed class BooleanConverter : CsharpcCoreLib.TypeConverterImpl
        {
            public override bool CanConvertFrom(object context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(object context, CultureInfo culture, object value)
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
            public override bool CanConvertFrom(object context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(object context, CultureInfo culture, object value)
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

        internal sealed class GuidConverter : CsharpcCoreLib.TypeConverterImpl
        {
            public override bool CanConvertFrom(object context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(object context, CultureInfo culture, object value) =>
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

            public override bool CanConvertFrom(object context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object ConvertFrom(object context, CultureInfo culture, object value)
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
