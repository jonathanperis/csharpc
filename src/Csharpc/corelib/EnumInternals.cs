// CoreLib-internal members of System.Enum that the framework's own IL calls (issue #191 part B, #220): the IL front-end resolves
// them here because the reference pack does not declare them (IlAssembly.ShadowedMember).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    /// <summary>CoreLib's internal Enum.TryFormatUnconstrained&lt;TEnum&gt;: a span formatter for any enum type.</summary>
    [ShadowMembers("System.Enum")]
    internal static class EnumInternals
    {
        /// <summary>CoreLib's internal Enum.GetValuesAsUnderlyingType(Type): EnumConverter's list of the values.</summary>
        public static Array GetValuesAsUnderlyingType(Type enumType) => EnumRuntime.GetValuesAsUnderlyingType(enumType);

        /// <summary>
        /// Enum.Format(Type, object, string) with a Type known only at run time (System.ComponentModel's EnumConverter): the enum value or
        /// its underlying integer, formatted by Enum.ToString(format) after .NET's checks and messages.
        /// </summary>
        public static string Format(Type enumType, object value, string format)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(enumType);
            if (!enumType.IsEnum) throw new ArgumentException("Type provided must be an Enum.", "enumType");
            Type valueType = value.GetType();
            if (valueType.IsEnum)
            {
                if (valueType != enumType)
                    throw new ArgumentException($"Object must be the same type as the enum. The type passed in was '{valueType}'; the enum type was '{enumType}'.");
                if (format.Length == 1) return ((Enum)value).ToString(format);
            }
            else
            {
                Type underlyingType = enumType.GetEnumUnderlyingType();
                if (valueType != underlyingType)
                    throw new ArgumentException($"Enum underlying type and the object must be same type or object. Type passed in was '{valueType}'; the enum underlying type was '{underlyingType}'.");
                if (format.Length == 1) return ((Enum)Enum.ToObject(enumType, value)).ToString(format);
            }
            throw new FormatException("Format string can be only \"G\", \"g\", \"X\", \"x\", \"F\", \"f\", \"D\" or \"d\".");
        }


        // Enum.TryParse(Type, string, [bool,] out object) with a Type known at run time (#104), as .NET's: a null or non-enum Type throws,
        // a null text or one that names no value (or overflows the underlying type) gives false and null.
        public static bool TryParse(Type enumType, string value, out object result) => TryParse(enumType, value, false, out result);

        public static bool TryParse(Type enumType, string value, bool ignoreCase, out object result)
        {
            ArgumentNullException.ThrowIfNull(enumType);
            if (!enumType.IsEnum) throw new ArgumentException("Type provided must be an Enum.", "enumType");
            result = null;
            if (value == null) return false;
            try { result = EnumRuntime.Parse(enumType, value, ignoreCase); return true; }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
        }

        public static bool TryFormatUnconstrained<TEnum>(TEnum value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default)
        {
            string text = format.IsEmpty ? ((Enum)(object)value).ToString() : ((Enum)(object)value).ToString(format.ToString());
            if (text.Length > destination.Length) { charsWritten = 0; return false; }
            text.AsSpan().CopyTo(destination);
            charsWritten = text.Length;
            return true;
        }
    }
}
