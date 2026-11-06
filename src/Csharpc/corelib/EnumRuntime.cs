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
                long parsed = CoreIntrinsics.csrt_enum_parse_or_throw(value, ignoreCase, (void*)*(nint*)(entry + p), (void*)*(nint*)(entry + 2 * p),
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
