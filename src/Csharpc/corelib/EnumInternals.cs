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
