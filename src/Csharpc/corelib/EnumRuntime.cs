// Enum.Parse/IsDefined(Type, ...) when the Type is not a compile-time typeof(enum): Configuration.Binder
// reaches these from BindDictionary even for programs that only bind strings. Throw until a run-time
// enum member table exists; those programs never call the helpers.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class EnumRuntime
    {
        public static object Parse(Type enumType, string value, bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(enumType);
            ArgumentNullException.ThrowIfNull(value);
            throw new NotSupportedException(
                "Enum.Parse(Type, string) with a Type that is not typeof(an enum) is not supported by csharpc yet.");
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
