// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// System.Convert's conversions between primitives, strings (with providers and bases) and DateTime,
// ported from dotnet/runtime (release/11.0) src/libraries/System.Private.CoreLib/src/System/Convert.cs and
// ParseNumbers.cs; see THIRD-PARTY-NOTICES.md. The object/IConvertible overloads and ChangeType match the BCL's
// IConvertible types by type (csharpc's boxed primitives have no IConvertible dispatch); hex and Base64 are in ConvertExtras.cs. Conversions that .NET routes through IConvertible and
// that always fail (e.g. ToBoolean(char), ToDateTime(int)) throw its InvalidCastException directly.
#nullable disable
using System;
using System.Diagnostics;
using System.Globalization;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Convert")]
    internal static class ConvertPort
    {
        // Convert.ToString(object): .NET asks IConvertible, then IFormattable, then ToString() (null: the empty
        // string). Every BCL IConvertible is IFormattable with the same result, or (bool, char, string) formats
        // like ToString(); boxed primitives have no IConvertible dispatch in csharpc, so only other types ask it.
        public static string ToString(object value) => ToString(value, null);

        public static string ToString(object value, IFormatProvider provider)
        {
            if (value is IConvertible convertible && !IsBuiltinConvertible(value) && value is not Enum) return convertible.ToString(provider);
            if (value is IFormattable formattable) return formattable.ToString(null, provider);
            return value != null ? value.ToString() : string.Empty;
        }

        // Convert.ToX(object[, provider]): .NET calls ((IConvertible)value).ToX(provider) (null: the default). csharpc's
        // boxed primitives have no IConvertible dispatch, so the BCL's IConvertible types are matched here, each with
        // exactly what its IConvertible implementation does (the typed overloads below; strings parse with the
        // provider), boxed enums through their underlying value, and anything else through IConvertible itself.

        public static bool ToBoolean(object value) => ToBoolean(value, null);

        public static bool ToBoolean(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return false;
                case string v: return ToBoolean(v, provider);
                case bool v: return ToBoolean(v);
                case char v: return ToBoolean(v);
                case sbyte v: return ToBoolean(v);
                case byte v: return ToBoolean(v);
                case short v: return ToBoolean(v);
                case ushort v: return ToBoolean(v);
                case int v: return ToBoolean(v);
                case uint v: return ToBoolean(v);
                case long v: return ToBoolean(v);
                case ulong v: return ToBoolean(v);
                case float v: return ToBoolean(v);
                case double v: return ToBoolean(v);
                case decimal v: return ToBoolean(v);
                case DateTime v: return ToBoolean(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToBoolean(signed) : ToBoolean(unsigned);
                case IConvertible v: return v.ToBoolean(provider);
                default: throw NotConvertible(value);
            }
        }

        public static char ToChar(object value) => ToChar(value, null);

        public static char ToChar(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return '\0';
                case string v: return ToChar(v, provider);
                case bool v: return ToChar(v);
                case char v: return ToChar(v);
                case sbyte v: return ToChar(v);
                case byte v: return ToChar(v);
                case short v: return ToChar(v);
                case ushort v: return ToChar(v);
                case int v: return ToChar(v);
                case uint v: return ToChar(v);
                case long v: return ToChar(v);
                case ulong v: return ToChar(v);
                case float v: return ToChar(v);
                case double v: return ToChar(v);
                case decimal v: return ToChar(v);
                case DateTime v: return ToChar(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToChar(signed) : ToChar(unsigned);
                case IConvertible v: return v.ToChar(provider);
                default: throw NotConvertible(value);
            }
        }

        public static sbyte ToSByte(object value) => ToSByte(value, null);

        public static sbyte ToSByte(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToSByte(v, provider);
                case bool v: return ToSByte(v);
                case char v: return ToSByte(v);
                case sbyte v: return ToSByte(v);
                case byte v: return ToSByte(v);
                case short v: return ToSByte(v);
                case ushort v: return ToSByte(v);
                case int v: return ToSByte(v);
                case uint v: return ToSByte(v);
                case long v: return ToSByte(v);
                case ulong v: return ToSByte(v);
                case float v: return ToSByte(v);
                case double v: return ToSByte(v);
                case decimal v: return ToSByte(v);
                case DateTime v: return ToSByte(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToSByte(signed) : ToSByte(unsigned);
                case IConvertible v: return v.ToSByte(provider);
                default: throw NotConvertible(value);
            }
        }

        public static byte ToByte(object value) => ToByte(value, null);

        public static byte ToByte(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToByte(v, provider);
                case bool v: return ToByte(v);
                case char v: return ToByte(v);
                case sbyte v: return ToByte(v);
                case byte v: return ToByte(v);
                case short v: return ToByte(v);
                case ushort v: return ToByte(v);
                case int v: return ToByte(v);
                case uint v: return ToByte(v);
                case long v: return ToByte(v);
                case ulong v: return ToByte(v);
                case float v: return ToByte(v);
                case double v: return ToByte(v);
                case decimal v: return ToByte(v);
                case DateTime v: return ToByte(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToByte(signed) : ToByte(unsigned);
                case IConvertible v: return v.ToByte(provider);
                default: throw NotConvertible(value);
            }
        }

        public static short ToInt16(object value) => ToInt16(value, null);

        public static short ToInt16(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToInt16(v, provider);
                case bool v: return ToInt16(v);
                case char v: return ToInt16(v);
                case sbyte v: return ToInt16(v);
                case byte v: return ToInt16(v);
                case short v: return ToInt16(v);
                case ushort v: return ToInt16(v);
                case int v: return ToInt16(v);
                case uint v: return ToInt16(v);
                case long v: return ToInt16(v);
                case ulong v: return ToInt16(v);
                case float v: return ToInt16(v);
                case double v: return ToInt16(v);
                case decimal v: return ToInt16(v);
                case DateTime v: return ToInt16(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToInt16(signed) : ToInt16(unsigned);
                case IConvertible v: return v.ToInt16(provider);
                default: throw NotConvertible(value);
            }
        }

        public static ushort ToUInt16(object value) => ToUInt16(value, null);

        public static ushort ToUInt16(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToUInt16(v, provider);
                case bool v: return ToUInt16(v);
                case char v: return ToUInt16(v);
                case sbyte v: return ToUInt16(v);
                case byte v: return ToUInt16(v);
                case short v: return ToUInt16(v);
                case ushort v: return ToUInt16(v);
                case int v: return ToUInt16(v);
                case uint v: return ToUInt16(v);
                case long v: return ToUInt16(v);
                case ulong v: return ToUInt16(v);
                case float v: return ToUInt16(v);
                case double v: return ToUInt16(v);
                case decimal v: return ToUInt16(v);
                case DateTime v: return ToUInt16(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToUInt16(signed) : ToUInt16(unsigned);
                case IConvertible v: return v.ToUInt16(provider);
                default: throw NotConvertible(value);
            }
        }

        public static int ToInt32(object value) => ToInt32(value, null);

        public static int ToInt32(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToInt32(v, provider);
                case bool v: return ToInt32(v);
                case char v: return ToInt32(v);
                case sbyte v: return ToInt32(v);
                case byte v: return ToInt32(v);
                case short v: return ToInt32(v);
                case ushort v: return ToInt32(v);
                case int v: return ToInt32(v);
                case uint v: return ToInt32(v);
                case long v: return ToInt32(v);
                case ulong v: return ToInt32(v);
                case float v: return ToInt32(v);
                case double v: return ToInt32(v);
                case decimal v: return ToInt32(v);
                case DateTime v: return ToInt32(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToInt32(signed) : ToInt32(unsigned);
                case IConvertible v: return v.ToInt32(provider);
                default: throw NotConvertible(value);
            }
        }

        public static uint ToUInt32(object value) => ToUInt32(value, null);

        public static uint ToUInt32(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToUInt32(v, provider);
                case bool v: return ToUInt32(v);
                case char v: return ToUInt32(v);
                case sbyte v: return ToUInt32(v);
                case byte v: return ToUInt32(v);
                case short v: return ToUInt32(v);
                case ushort v: return ToUInt32(v);
                case int v: return ToUInt32(v);
                case uint v: return ToUInt32(v);
                case long v: return ToUInt32(v);
                case ulong v: return ToUInt32(v);
                case float v: return ToUInt32(v);
                case double v: return ToUInt32(v);
                case decimal v: return ToUInt32(v);
                case DateTime v: return ToUInt32(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToUInt32(signed) : ToUInt32(unsigned);
                case IConvertible v: return v.ToUInt32(provider);
                default: throw NotConvertible(value);
            }
        }

        public static long ToInt64(object value) => ToInt64(value, null);

        public static long ToInt64(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToInt64(v, provider);
                case bool v: return ToInt64(v);
                case char v: return ToInt64(v);
                case sbyte v: return ToInt64(v);
                case byte v: return ToInt64(v);
                case short v: return ToInt64(v);
                case ushort v: return ToInt64(v);
                case int v: return ToInt64(v);
                case uint v: return ToInt64(v);
                case long v: return ToInt64(v);
                case ulong v: return ToInt64(v);
                case float v: return ToInt64(v);
                case double v: return ToInt64(v);
                case decimal v: return ToInt64(v);
                case DateTime v: return ToInt64(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToInt64(signed) : ToInt64(unsigned);
                case IConvertible v: return v.ToInt64(provider);
                default: throw NotConvertible(value);
            }
        }

        public static ulong ToUInt64(object value) => ToUInt64(value, null);

        public static ulong ToUInt64(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToUInt64(v, provider);
                case bool v: return ToUInt64(v);
                case char v: return ToUInt64(v);
                case sbyte v: return ToUInt64(v);
                case byte v: return ToUInt64(v);
                case short v: return ToUInt64(v);
                case ushort v: return ToUInt64(v);
                case int v: return ToUInt64(v);
                case uint v: return ToUInt64(v);
                case long v: return ToUInt64(v);
                case ulong v: return ToUInt64(v);
                case float v: return ToUInt64(v);
                case double v: return ToUInt64(v);
                case decimal v: return ToUInt64(v);
                case DateTime v: return ToUInt64(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToUInt64(signed) : ToUInt64(unsigned);
                case IConvertible v: return v.ToUInt64(provider);
                default: throw NotConvertible(value);
            }
        }

        public static float ToSingle(object value) => ToSingle(value, null);

        public static float ToSingle(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToSingle(v, provider);
                case bool v: return ToSingle(v);
                case char v: return ToSingle(v);
                case sbyte v: return ToSingle(v);
                case byte v: return ToSingle(v);
                case short v: return ToSingle(v);
                case ushort v: return ToSingle(v);
                case int v: return ToSingle(v);
                case uint v: return ToSingle(v);
                case long v: return ToSingle(v);
                case ulong v: return ToSingle(v);
                case float v: return ToSingle(v);
                case double v: return ToSingle(v);
                case decimal v: return ToSingle(v);
                case DateTime v: return ToSingle(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToSingle(signed) : ToSingle(unsigned);
                case IConvertible v: return v.ToSingle(provider);
                default: throw NotConvertible(value);
            }
        }

        public static double ToDouble(object value) => ToDouble(value, null);

        public static double ToDouble(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToDouble(v, provider);
                case bool v: return ToDouble(v);
                case char v: return ToDouble(v);
                case sbyte v: return ToDouble(v);
                case byte v: return ToDouble(v);
                case short v: return ToDouble(v);
                case ushort v: return ToDouble(v);
                case int v: return ToDouble(v);
                case uint v: return ToDouble(v);
                case long v: return ToDouble(v);
                case ulong v: return ToDouble(v);
                case float v: return ToDouble(v);
                case double v: return ToDouble(v);
                case decimal v: return ToDouble(v);
                case DateTime v: return ToDouble(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToDouble(signed) : ToDouble(unsigned);
                case IConvertible v: return v.ToDouble(provider);
                default: throw NotConvertible(value);
            }
        }

        public static decimal ToDecimal(object value) => ToDecimal(value, null);

        public static decimal ToDecimal(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return 0;
                case string v: return ToDecimal(v, provider);
                case bool v: return ToDecimal(v);
                case char v: return ToDecimal(v);
                case sbyte v: return ToDecimal(v);
                case byte v: return ToDecimal(v);
                case short v: return ToDecimal(v);
                case ushort v: return ToDecimal(v);
                case int v: return ToDecimal(v);
                case uint v: return ToDecimal(v);
                case long v: return ToDecimal(v);
                case ulong v: return ToDecimal(v);
                case float v: return ToDecimal(v);
                case double v: return ToDecimal(v);
                case decimal v: return ToDecimal(v);
                case DateTime v: return ToDecimal(v);
                case Enum: return EnumIsNegative(value, out long signed, out ulong unsigned) ? ToDecimal(signed) : ToDecimal(unsigned);
                case IConvertible v: return v.ToDecimal(provider);
                default: throw NotConvertible(value);
            }
        }

        public static DateTime ToDateTime(object value) => ToDateTime(value, null);

        public static DateTime ToDateTime(object value, IFormatProvider provider)
        {
            switch (value)
            {
                case null: return DateTime.MinValue;
                case string v: return ToDateTime(v, provider);
                case bool v: return ToDateTime(v);
                case char v: return ToDateTime(v);
                case sbyte v: return ToDateTime(v);
                case byte v: return ToDateTime(v);
                case short v: return ToDateTime(v);
                case ushort v: return ToDateTime(v);
                case int v: return ToDateTime(v);
                case uint v: return ToDateTime(v);
                case long v: return ToDateTime(v);
                case ulong v: return ToDateTime(v);
                case float v: return ToDateTime(v);
                case double v: return ToDateTime(v);
                case decimal v: return ToDateTime(v);
                case DateTime v: return ToDateTime(v);
                case Enum: throw new NotSupportedException("not supported by csharpc yet: Convert.ToDateTime of an enum value");
                case IConvertible v: return v.ToDateTime(provider);
                default: throw NotConvertible(value);
            }
        }

        // A boxed enum's underlying value, from its "D" format (csharpc's enum boxes implement IFormattable only).
        private static bool EnumIsNegative(object value, out long signed, out ulong unsigned)
        {
            string digits = ((IFormattable)value).ToString("D", CultureInfo.InvariantCulture);
            signed = 0;
            unsigned = 0;
            if (digits.Length > 0 && digits[0] == '-')
            {
                signed = long.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                return true;
            }
            unsigned = ulong.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
            return false;
        }

        private static InvalidCastException NotConvertible(object value) =>
            new InvalidCastException("Unable to cast object of type '" + value.GetType().FullName + "' to type 'System.IConvertible'.");

        public static TypeCode GetTypeCode(object value) => value switch
        {
            null => TypeCode.Empty,
            bool => TypeCode.Boolean,
            char => TypeCode.Char,
            sbyte => TypeCode.SByte,
            byte => TypeCode.Byte,
            short => TypeCode.Int16,
            ushort => TypeCode.UInt16,
            int => TypeCode.Int32,
            uint => TypeCode.UInt32,
            long => TypeCode.Int64,
            ulong => TypeCode.UInt64,
            float => TypeCode.Single,
            double => TypeCode.Double,
            decimal => TypeCode.Decimal,
            DateTime => TypeCode.DateTime,
            string => TypeCode.String,
            Enum => throw new NotSupportedException("not supported by csharpc yet: Convert.GetTypeCode of an enum value"),
            IConvertible c => c.GetTypeCode(),
            _ => TypeCode.Object,
        };

        // Convert.ChangeType, as .NET's (Convert.cs), with the conversions above in place of the IConvertible calls.
        public static object ChangeType(object value, TypeCode typeCode) => ChangeType(value, typeCode, CultureInfo.CurrentCulture);

        public static object ChangeType(object value, TypeCode typeCode, IFormatProvider provider)
        {
            if (value == null && (typeCode == TypeCode.Empty || typeCode == TypeCode.String || typeCode == TypeCode.Object))
                return null;
            if (!IsConvertible(value)) throw new InvalidCastException("Object must implement IConvertible.");
            switch (typeCode)
            {
                case TypeCode.Boolean: return ToBoolean(value, provider);
                case TypeCode.Char: return ToChar(value, provider);
                case TypeCode.SByte: return ToSByte(value, provider);
                case TypeCode.Byte: return ToByte(value, provider);
                case TypeCode.Int16: return ToInt16(value, provider);
                case TypeCode.UInt16: return ToUInt16(value, provider);
                case TypeCode.Int32: return ToInt32(value, provider);
                case TypeCode.UInt32: return ToUInt32(value, provider);
                case TypeCode.Int64: return ToInt64(value, provider);
                case TypeCode.UInt64: return ToUInt64(value, provider);
                case TypeCode.Single: return ToSingle(value, provider);
                case TypeCode.Double: return ToDouble(value, provider);
                case TypeCode.Decimal: return ToDecimal(value, provider);
                case TypeCode.DateTime: return ToDateTime(value, provider);
                case TypeCode.String: return ToString(value, provider);
                case TypeCode.Object: return value;
                case TypeCode.DBNull: throw new InvalidCastException("Object cannot be cast to DBNull.");
                case TypeCode.Empty: throw new InvalidCastException("Object cannot be cast to Empty.");
                default: throw new ArgumentException("Unknown TypeCode value.");
            }
        }

        public static object ChangeType(object value, Type conversionType) => ChangeType(value, conversionType, CultureInfo.CurrentCulture);

        public static object ChangeType(object value, Type conversionType, IFormatProvider provider)
        {
            if (conversionType == null) throw new ArgumentNullException(nameof(conversionType));
            if (value == null)
            {
                if (conversionType.IsValueType) throw new InvalidCastException("Null object cannot be converted to a value type.");
                return null;
            }
            if (!IsConvertible(value))
            {
                if (value.GetType() == conversionType) return value;
                throw new InvalidCastException("Object must implement IConvertible.");
            }
            if (conversionType == typeof(bool)) return ToBoolean(value, provider);
            if (conversionType == typeof(char)) return ToChar(value, provider);
            if (conversionType == typeof(sbyte)) return ToSByte(value, provider);
            if (conversionType == typeof(byte)) return ToByte(value, provider);
            if (conversionType == typeof(short)) return ToInt16(value, provider);
            if (conversionType == typeof(ushort)) return ToUInt16(value, provider);
            if (conversionType == typeof(int)) return ToInt32(value, provider);
            if (conversionType == typeof(uint)) return ToUInt32(value, provider);
            if (conversionType == typeof(long)) return ToInt64(value, provider);
            if (conversionType == typeof(ulong)) return ToUInt64(value, provider);
            if (conversionType == typeof(float)) return ToSingle(value, provider);
            if (conversionType == typeof(double)) return ToDouble(value, provider);
            if (conversionType == typeof(decimal)) return ToDecimal(value, provider);
            if (conversionType == typeof(DateTime)) return ToDateTime(value, provider);
            if (conversionType == typeof(string)) return ToString(value, provider);
            if (conversionType == typeof(object)) return value;
            if (conversionType == typeof(Enum)) return (Enum)value;
            // IConvertible.ToType: the BCL's types (Convert.DefaultToType) return the value for its own type and
            // fail for any other; a user IConvertible decides itself.
            if (value.GetType() == conversionType) return value;
            if (value is Enum) throw new NotSupportedException("not supported by csharpc yet: Convert.ChangeType of an enum value to '" + conversionType.FullName + "'");
            if (value is IConvertible custom && !IsBuiltinConvertible(value)) return custom.ToType(conversionType, provider);
            throw new InvalidCastException("Invalid cast from '" + value.GetType().FullName + "' to '" + conversionType.FullName + "'.");
        }

        private static bool IsBuiltinConvertible(object value) =>
            value is bool or char or sbyte or byte or short or ushort or int or uint or long or ulong or float or double
                or decimal or DateTime or string;

        private static bool IsConvertible(object value) => IsBuiltinConvertible(value) || value is Enum || value is IConvertible;

        internal static void ThrowOverflow(string type)
        {
            switch (type)
            {
                case "sbyte": throw new OverflowException(ConvertSR.Overflow_SByte);
                case "short": throw new OverflowException(ConvertSR.Overflow_Int16);
                case "int": throw new OverflowException(ConvertSR.Overflow_Int32);
                case "uint": throw new OverflowException(ConvertSR.Overflow_UInt32);
                default: throw new OverflowException(ConvertSR.Overflow_Int64);
            }
        }

        public static bool ToBoolean(bool value)
        {
            return value;
        }

        public static bool ToBoolean(sbyte value)
        {
            return value != 0;
        }

        public static bool ToBoolean(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'Boolean'.");
        }

        public static bool ToBoolean(byte value)
        {
            return value != 0;
        }

        public static bool ToBoolean(short value)
        {
            return value != 0;
        }

        public static bool ToBoolean(ushort value)
        {
            return value != 0;
        }

        public static bool ToBoolean(int value)
        {
            return value != 0;
        }

        public static bool ToBoolean(uint value)
        {
            return value != 0;
        }

        public static bool ToBoolean(long value)
        {
            return value != 0;
        }

        public static bool ToBoolean(ulong value)
        {
            return value != 0;
        }

        public static bool ToBoolean(string? value)
        {
            if (value == null)
                return false;
            return bool.Parse(value);
        }

        public static bool ToBoolean(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return false;
            return bool.Parse(value);
        }

        public static bool ToBoolean(float value)
        {
            return value != 0;
        }

        public static bool ToBoolean(double value)
        {
            return value != 0;
        }

        public static bool ToBoolean(decimal value)
        {
            return value != 0;
        }

        public static bool ToBoolean(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Boolean'.");
        }

        public static char ToChar(bool value)
        {
            throw new InvalidCastException("Invalid cast from 'Boolean' to 'Char'.");
        }

        public static char ToChar(char value)
        {
            return value;
        }

        public static char ToChar(sbyte value)
        {
            if (value < 0) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(byte value)
        {
            return (char)value;
        }

        public static char ToChar(short value)
        {
            if (value < 0) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(ushort value)
        {
            return (char)value;
        }

        public static char ToChar(int value) => Convert.ToChar((uint)value);

        public static char ToChar(uint value)
        {
            if (value > char.MaxValue) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(long value) => Convert.ToChar((ulong)value);

        public static char ToChar(ulong value)
        {
            if (value > char.MaxValue) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(string value)
        {
            return Convert.ToChar(value, null);
        }

        public static char ToChar(string value, IFormatProvider? provider)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Length != 1)
                throw new FormatException(ConvertSR.Format_NeedSingleChar);

            return value[0];
        }

        public static char ToChar(float value)
        {
            throw new InvalidCastException("Invalid cast from 'Single' to 'Char'.");
        }

        public static char ToChar(double value)
        {
            throw new InvalidCastException("Invalid cast from 'Double' to 'Char'.");
        }

        public static char ToChar(decimal value)
        {
            throw new InvalidCastException("Invalid cast from 'Decimal' to 'Char'.");
        }

        public static char ToChar(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Char'.");
        }

        public static sbyte ToSByte(bool value)
        {
            return value ? (sbyte)1 : (sbyte)0;
        }

        public static sbyte ToSByte(sbyte value)
        {
            return value;
        }

        public static sbyte ToSByte(char value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(byte value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(short value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(ushort value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(int value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(uint value)
        {
            if (value > (uint)sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(long value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(ulong value)
        {
            if (value > (ulong)sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(float value)
        {
            return Convert.ToSByte((double)value);
        }

        public static sbyte ToSByte(double value)
        {
            return Convert.ToSByte(Convert.ToInt32(value));
        }

        public static sbyte ToSByte(decimal value)
        {
            return decimal.ToSByte(decimal.Round(value, 0));
        }

        public static sbyte ToSByte(string? value)
        {
            if (value == null)
                return 0;
            return sbyte.Parse(value);
        }

        public static sbyte ToSByte(string value, IFormatProvider? provider)
        {
            return sbyte.Parse(value, provider);
        }

        public static sbyte ToSByte(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'SByte'.");
        }

        public static byte ToByte(bool value)
        {
            return value ? (byte)1 : (byte)0;
        }

        public static byte ToByte(byte value)
        {
            return value;
        }

        public static byte ToByte(char value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(sbyte value)
        {
            if (value < 0) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(short value)
        {
            if ((uint)value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(ushort value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(int value) => Convert.ToByte((uint)value);

        public static byte ToByte(uint value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(long value) => Convert.ToByte((ulong)value);

        public static byte ToByte(ulong value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(float value)
        {
            return Convert.ToByte((double)value);
        }

        public static byte ToByte(double value)
        {
            return Convert.ToByte(Convert.ToInt32(value));
        }

        public static byte ToByte(decimal value)
        {
            return decimal.ToByte(decimal.Round(value, 0));
        }

        public static byte ToByte(string? value)
        {
            if (value == null)
                return 0;
            return byte.Parse(value);
        }

        public static byte ToByte(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return byte.Parse(value, provider);
        }

        public static byte ToByte(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Byte'.");
        }

        public static short ToInt16(bool value)
        {
            return value ? (short)1 : (short)0;
        }

        public static short ToInt16(char value)
        {
            if (value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(sbyte value)
        {
            return value;
        }

        public static short ToInt16(byte value)
        {
            return value;
        }

        public static short ToInt16(ushort value)
        {
            if (value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(int value)
        {
            if (value < short.MinValue || value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(uint value)
        {
            if (value > (uint)short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(short value)
        {
            return value;
        }

        public static short ToInt16(long value)
        {
            if (value < short.MinValue || value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(ulong value)
        {
            if (value > (ulong)short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(float value)
        {
            return Convert.ToInt16((double)value);
        }

        public static short ToInt16(double value)
        {
            return Convert.ToInt16(Convert.ToInt32(value));
        }

        public static short ToInt16(decimal value)
        {
            return decimal.ToInt16(decimal.Round(value, 0));
        }

        public static short ToInt16(string? value)
        {
            if (value == null)
                return 0;
            return short.Parse(value);
        }

        public static short ToInt16(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return short.Parse(value, provider);
        }

        public static short ToInt16(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Int16'.");
        }

        public static ushort ToUInt16(bool value)
        {
            return value ? (ushort)1 : (ushort)0;
        }

        public static ushort ToUInt16(char value)
        {
            return value;
        }

        public static ushort ToUInt16(sbyte value)
        {
            if (value < 0) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(byte value)
        {
            return value;
        }

        public static ushort ToUInt16(short value)
        {
            if (value < 0) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(int value) => Convert.ToUInt16((uint)value);

        public static ushort ToUInt16(ushort value)
        {
            return value;
        }

        public static ushort ToUInt16(uint value)
        {
            if (value > ushort.MaxValue) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(long value) => Convert.ToUInt16((ulong)value);

        public static ushort ToUInt16(ulong value)
        {
            if (value > ushort.MaxValue) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(float value)
        {
            return Convert.ToUInt16((double)value);
        }

        public static ushort ToUInt16(double value)
        {
            return Convert.ToUInt16(Convert.ToInt32(value));
        }

        public static ushort ToUInt16(decimal value)
        {
            return decimal.ToUInt16(decimal.Round(value, 0));
        }

        public static ushort ToUInt16(string? value)
        {
            if (value == null)
                return 0;
            return ushort.Parse(value);
        }

        public static ushort ToUInt16(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return ushort.Parse(value, provider);
        }

        public static ushort ToUInt16(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'UInt16'.");
        }

        public static int ToInt32(bool value)
        {
            return value ? 1 : 0;
        }

        public static int ToInt32(char value)
        {
            return value;
        }

        public static int ToInt32(sbyte value)
        {
            return value;
        }

        public static int ToInt32(byte value)
        {
            return value;
        }

        public static int ToInt32(short value)
        {
            return value;
        }

        public static int ToInt32(ushort value)
        {
            return value;
        }

        public static int ToInt32(uint value)
        {
            if ((int)value < 0) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(int value)
        {
            return value;
        }

        public static int ToInt32(long value)
        {
            if (value < int.MinValue || value > int.MaxValue) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(ulong value)
        {
            if (value > int.MaxValue) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(float value)
        {
            return Convert.ToInt32((double)value);
        }

        public static int ToInt32(double value)
        {
            if (value >= 0)
            {
                if (value < 2147483647.5)
                {
                    int result = (int)value;
                    double dif = value - result;
                    if (dif > 0.5 || dif == 0.5 && (result & 1) != 0) result++;
                    return result;
                }
            }
            else
            {
                if (value >= -2147483648.5)
                {
                    int result = (int)value;
                    double dif = value - result;
                    if (dif < -0.5 || dif == -0.5 && (result & 1) != 0) result--;
                    return result;
                }
            }
            throw new OverflowException(ConvertSR.Overflow_Int32);
        }

        public static int ToInt32(decimal value)
        {
            return decimal.ToInt32(decimal.Round(value, 0));
        }

        public static int ToInt32(string? value)
        {
            if (value == null)
                return 0;
            return int.Parse(value);
        }

        public static int ToInt32(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return int.Parse(value, provider);
        }

        public static int ToInt32(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Int32'.");
        }

        public static uint ToUInt32(bool value)
        {
            return value ? (uint)1 : (uint)0;
        }

        public static uint ToUInt32(char value)
        {
            return value;
        }

        public static uint ToUInt32(sbyte value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(byte value)
        {
            return value;
        }

        public static uint ToUInt32(short value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(ushort value)
        {
            return value;
        }

        public static uint ToUInt32(int value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(uint value)
        {
            return value;
        }

        public static uint ToUInt32(long value) => Convert.ToUInt32((ulong)value);

        public static uint ToUInt32(ulong value)
        {
            if (value > uint.MaxValue) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(float value)
        {
            return Convert.ToUInt32((double)value);
        }

        public static uint ToUInt32(double value)
        {
            if (value >= -0.5 && value < 4294967295.5)
            {
                uint result = (uint)value;
                double dif = value - result;
                if (dif > 0.5 || dif == 0.5 && (result & 1) != 0) result++;
                return result;
            }
            throw new OverflowException(ConvertSR.Overflow_UInt32);
        }

        public static uint ToUInt32(decimal value)
        {
            return decimal.ToUInt32(decimal.Round(value, 0));
        }

        public static uint ToUInt32(string? value)
        {
            if (value == null)
                return 0;
            return uint.Parse(value);
        }

        public static uint ToUInt32(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return uint.Parse(value, provider);
        }

        public static uint ToUInt32(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'UInt32'.");
        }

        public static long ToInt64(bool value)
        {
            return value ? 1 : 0;
        }

        public static long ToInt64(char value)
        {
            return value;
        }

        public static long ToInt64(sbyte value)
        {
            return value;
        }

        public static long ToInt64(byte value)
        {
            return value;
        }

        public static long ToInt64(short value)
        {
            return value;
        }

        public static long ToInt64(ushort value)
        {
            return value;
        }

        public static long ToInt64(int value)
        {
            return value;
        }

        public static long ToInt64(uint value)
        {
            return value;
        }

        public static long ToInt64(ulong value)
        {
            if ((long)value < 0) ThrowInt64OverflowException();
            return (long)value;
        }

        public static long ToInt64(long value)
        {
            return value;
        }

        public static long ToInt64(float value)
        {
            return Convert.ToInt64((double)value);
        }

        public static long ToInt64(double value)
        {
            return checked((long)Math.Round(value));
        }

        public static long ToInt64(decimal value)
        {
            return decimal.ToInt64(decimal.Round(value, 0));
        }

        public static long ToInt64(string? value)
        {
            if (value == null)
                return 0;
            return long.Parse(value);
        }

        public static long ToInt64(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return long.Parse(value, provider);
        }

        public static long ToInt64(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Int64'.");
        }

        public static ulong ToUInt64(bool value)
        {
            return value ? (ulong)1 : (ulong)0;
        }

        public static ulong ToUInt64(char value)
        {
            return value;
        }

        public static ulong ToUInt64(sbyte value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(byte value)
        {
            return value;
        }

        public static ulong ToUInt64(short value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(ushort value)
        {
            return value;
        }

        public static ulong ToUInt64(int value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(uint value)
        {
            return value;
        }

        public static ulong ToUInt64(long value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(ulong value)
        {
            return value;
        }

        public static ulong ToUInt64(float value)
        {
            return Convert.ToUInt64((double)value);
        }

        public static ulong ToUInt64(double value)
        {
            return checked((ulong)Math.Round(value));
        }

        public static ulong ToUInt64(decimal value)
        {
            return decimal.ToUInt64(decimal.Round(value, 0));
        }

        public static ulong ToUInt64(string? value)
        {
            if (value == null)
                return 0;
            return ulong.Parse(value);
        }

        public static ulong ToUInt64(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return ulong.Parse(value, provider);
        }

        public static ulong ToUInt64(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'UInt64'.");
        }

        public static float ToSingle(sbyte value)
        {
            return value;
        }

        public static float ToSingle(byte value)
        {
            return value;
        }

        public static float ToSingle(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'Single'.");
        }

        public static float ToSingle(short value)
        {
            return value;
        }

        public static float ToSingle(ushort value)
        {
            return value;
        }

        public static float ToSingle(int value)
        {
            return value;
        }

        public static float ToSingle(uint value)
        {
            return value;
        }

        public static float ToSingle(long value)
        {
            return value;
        }

        public static float ToSingle(ulong value)
        {
            return value;
        }

        public static float ToSingle(float value)
        {
            return value;
        }

        public static float ToSingle(double value)
        {
            return (float)value;
        }

        public static float ToSingle(decimal value)
        {
            return (float)value;
        }

        public static float ToSingle(string? value)
        {
            if (value == null)
                return 0;
            return float.Parse(value);
        }

        public static float ToSingle(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return float.Parse(value, provider);
        }

        public static float ToSingle(bool value)
        {
            return value ? 1 : 0;
        }

        public static float ToSingle(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Single'.");
        }

        public static double ToDouble(sbyte value)
        {
            return value;
        }

        public static double ToDouble(byte value)
        {
            return value;
        }

        public static double ToDouble(short value)
        {
            return value;
        }

        public static double ToDouble(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'Double'.");
        }

        public static double ToDouble(ushort value)
        {
            return value;
        }

        public static double ToDouble(int value)
        {
            return value;
        }

        public static double ToDouble(uint value)
        {
            return value;
        }

        public static double ToDouble(long value)
        {
            return value;
        }

        public static double ToDouble(ulong value)
        {
            return value;
        }

        public static double ToDouble(float value)
        {
            return value;
        }

        public static double ToDouble(double value)
        {
            return value;
        }

        public static double ToDouble(decimal value)
        {
            return (double)value;
        }

        public static double ToDouble(string? value)
        {
            if (value == null)
                return 0;
            return double.Parse(value);
        }

        public static double ToDouble(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return double.Parse(value, provider);
        }

        public static double ToDouble(bool value)
        {
            return value ? 1 : 0;
        }

        public static double ToDouble(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Double'.");
        }

        public static decimal ToDecimal(sbyte value)
        {
            return value;
        }

        public static decimal ToDecimal(byte value)
        {
            return value;
        }

        public static decimal ToDecimal(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'Decimal'.");
        }

        public static decimal ToDecimal(short value)
        {
            return value;
        }

        public static decimal ToDecimal(ushort value)
        {
            return value;
        }

        public static decimal ToDecimal(int value)
        {
            return value;
        }

        public static decimal ToDecimal(uint value)
        {
            return value;
        }

        public static decimal ToDecimal(long value)
        {
            return value;
        }

        public static decimal ToDecimal(ulong value)
        {
            return value;
        }

        public static decimal ToDecimal(float value)
        {
            return (decimal)value;
        }

        public static decimal ToDecimal(double value)
        {
            return (decimal)value;
        }

        public static decimal ToDecimal(string? value)
        {
            if (value == null)
                return decimal.Zero;
            return decimal.Parse(value);
        }

        public static decimal ToDecimal(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return decimal.Zero;
            return decimal.Parse(value, provider);
        }

        public static decimal ToDecimal(decimal value)
        {
            return value;
        }

        public static decimal ToDecimal(bool value)
        {
            return value ? 1 : 0;
        }

        public static decimal ToDecimal(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Decimal'.");
        }

        public static DateTime ToDateTime(DateTime value)
        {
            return value;
        }

        public static DateTime ToDateTime(string? value)
        {
            if (value == null)
                return new DateTime(0);
            return DateTime.Parse(value);
        }

        public static DateTime ToDateTime(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return new DateTime(0);
            return DateTime.Parse(value, provider);
        }

        public static DateTime ToDateTime(sbyte value)
        {
            throw new InvalidCastException("Invalid cast from 'SByte' to 'DateTime'.");
        }

        public static DateTime ToDateTime(byte value)
        {
            throw new InvalidCastException("Invalid cast from 'Byte' to 'DateTime'.");
        }

        public static DateTime ToDateTime(short value)
        {
            throw new InvalidCastException("Invalid cast from 'Int16' to 'DateTime'.");
        }

        public static DateTime ToDateTime(ushort value)
        {
            throw new InvalidCastException("Invalid cast from 'UInt16' to 'DateTime'.");
        }

        public static DateTime ToDateTime(int value)
        {
            throw new InvalidCastException("Invalid cast from 'Int32' to 'DateTime'.");
        }

        public static DateTime ToDateTime(uint value)
        {
            throw new InvalidCastException("Invalid cast from 'UInt32' to 'DateTime'.");
        }

        public static DateTime ToDateTime(long value)
        {
            throw new InvalidCastException("Invalid cast from 'Int64' to 'DateTime'.");
        }

        public static DateTime ToDateTime(ulong value)
        {
            throw new InvalidCastException("Invalid cast from 'UInt64' to 'DateTime'.");
        }

        public static DateTime ToDateTime(bool value)
        {
            throw new InvalidCastException("Invalid cast from 'Boolean' to 'DateTime'.");
        }

        public static DateTime ToDateTime(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'DateTime'.");
        }

        public static DateTime ToDateTime(float value)
        {
            throw new InvalidCastException("Invalid cast from 'Single' to 'DateTime'.");
        }

        public static DateTime ToDateTime(double value)
        {
            throw new InvalidCastException("Invalid cast from 'Double' to 'DateTime'.");
        }

        public static DateTime ToDateTime(decimal value)
        {
            throw new InvalidCastException("Invalid cast from 'Decimal' to 'DateTime'.");
        }

        public static string ToString(bool value)
        {
            return value.ToString();
        }

        public static string ToString(bool value, IFormatProvider? provider)
        {
            return value.ToString();
        }

        public static string ToString(char value)
        {
            return char.ToString(value);
        }

        public static string ToString(char value, IFormatProvider? provider)
        {
            return value.ToString();
        }

        public static string ToString(sbyte value)
        {
            return value.ToString();
        }

        public static string ToString(sbyte value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(byte value)
        {
            return value.ToString();
        }

        public static string ToString(byte value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(short value)
        {
            return value.ToString();
        }

        public static string ToString(short value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(ushort value)
        {
            return value.ToString();
        }

        public static string ToString(ushort value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(int value)
        {
            return value.ToString();
        }

        public static string ToString(int value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(uint value)
        {
            return value.ToString();
        }

        public static string ToString(uint value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(long value)
        {
            return value.ToString();
        }

        public static string ToString(long value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(ulong value)
        {
            return value.ToString();
        }

        public static string ToString(ulong value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(float value)
        {
            return value.ToString();
        }

        public static string ToString(float value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(double value)
        {
            return value.ToString();
        }

        public static string ToString(double value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(decimal value)
        {
            return value.ToString();
        }

        public static string ToString(decimal value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(DateTime value)
        {
            return value.ToString();
        }

        public static string ToString(DateTime value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string? ToString(string? value)
        {
            return value;
        }

        public static string? ToString(string? value, IFormatProvider? provider)
        {
            return value;
        }

        public static byte ToByte(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }

            if (value == null)
            {
                return 0;
            }

            int r = ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight | ParseNumbers.TreatAsUnsigned);
            if ((uint)r > byte.MaxValue)
                ThrowByteOverflowException();
            return (byte)r;
        }

        public static sbyte ToSByte(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }

            if (value == null)
            {
                return 0;
            }

            int r = ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight | ParseNumbers.TreatAsI1);
            if (fromBase != 10 && r <= byte.MaxValue)
                return (sbyte)r;

            if (r < sbyte.MinValue || r > sbyte.MaxValue)
                ThrowSByteOverflowException();
            return (sbyte)r;
        }

        public static short ToInt16(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }

            if (value == null)
            {
                return 0;
            }

            int r = ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight | ParseNumbers.TreatAsI2);
            if (fromBase != 10 && r <= ushort.MaxValue)
                return (short)r;

            if (r < short.MinValue || r > short.MaxValue)
                ThrowInt16OverflowException();
            return (short)r;
        }

        public static ushort ToUInt16(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }

            if (value == null)
            {
                return 0;
            }

            int r = ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight | ParseNumbers.TreatAsUnsigned);
            if ((uint)r > ushort.MaxValue)
                ThrowUInt16OverflowException();
            return (ushort)r;
        }

        public static int ToInt32(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }
            return value != null ?
                ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight) :
                0;
        }

        public static uint ToUInt32(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }
            return value != null ?
                (uint)ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.TreatAsUnsigned | ParseNumbers.IsTight) :
                0;
        }

        public static long ToInt64(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }
            return value != null ?
                ParseNumbers.StringToLong(value.AsSpan(), fromBase, ParseNumbers.IsTight) :
                0;
        }

        public static ulong ToUInt64(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }
            return value != null ?
                (ulong)ParseNumbers.StringToLong(value.AsSpan(), fromBase, ParseNumbers.TreatAsUnsigned | ParseNumbers.IsTight) :
                0;
        }

        public static string ToString(byte value, int toBase) =>
            Convert.ToString((int)value, toBase);

        public static string ToString(short value, int toBase)
        {
            string format = "d";

            switch (toBase)
            {
                case 2:
                    format = "b";
                    break;

                case 8:
                    return ToOctalString((ushort)value);

                case 10:
                    break;

                case 16:
                    format = "x";
                    break;

                default:
                    ThrowInvalidBase();
                    break;
            };

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string ToString(int value, int toBase)
        {
            string format = "d";

            switch (toBase)
            {
                case 2:
                    format = "b";
                    break;

                case 8:
                    return ToOctalString((uint)value);

                case 10:
                    break;

                case 16:
                    format = "x";
                    break;

                default:
                    ThrowInvalidBase();
                    break;
            };

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string ToString(long value, int toBase)
        {
            string format = "d";

            switch (toBase)
            {
                case 2:
                    format = "b";
                    break;

                case 8:
                    return ToOctalString((ulong)value);

                case 10:
                    break;

                case 16:
                    format = "x";
                    break;

                default:
                    ThrowInvalidBase();
                    break;
            };

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private static void ThrowCharOverflowException() { throw new OverflowException(ConvertSR.Overflow_Char); }

        
        private static void ThrowByteOverflowException() { throw new OverflowException(ConvertSR.Overflow_Byte); }

        
        private static void ThrowSByteOverflowException() { throw new OverflowException(ConvertSR.Overflow_SByte); }

        
        private static void ThrowInt16OverflowException() { throw new OverflowException(ConvertSR.Overflow_Int16); }

        
        private static void ThrowUInt16OverflowException() { throw new OverflowException(ConvertSR.Overflow_UInt16); }

        
        private static void ThrowInt32OverflowException() { throw new OverflowException(ConvertSR.Overflow_Int32); }

        
        private static void ThrowUInt32OverflowException() { throw new OverflowException(ConvertSR.Overflow_UInt32); }

        
        private static void ThrowInt64OverflowException() { throw new OverflowException(ConvertSR.Overflow_Int64); }

        
        private static void ThrowUInt64OverflowException() { throw new OverflowException(ConvertSR.Overflow_UInt64); }

        private static void ThrowInvalidBase() => throw new ArgumentException(ConvertSR.Arg_InvalidBase);
        private static unsafe string ToOctalString(ulong value)
        {
            Span<char> chars = stackalloc char[22]; // max length of a ulong in octal

            int i = chars.Length;
            do
            {
                chars[--i] = (char)('0' + (value & 7));
                value >>= 3;
            }
            while (value != 0);

            return chars.Slice(i).ToString();
        }
    }

    internal static class ParseNumbers
    {
        internal const int TreatAsUnsigned = 0x0200;
        internal const int TreatAsI1 = 0x0400;
        internal const int TreatAsI2 = 0x0800;
        internal const int IsTight = 0x1000;

        public static long StringToLong(ReadOnlySpan<char> s, int radix, int flags)
        {
            int pos = 0;
            return StringToLong(s, radix, flags, ref pos);
        }

        public static long StringToLong(ReadOnlySpan<char> s, int radix, int flags, ref int currPos)
        {
            int i = currPos;

            // Do some radix checking.
            // A radix of -1 says to use whatever base is spec'd on the number.
            // Parse in Base10 until we figure out what the base actually is.
            int r = (-1 == radix) ? 10 : radix;

            if (r != 2 && r != 10 && r != 8 && r != 16)
                throw new ArgumentException(ConvertSR.Arg_InvalidBase, nameof(radix));

            int length = s.Length;

            if (i < 0 || i >= length)
                throw new ArgumentOutOfRangeException(ConvertSR.ArgumentOutOfRange_IndexMustBeLess);

            // Get rid of the whitespace and then check that we've still got some digits to parse.
            if ((flags & IsTight) == 0)
            {
                EatWhiteSpace(s, ref i);
                if (i == length)
                    throw new FormatException(ConvertSR.Format_EmptyInputString);
            }

            // Check for a sign
            int sign = 1;
            if (s[i] == '-')
            {
                if (r != 10)
                    throw new ArgumentException(ConvertSR.Arg_CannotHaveNegativeValue);

                if ((flags & TreatAsUnsigned) != 0)
                    throw new OverflowException(ConvertSR.Overflow_NegativeUnsigned);

                sign = -1;
                i++;
            }
            else if (s[i] == '+')
            {
                i++;
            }

            if ((radix == -1 || radix == 16) && (i + 1 < length) && s[i] == '0')
            {
                if (s[i + 1] == 'x' || s[i + 1] == 'X')
                {
                    r = 16;
                    i += 2;
                }
            }

            int grabNumbersStart = i;
            long result = GrabLongs(r, s, ref i, (flags & TreatAsUnsigned) != 0);

            // Check if they passed us a string with no parsable digits.
            if (i == grabNumbersStart)
                throw new FormatException(ConvertSR.Format_NoParsibleDigits);

            if ((flags & IsTight) != 0)
            {
                // If we've got effluvia left at the end of the string, complain.
                if (i < length)
                    throw new FormatException(ConvertSR.Format_ExtraJunkAtEnd);
            }

            // Put the current index back into the correct place.
            currPos = i;

            // Return the value properly signed.
            if ((ulong)result == 0x8000000000000000 && sign == 1 && r == 10 && ((flags & TreatAsUnsigned) == 0))
                ConvertPort.ThrowOverflow("long");

            if (r == 10)
            {
                result *= sign;
            }

            return result;
        }

        public static int StringToInt(ReadOnlySpan<char> s, int radix, int flags)
        {
            int pos = 0;
            return StringToInt(s, radix, flags, ref pos);
        }

        public static int StringToInt(ReadOnlySpan<char> s, int radix, int flags, ref int currPos)
        {
            // They're requied to tell me where to start parsing.
            int i = currPos;

            // Do some radix checking.
            // A radix of -1 says to use whatever base is spec'd on the number.
            // Parse in Base10 until we figure out what the base actually is.
            int r = (-1 == radix) ? 10 : radix;

            if (r != 2 && r != 10 && r != 8 && r != 16)
                throw new ArgumentException(ConvertSR.Arg_InvalidBase, nameof(radix));

            int length = s.Length;

            if (i < 0 || i >= length)
                throw new ArgumentOutOfRangeException(ConvertSR.ArgumentOutOfRange_IndexMustBeLess);

            // Get rid of the whitespace and then check that we've still got some digits to parse.
            if ((flags & IsTight) == 0)
            {
                EatWhiteSpace(s, ref i);
                if (i == length)
                    throw new FormatException(ConvertSR.Format_EmptyInputString);
            }

            // Check for a sign
            int sign = 1;
            if (s[i] == '-')
            {
                if (r != 10)
                    throw new ArgumentException(ConvertSR.Arg_CannotHaveNegativeValue);

                if ((flags & TreatAsUnsigned) != 0)
                    throw new OverflowException(ConvertSR.Overflow_NegativeUnsigned);

                sign = -1;
                i++;
            }
            else if (s[i] == '+')
            {
                i++;
            }

            // Consume the 0x if we're in an unknown base or in base-16.
            if ((radix == -1 || radix == 16) && (i + 1 < length) && s[i] == '0')
            {
                if (s[i + 1] == 'x' || s[i + 1] == 'X')
                {
                    r = 16;
                    i += 2;
                }
            }

            int grabNumbersStart = i;
            int result = GrabInts(r, s, ref i, (flags & TreatAsUnsigned) != 0);

            // Check if they passed us a string with no parsable digits.
            if (i == grabNumbersStart)
                throw new FormatException(ConvertSR.Format_NoParsibleDigits);

            if ((flags & IsTight) != 0)
            {
                // If we've got effluvia left at the end of the string, complain.
                if (i < length)
                    throw new FormatException(ConvertSR.Format_ExtraJunkAtEnd);
            }

            // Put the current index back into the correct place.
            currPos = i;

            // Return the value properly signed.
            if ((flags & TreatAsI1) != 0)
            {
                if ((uint)result > 0xFF)
                    ConvertPort.ThrowOverflow("sbyte");
            }
            else if ((flags & TreatAsI2) != 0)
            {
                if ((uint)result > 0xFFFF)
                    ConvertPort.ThrowOverflow("short");
            }
            else if ((uint)result == 0x80000000 && sign == 1 && r == 10 && ((flags & TreatAsUnsigned) == 0))
            {
                ConvertPort.ThrowOverflow("int");
            }

            if (r == 10)
            {
                result *= sign;
            }

            return result;
        }

        private static void EatWhiteSpace(ReadOnlySpan<char> s, ref int i)
        {
            int localIndex = i;
            for (; localIndex < s.Length && char.IsWhiteSpace(s[localIndex]); localIndex++) ;
            i = localIndex;
        }

        private static long GrabLongs(int radix, ReadOnlySpan<char> s, ref int i, bool isUnsigned)
        {
            ulong result = 0;
            ulong maxVal;

            // Allow all non-decimal numbers to set the sign bit.
            if (radix == 10 && !isUnsigned)
            {
                maxVal = 0x7FFFFFFFFFFFFFFF / 10;

                // Read all of the digits and convert to a number
                while (i < s.Length && IsDigit(s[i], radix, out int value))
                {
                    // Check for overflows - this is sufficient & correct.
                    if (result > maxVal || ((long)result) < 0)
                    {
                        ConvertPort.ThrowOverflow("long");
                    }

                    result = result * (ulong)radix + (ulong)value;
                    i++;
                }

                if ((long)result < 0 && result != 0x8000000000000000)
                {
                    ConvertPort.ThrowOverflow("long");
                }
            }
            else
            {
                Debug.Assert(radix == 2 || radix == 8 || radix == 10 || radix == 16);
                maxVal =
                    radix == 10 ? 0xffffffffffffffff / 10 :
                    radix == 16 ? 0xffffffffffffffff / 16 :
                    radix == 8 ? 0xffffffffffffffff / 8 :
                    0xffffffffffffffff / 2;

                // Read all of the digits and convert to a number
                while (i < s.Length && IsDigit(s[i], radix, out int value))
                {
                    // Check for overflows - this is sufficient & correct.
                    if (result > maxVal)
                    {
                        ConvertPort.ThrowOverflow("ulong");
                    }

                    ulong temp = result * (ulong)radix + (ulong)value;

                    if (temp < result) // this means overflow as well
                    {
                        ConvertPort.ThrowOverflow("ulong");
                    }

                    result = temp;
                    i++;
                }
            }

            return (long)result;
        }

        private static int GrabInts(int radix, ReadOnlySpan<char> s, ref int i, bool isUnsigned)
        {
            uint result = 0;
            uint maxVal;

            // Allow all non-decimal numbers to set the sign bit.
            if (radix == 10 && !isUnsigned)
            {
                maxVal = (0x7FFFFFFF / 10);

                // Read all of the digits and convert to a number
                while (i < s.Length && IsDigit(s[i], radix, out int value))
                {
                    // Check for overflows - this is sufficient & correct.
                    if (result > maxVal || (int)result < 0)
                    {
                        ConvertPort.ThrowOverflow("int");
                    }
                    result = result * (uint)radix + (uint)value;
                    i++;
                }
                if ((int)result < 0 && result != 0x80000000)
                {
                    ConvertPort.ThrowOverflow("int");
                }
            }
            else
            {
                Debug.Assert(radix == 2 || radix == 8 || radix == 10 || radix == 16);
                maxVal =
                    radix == 10 ? 0xffffffff / 10 :
                    radix == 16 ? 0xffffffff / 16 :
                    radix == 8 ? 0xffffffff / 8 :
                    0xffffffff / 2;

                // Read all of the digits and convert to a number
                while (i < s.Length && IsDigit(s[i], radix, out int value))
                {
                    // Check for overflows - this is sufficient & correct.
                    if (result > maxVal)
                    {
                        ConvertPort.ThrowOverflow("uint");
                    }

                    uint temp = result * (uint)radix + (uint)value;

                    if (temp < result) // this means overflow as well
                    {
                        ConvertPort.ThrowOverflow("uint");
                    }

                    result = temp;
                    i++;
                }
            }

            return (int)result;
        }

        
        private static bool IsDigit(char c, int radix, out int result)
        {
            int tmp;
            if ((uint)(c - '0') <= 9)
            {
                result = tmp = c - '0';
            }
            else if ((uint)(c - 'A') <= 'Z' - 'A')
            {
                result = tmp = c - 'A' + 10;
            }
            else if ((uint)(c - 'a') <= 'z' - 'a')
            {
                result = tmp = c - 'a' + 10;
            }
            else
            {
                result = -1;
                return false;
            }

            return tmp < radix;
        }
    }

    internal static class ConvertSR
    {
        internal const string Arg_CannotHaveNegativeValue = "String cannot contain a minus sign if the base is not 10.";
        internal const string Arg_InvalidBase = "Invalid Base.";
        internal const string ArgumentOutOfRange_IndexMustBeLess = "Index was out of range. Must be non-negative and less than the size of the collection.";
        internal const string Format_EmptyInputString = "Input string was either empty or contained only whitespace.";
        internal const string Format_ExtraJunkAtEnd = "Additional non-parsable characters are at the end of the string.";
        internal const string Format_NeedSingleChar = "String must be exactly one character long.";
        internal const string Format_NoParsibleDigits = "Could not find any recognizable digits.";
        internal const string Overflow_Byte = "Value was either too large or too small for an unsigned byte.";
        internal const string Overflow_Char = "Value was either too large or too small for a character.";
        internal const string Overflow_Int16 = "Value was either too large or too small for an Int16.";
        internal const string Overflow_Int32 = "Value was either too large or too small for an Int32.";
        internal const string Overflow_Int64 = "Value was either too large or too small for an Int64.";
        internal const string Overflow_NegativeUnsigned = "The string was being parsed as an unsigned number and could not have a negative sign.";
        internal const string Overflow_SByte = "Value was either too large or too small for a signed byte.";
        internal const string Overflow_UInt16 = "Value was either too large or too small for a UInt16.";
        internal const string Overflow_UInt32 = "Value was either too large or too small for a UInt32.";
        internal const string Overflow_UInt64 = "Value was either too large or too small for a UInt64.";
    }
}
