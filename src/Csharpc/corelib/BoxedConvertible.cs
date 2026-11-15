// IConvertible on boxed primitives, decimal, DateTime and enums: the contract between csharpc's box method tables and the
// framework's Convert (served from CoreLib's IL, src/Csharpc/upstream-types/System.Convert.txt).
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    /// <summary>
    /// IConvertible on boxed primitives, decimal and DateTime (ModuleCodeGen.Primitives.cs: each slot is the method of the same name here,
    /// called with the box as its first argument), and on boxed enums (ModuleCodeGen.Bcl.cs). Each member is what the BCL type's own
    /// IConvertible member does: the typed Convert overload for its value (Int32's ToInt64 is Convert.ToInt64(int), DateTime's ToInt32 throws
    /// through Convert.ToInt32(DateTime)). An enum's are its underlying value's, as Enum's IConvertible is.
    /// </summary>
    internal static class BoxedConvertible
    {
        public static TypeCode GetTypeCode(object self) => self switch
        {
            Enum => Type.GetTypeCode(self.GetType()),
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
            _ => TypeCode.Object,
        };

        // A boxed enum's underlying value, boxed as that type (from its "D" format: csharpc's enum boxes implement IFormattable).
        private static object Underlying(object self)
        {
            if (self is not Enum) return self;
            string digits = ((IFormattable)self).ToString("D", CultureInfo.InvariantCulture);
            return Type.GetTypeCode(self.GetType()) switch
            {
                TypeCode.SByte => sbyte.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                TypeCode.Byte => byte.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
                TypeCode.Int16 => short.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                TypeCode.UInt16 => ushort.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
                TypeCode.Int32 => int.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                TypeCode.UInt32 => uint.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
                TypeCode.Int64 => long.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                TypeCode.UInt64 => ulong.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
                TypeCode.Boolean => digits != "0",
                TypeCode.Char => (char)ushort.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException("Unknown enum type."),
            };
        }

        // A conversion the source type's IConvertible refuses (Char's ToBoolean, DateTime's ToInt32...): the typed Convert overloads call that
        // IConvertible member themselves, so the exception is thrown here, with .NET's message.
        private static InvalidCastException Invalid(string from, string to) => new InvalidCastException("Invalid cast from '" + from + "' to '" + to + "'.");

        private static InvalidCastException NotConvertible(object self) =>
            new InvalidCastException("Unable to cast object of type '" + self.GetType().FullName + "' to type 'System.IConvertible'.");

        public static bool ToBoolean(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => v, char => throw Invalid("Char", "Boolean"), sbyte v => Convert.ToBoolean(v), byte v => Convert.ToBoolean(v), short v => Convert.ToBoolean(v),
            ushort v => Convert.ToBoolean(v), int v => Convert.ToBoolean(v), uint v => Convert.ToBoolean(v), long v => Convert.ToBoolean(v),
            ulong v => Convert.ToBoolean(v), float v => Convert.ToBoolean(v), double v => Convert.ToBoolean(v), decimal v => Convert.ToBoolean(v),
            DateTime => throw Invalid("DateTime", "Boolean"), _ => throw NotConvertible(self),
        };

        public static char ToChar(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool => throw Invalid("Boolean", "Char"), char v => v, sbyte v => Convert.ToChar(v), byte v => Convert.ToChar(v), short v => Convert.ToChar(v),
            ushort v => Convert.ToChar(v), int v => Convert.ToChar(v), uint v => Convert.ToChar(v), long v => Convert.ToChar(v),
            ulong v => Convert.ToChar(v), float => throw Invalid("Single", "Char"), double => throw Invalid("Double", "Char"), decimal => throw Invalid("Decimal", "Char"),
            DateTime => throw Invalid("DateTime", "Char"), _ => throw NotConvertible(self),
        };

        public static sbyte ToSByte(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToSByte(v), char v => Convert.ToSByte(v), sbyte v => v, byte v => Convert.ToSByte(v), short v => Convert.ToSByte(v),
            ushort v => Convert.ToSByte(v), int v => Convert.ToSByte(v), uint v => Convert.ToSByte(v), long v => Convert.ToSByte(v),
            ulong v => Convert.ToSByte(v), float v => Convert.ToSByte(v), double v => Convert.ToSByte(v), decimal v => Convert.ToSByte(v),
            DateTime => throw Invalid("DateTime", "SByte"), _ => throw NotConvertible(self),
        };

        public static byte ToByte(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToByte(v), char v => Convert.ToByte(v), sbyte v => Convert.ToByte(v), byte v => v, short v => Convert.ToByte(v),
            ushort v => Convert.ToByte(v), int v => Convert.ToByte(v), uint v => Convert.ToByte(v), long v => Convert.ToByte(v),
            ulong v => Convert.ToByte(v), float v => Convert.ToByte(v), double v => Convert.ToByte(v), decimal v => Convert.ToByte(v),
            DateTime => throw Invalid("DateTime", "Byte"), _ => throw NotConvertible(self),
        };

        public static short ToInt16(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToInt16(v), char v => Convert.ToInt16(v), sbyte v => Convert.ToInt16(v), byte v => Convert.ToInt16(v), short v => v,
            ushort v => Convert.ToInt16(v), int v => Convert.ToInt16(v), uint v => Convert.ToInt16(v), long v => Convert.ToInt16(v),
            ulong v => Convert.ToInt16(v), float v => Convert.ToInt16(v), double v => Convert.ToInt16(v), decimal v => Convert.ToInt16(v),
            DateTime => throw Invalid("DateTime", "Int16"), _ => throw NotConvertible(self),
        };

        public static ushort ToUInt16(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToUInt16(v), char v => Convert.ToUInt16(v), sbyte v => Convert.ToUInt16(v), byte v => Convert.ToUInt16(v), short v => Convert.ToUInt16(v),
            ushort v => v, int v => Convert.ToUInt16(v), uint v => Convert.ToUInt16(v), long v => Convert.ToUInt16(v),
            ulong v => Convert.ToUInt16(v), float v => Convert.ToUInt16(v), double v => Convert.ToUInt16(v), decimal v => Convert.ToUInt16(v),
            DateTime => throw Invalid("DateTime", "UInt16"), _ => throw NotConvertible(self),
        };

        public static int ToInt32(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToInt32(v), char v => Convert.ToInt32(v), sbyte v => Convert.ToInt32(v), byte v => Convert.ToInt32(v), short v => Convert.ToInt32(v),
            ushort v => Convert.ToInt32(v), int v => v, uint v => Convert.ToInt32(v), long v => Convert.ToInt32(v),
            ulong v => Convert.ToInt32(v), float v => Convert.ToInt32(v), double v => Convert.ToInt32(v), decimal v => Convert.ToInt32(v),
            DateTime => throw Invalid("DateTime", "Int32"), _ => throw NotConvertible(self),
        };

        public static uint ToUInt32(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToUInt32(v), char v => Convert.ToUInt32(v), sbyte v => Convert.ToUInt32(v), byte v => Convert.ToUInt32(v), short v => Convert.ToUInt32(v),
            ushort v => Convert.ToUInt32(v), int v => Convert.ToUInt32(v), uint v => v, long v => Convert.ToUInt32(v),
            ulong v => Convert.ToUInt32(v), float v => Convert.ToUInt32(v), double v => Convert.ToUInt32(v), decimal v => Convert.ToUInt32(v),
            DateTime => throw Invalid("DateTime", "UInt32"), _ => throw NotConvertible(self),
        };

        public static long ToInt64(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToInt64(v), char v => Convert.ToInt64(v), sbyte v => Convert.ToInt64(v), byte v => Convert.ToInt64(v), short v => Convert.ToInt64(v),
            ushort v => Convert.ToInt64(v), int v => Convert.ToInt64(v), uint v => Convert.ToInt64(v), long v => v,
            ulong v => Convert.ToInt64(v), float v => Convert.ToInt64(v), double v => Convert.ToInt64(v), decimal v => Convert.ToInt64(v),
            DateTime => throw Invalid("DateTime", "Int64"), _ => throw NotConvertible(self),
        };

        public static ulong ToUInt64(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToUInt64(v), char v => Convert.ToUInt64(v), sbyte v => Convert.ToUInt64(v), byte v => Convert.ToUInt64(v), short v => Convert.ToUInt64(v),
            ushort v => Convert.ToUInt64(v), int v => Convert.ToUInt64(v), uint v => Convert.ToUInt64(v), long v => Convert.ToUInt64(v),
            ulong v => v, float v => Convert.ToUInt64(v), double v => Convert.ToUInt64(v), decimal v => Convert.ToUInt64(v),
            DateTime => throw Invalid("DateTime", "UInt64"), _ => throw NotConvertible(self),
        };

        public static float ToSingle(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToSingle(v), char => throw Invalid("Char", "Single"), sbyte v => Convert.ToSingle(v), byte v => Convert.ToSingle(v), short v => Convert.ToSingle(v),
            ushort v => Convert.ToSingle(v), int v => Convert.ToSingle(v), uint v => Convert.ToSingle(v), long v => Convert.ToSingle(v),
            ulong v => Convert.ToSingle(v), float v => v, double v => Convert.ToSingle(v), decimal v => Convert.ToSingle(v),
            DateTime => throw Invalid("DateTime", "Single"), _ => throw NotConvertible(self),
        };

        public static double ToDouble(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToDouble(v), char => throw Invalid("Char", "Double"), sbyte v => Convert.ToDouble(v), byte v => Convert.ToDouble(v), short v => Convert.ToDouble(v),
            ushort v => Convert.ToDouble(v), int v => Convert.ToDouble(v), uint v => Convert.ToDouble(v), long v => Convert.ToDouble(v),
            ulong v => Convert.ToDouble(v), float v => Convert.ToDouble(v), double v => v, decimal v => Convert.ToDouble(v),
            DateTime => throw Invalid("DateTime", "Double"), _ => throw NotConvertible(self),
        };

        public static decimal ToDecimal(object self, IFormatProvider provider) => Underlying(self) switch
        {
            bool v => Convert.ToDecimal(v), char => throw Invalid("Char", "Decimal"), sbyte v => Convert.ToDecimal(v), byte v => Convert.ToDecimal(v), short v => Convert.ToDecimal(v),
            ushort v => Convert.ToDecimal(v), int v => Convert.ToDecimal(v), uint v => Convert.ToDecimal(v), long v => Convert.ToDecimal(v),
            ulong v => Convert.ToDecimal(v), float v => Convert.ToDecimal(v), double v => Convert.ToDecimal(v), decimal v => v,
            DateTime => throw Invalid("DateTime", "Decimal"), _ => throw NotConvertible(self),
        };

        // Enum's IConvertible.ToDateTime throws InvalidCast naming Enum, as .NET's does.
        public static DateTime ToDateTime(object self, IFormatProvider provider) => self is Enum ? throw new InvalidCastException("Invalid cast from 'Enum' to 'DateTime'.") : self switch
        {
            bool => throw Invalid("Boolean", "DateTime"), char => throw Invalid("Char", "DateTime"), sbyte => throw Invalid("SByte", "DateTime"), byte => throw Invalid("Byte", "DateTime"), short => throw Invalid("Int16", "DateTime"),
            ushort => throw Invalid("UInt16", "DateTime"), int => throw Invalid("Int32", "DateTime"), uint => throw Invalid("UInt32", "DateTime"), long => throw Invalid("Int64", "DateTime"),
            ulong => throw Invalid("UInt64", "DateTime"), float => throw Invalid("Single", "DateTime"), double => throw Invalid("Double", "DateTime"), decimal => throw Invalid("Decimal", "DateTime"),
            DateTime v => v, _ => throw NotConvertible(self),
        };

        // IFormattable.ToString(null, provider): bool and char ignore the provider, as their IConvertible.ToString does; an enum's is its name.
        public static string ToString(object self, IFormatProvider provider) => self switch
        {
            Enum => self.ToString(),
            bool v => v.ToString(), char v => v.ToString(), sbyte v => v.ToString(provider), byte v => v.ToString(provider), short v => v.ToString(provider),
            ushort v => v.ToString(provider), int v => v.ToString(provider), uint v => v.ToString(provider), long v => v.ToString(provider),
            ulong v => v.ToString(provider), float v => v.ToString(provider), double v => v.ToString(provider), decimal v => v.ToString(provider),
            DateTime v => v.ToString(provider), _ => self.ToString(),
        };

        /// <summary>
        /// IConvertible.ToType of the BCL's types: Convert.DefaultToType (CoreLib-internal), for the type itself, the convertible types,
        /// string, object and Enum, else InvalidCastException. Convert.ChangeType calls this for a target it doesn't list itself.
        /// </summary>
        public static object ToType(object self, Type conversionType, IFormatProvider provider)
        {
            ArgumentNullException.ThrowIfNull(conversionType, "targetType");
            if (ReferenceEquals(self.GetType(), conversionType)) return self;
            if (ReferenceEquals(conversionType, typeof(bool))) return ToBoolean(self, provider);
            if (ReferenceEquals(conversionType, typeof(char))) return ToChar(self, provider);
            if (ReferenceEquals(conversionType, typeof(sbyte))) return ToSByte(self, provider);
            if (ReferenceEquals(conversionType, typeof(byte))) return ToByte(self, provider);
            if (ReferenceEquals(conversionType, typeof(short))) return ToInt16(self, provider);
            if (ReferenceEquals(conversionType, typeof(ushort))) return ToUInt16(self, provider);
            if (ReferenceEquals(conversionType, typeof(int))) return ToInt32(self, provider);
            if (ReferenceEquals(conversionType, typeof(uint))) return ToUInt32(self, provider);
            if (ReferenceEquals(conversionType, typeof(long))) return ToInt64(self, provider);
            if (ReferenceEquals(conversionType, typeof(ulong))) return ToUInt64(self, provider);
            if (ReferenceEquals(conversionType, typeof(float))) return ToSingle(self, provider);
            if (ReferenceEquals(conversionType, typeof(double))) return ToDouble(self, provider);
            if (ReferenceEquals(conversionType, typeof(decimal))) return ToDecimal(self, provider);
            if (ReferenceEquals(conversionType, typeof(DateTime))) return ToDateTime(self, provider);
            if (ReferenceEquals(conversionType, typeof(string))) return ToString(self, provider);
            if (ReferenceEquals(conversionType, typeof(object))) return self;
            if (ReferenceEquals(conversionType, typeof(Enum))) return (Enum)self;
            if (ReferenceEquals(conversionType, typeof(DBNull))) throw new InvalidCastException("Object cannot be cast to DBNull.");
            throw new InvalidCastException("Invalid cast from '" + self.GetType().FullName + "' to '" + conversionType.FullName + "'.");
        }
    }
}
