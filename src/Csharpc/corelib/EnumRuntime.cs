// Enum.Parse/IsDefined(Type, ...) when the Type is not a compile-time typeof(enum): Configuration.Binder
// reaches these from BindDictionary even for programs that only bind strings. Throw until a run-time
// enum member table exists; those programs never call the helpers.
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    internal static class EnumRuntime
    {
        /// <summary>Enum.Parse/TryParse of a ReadOnlySpan&lt;char&gt;: the same rules as for the string with those chars.</summary>
        public static string SpanText(ReadOnlySpan<char> value) => new string(value);

        // Enum.Parse / Enum.TryParse over the compiler-emitted name/value tables (ModuleCodeGen.EnumTable: values as unsigned 64-bit numbers
        // sorted as .NET's EnumInfo sorts them, names as strings), as .NET's Enum.TryParseByValueOrName: white space (char.IsWhiteSpace) is
        // trimmed from the start; a text starting with a digit or sign is an integer of the underlying type (invariant, trailing white space
        // allowed; parsed by the framework's own integer parser), and only when it is not a well-formed integer a name or a comma-separated list
        // of names, each trimmed, OR-ed together. Too large or too small for the underlying type is an OverflowException, not "not found".
        private const NumberStyles EnumNumberStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingWhite;
        private const int ParseOk = 1, ParseNotFound = 0, ParseEmpty = 3, ParseOverflow = 2;

        private static unsafe int ParseCore(ReadOnlySpan<char> value, bool ignoreCase, ulong* values, nint* names, int count, int bits, bool isSigned,
            out long result)
        {
            result = 0;
            value = value.TrimStart();
            if (value.IsEmpty) return ParseEmpty;
            char c = value[0];
            if (char.IsAsciiDigit(c) || c == '-' || c == '+')
            {
                if (isSigned)
                {
                    if (long.TryParse(value, EnumNumberStyles, NumberFormatInfo.InvariantInfo, out long signedValue))
                    {
                        if (bits < 64 && (signedValue < -(1L << (bits - 1)) || signedValue > (1L << (bits - 1)) - 1)) return ParseOverflow;
                        result = signedValue;
                        return ParseOk;
                    }
                }
                else if (ulong.TryParse(value, EnumNumberStyles, NumberFormatInfo.InvariantInfo, out ulong unsignedValue))
                {
                    if (bits < 64 && unsignedValue > (1UL << bits) - 1) return ParseOverflow;
                    result = (long)unsignedValue;
                    return ParseOk;
                }
                // The framework's parser said no: a well-formed integer was out of range; anything else may still be names.
                if (IsIntegerText(value)) return ParseOverflow;
            }
            ulong accumulated = 0;
            while (true)
            {
                int comma = value.IndexOf(',');
                if (comma == value.Length - 1) return ParseNotFound;  // a trailing separator
                ReadOnlySpan<char> part = (comma < 0 ? value : value.Slice(0, comma)).Trim();
                bool found = false;
                for (int i = 0; i < count; i++)
                {
                    string name = (string)CoreIntrinsics.PointerToObject(names[i]);
                    if (ignoreCase ? part.Equals(name, StringComparison.OrdinalIgnoreCase) : part.SequenceEqual(name))
                    {
                        accumulated |= values[i];
                        found = true;
                        break;
                    }
                }
                if (!found) return ParseNotFound;
                if (comma < 0) break;
                value = value.Slice(comma + 1);
            }
            result = (long)accumulated;
            return ParseOk;
        }

        // What Number.TryParseBinaryIntegerStyle accepts with EnumNumberStyles (after the leading white space is gone), whatever the value:
        // a sign, ASCII digits, white space (U+0009..U+000D, U+0020), then only NULs.
        private static bool IsIntegerText(ReadOnlySpan<char> value)
        {
            int i = 0;
            if (value[0] == '-' || value[0] == '+') i++;
            int digits = i;
            while (i < value.Length && char.IsAsciiDigit(value[i])) i++;
            if (i == digits) return false;
            while (i < value.Length && (value[i] == ' ' || (uint)(value[i] - '\t') <= '\r' - '\t')) i++;
            while (i < value.Length && value[i] == '\0') i++;
            return i == value.Length;
        }

        /// <summary>Enum.TryParse&lt;TEnum&gt;(text, [ignoreCase,] out value) (FunctionCodeGen.Enums): false for null, empty, unknown or out of range.</summary>
        public static unsafe bool TryParseTable(string value, bool ignoreCase, ulong* values, nint* names, int count, int bits, bool isSigned, long* result)
        {
            *result = 0;
            if (value == null) return false;
            if (ParseCore(value, ignoreCase, values, names, count, bits, isSigned, out long parsed) != ParseOk) return false;
            *result = parsed;
            return true;
        }

        /// <summary>Enum.Parse(text[, ignoreCase]) and Enum.Parse(typeof(TEnum), ...): the value, or .NET's exception.</summary>
        public static unsafe long ParseTable(string value, bool ignoreCase, ulong* values, nint* names, int count, int bits, bool isSigned)
        {
            ArgumentNullException.ThrowIfNull(value);
            switch (ParseCore(value, ignoreCase, values, names, count, bits, isSigned, out long parsed))
            {
                case ParseOk: return parsed;
                case ParseEmpty: throw new ArgumentException("Must specify valid information for parsing in the string.", "value");
                case ParseOverflow: throw new OverflowException(OverflowMessage(bits, isSigned));
                default: throw new ArgumentException("Requested value '" + value + "' was not found.");
            }
        }

        // Number.ThrowOverflowException<TUnderlying>'s texts (SR.Overflow_*).
        private static string OverflowMessage(int bits, bool isSigned) => "Value was either too large or too small for " + (bits, isSigned) switch
        {
            (8, true) => "a signed byte.",
            (8, false) => "an unsigned byte.",
            (16, true) => "an Int16.",
            (16, false) => "a UInt16.",
            (32, true) => "an Int32.",
            (32, false) => "a UInt32.",
            (64, true) => "an Int64.",
            _ => "a UInt64.",
        };

        /// <summary>
        /// Enum.Parse(Type, string, bool) with a Type known only at run time: the enum's names and values from the
        /// table of the enums whose method tables the program has (any Type object an enum can have).
        /// </summary>
        public static unsafe object Parse(Type enumType, string value, bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(enumType);
            if (!enumType.IsEnum) throw new ArgumentException("Type provided must be an Enum.", "enumType");
            byte* table = (byte*)CoreIntrinsics.EnumParseTableAddress();
            long count = *(long*)table;
            byte* entries = (byte*)*(nint*)(table + 8);
            nint mt = CoreIntrinsics.csrt_type_handle(enumType);
            // Entries: { mt, values, names, i32 count, i32 bits, i32 signed, i32 } (pointer-sized pointers).
            int p = IntPtr.Size, size = 3 * p + 16;
            for (long i = 0; i < count; i++)
            {
                byte* entry = entries + i * size;
                if (*(nint*)entry != mt) continue;
                long parsed = ParseTable(value, ignoreCase, (ulong*)*(nint*)(entry + p), (nint*)*(nint*)(entry + 2 * p),
                    *(int*)(entry + 3 * p), *(int*)(entry + 3 * p + 4), *(int*)(entry + 3 * p + 8) != 0);
                return Enum.ToObject(enumType, parsed);
            }
            throw new NotSupportedException("Enum.Parse of '" + enumType + "' (a Type known only at run time) is not supported by csharpc yet.");
        }

        /// <summary>The table entry of an enum whose Type object can exist: its names, values and layout (see Parse).</summary>
        private static unsafe byte* Entry(Type enumType, out int count)
        {
            byte* table = (byte*)CoreIntrinsics.EnumParseTableAddress();
            long total = *(long*)table;
            byte* entries = (byte*)*(nint*)(table + 8);
            nint mt = CoreIntrinsics.csrt_type_handle(enumType);
            int p = IntPtr.Size, size = 3 * p + 16;
            for (long i = 0; i < total; i++)
            {
                byte* entry = entries + i * size;
                if (*(nint*)entry != mt) continue;
                count = *(int*)(entry + 3 * p);
                return entry;
            }
            count = 0;
            return null;
        }

        /// <summary>Whether the enum has [Flags] (the table's last field, set by FinishEnumParseTable).</summary>
        public static unsafe bool IsFlags(Type enumType)
        {
            byte* entry = Entry(enumType, out _);
            if (entry == null) throw new NotSupportedException("Whether '" + enumType + "' is a [Flags] enum (a Type known only at run time) is not supported by csharpc yet.");
            return *(int*)(entry + 3 * IntPtr.Size + 12) != 0;
        }

        /// <summary>
        /// Enum.GetValuesAsUnderlyingType(Type) with a Type known only at run time (EnumConverter): the values in ascending order, as an array of
        /// the underlying type.
        /// </summary>
        public static unsafe Array GetValuesAsUnderlyingType(Type enumType)
        {
            ArgumentNullException.ThrowIfNull(enumType);
            if (!enumType.IsEnum) throw new ArgumentException("Type provided must be an Enum.", "enumType");
            byte* entry = Entry(enumType, out int count);
            if (entry == null)
                throw new NotSupportedException("Enum.GetValues of '" + enumType + "' (a Type known only at run time) is not supported by csharpc yet.");
            int p = IntPtr.Size;
            ulong* values = (ulong*)*(nint*)(entry + p);
            int bits = *(int*)(entry + 3 * p + 4);
            bool signed = *(int*)(entry + 3 * p + 8) != 0;
            // Each value as a sign- or zero-extended 64-bit number, so that one ordering sorts them.
            long[] raw = new long[count];
            for (int i = 0; i < count; i++)
            {
                ulong v = bits >= 64 ? values[i] : values[i] & ((1UL << bits) - 1);
                raw[i] = signed && bits < 64 && (v & (1UL << (bits - 1))) != 0 ? (long)(v | (ulong.MaxValue << bits)) : (long)v;
            }
            if (signed) Array.Sort(raw);
            else { ulong[] unsignedValues = new ulong[count]; for (int i = 0; i < count; i++) unsignedValues[i] = (ulong)raw[i]; Array.Sort(unsignedValues); for (int i = 0; i < count; i++) raw[i] = (long)unsignedValues[i]; }
            Type underlying = Enum.GetUnderlyingType(enumType);
            if (underlying == typeof(int)) { var a = new int[count]; for (int i = 0; i < count; i++) a[i] = (int)raw[i]; return a; }
            if (underlying == typeof(uint)) { var a = new uint[count]; for (int i = 0; i < count; i++) a[i] = (uint)raw[i]; return a; }
            if (underlying == typeof(long)) { var a = new long[count]; for (int i = 0; i < count; i++) a[i] = raw[i]; return a; }
            if (underlying == typeof(ulong)) { var a = new ulong[count]; for (int i = 0; i < count; i++) a[i] = (ulong)raw[i]; return a; }
            if (underlying == typeof(short)) { var a = new short[count]; for (int i = 0; i < count; i++) a[i] = (short)raw[i]; return a; }
            if (underlying == typeof(ushort)) { var a = new ushort[count]; for (int i = 0; i < count; i++) a[i] = (ushort)raw[i]; return a; }
            if (underlying == typeof(sbyte)) { var a = new sbyte[count]; for (int i = 0; i < count; i++) a[i] = (sbyte)raw[i]; return a; }
            var bytes = new byte[count];
            for (int i = 0; i < count; i++) bytes[i] = (byte)raw[i];
            return bytes;
        }

        /// <summary>Enum.IsDefined(Type, object) with a Type known only at run time: a name (string) or a value of the enum or its underlying type.</summary>
        public static unsafe bool IsDefined(Type enumType, object value)
        {
            ArgumentNullException.ThrowIfNull(enumType);
            ArgumentNullException.ThrowIfNull(value);
            if (!enumType.IsEnum) throw new ArgumentException("Type provided must be an Enum.", "enumType");
            byte* entry = Entry(enumType, out int count);
            if (entry == null)
                throw new NotSupportedException("Enum.IsDefined of '" + enumType + "' (a Type known only at run time) is not supported by csharpc yet.");
            int p = IntPtr.Size;
            ulong* values = (ulong*)*(nint*)(entry + p);
            nint* names = (nint*)*(nint*)(entry + 2 * p);
            int bits = *(int*)(entry + 3 * p + 4);
            if (value is string text)
            {
                for (int i = 0; i < count; i++)
                    if (string.Equals(CoreIntrinsics.PointerToObject(names[i]) as string, text, StringComparison.Ordinal)) return true;
                return false;
            }
            Type valueType = value.GetType();
            Type underlying = Enum.GetUnderlyingType(enumType);
            if (valueType.IsEnum)
            {
                if (valueType != enumType)
                    throw new ArgumentException("Object must be the same type as the enum. The type passed in was '" + valueType + "'; the enum type was '" + enumType + "'.");
            }
            else if (valueType != underlying)
            {
                throw new ArgumentException("Enum underlying type and the object must be the same type or object must be a String. Type passed in was '" + valueType + "'; the enum underlying type was '" + underlying + "'.");
            }
            ulong raw = underlying == typeof(ulong) || underlying == typeof(uint) || underlying == typeof(ushort) || underlying == typeof(byte)
                ? Convert.ToUInt64(value, CultureInfo.InvariantCulture) : unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture));
            ulong mask = bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;
            for (int i = 0; i < count; i++)
                if ((values[i] & mask) == (raw & mask)) return true;
            return false;
        }
    }
}
