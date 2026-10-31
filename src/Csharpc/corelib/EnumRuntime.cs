// Enum.Parse/IsDefined(Type, ...) when the Type is not a compile-time typeof(enum): Configuration.Binder
// reaches these from BindDictionary even for programs that only bind strings. Throw until a run-time
// enum member table exists; those programs never call the helpers.
#nullable disable
using System;

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

        public static bool IsDefined(Type enumType, object value)
        {
            ArgumentNullException.ThrowIfNull(enumType);
            ArgumentNullException.ThrowIfNull(value);
            throw new NotSupportedException(
                "Enum.IsDefined(Type, object) with a Type that is not typeof(an enum) is not supported by csharpc yet.");
        }
    }
}
