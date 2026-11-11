// Entry points for compiler-synthesized code (records' and anonymous types' ToString, interpolation), over the framework's own StringBuilder
// (served from System.Private.CoreLib's IL, src/Csharpc/upstream-types.txt; CoreLibRuntime.cs maps the csrt_sb_* names here).
#nullable disable
using System.Text;

namespace CsharpcCoreLib
{
    internal static class StringBuilderRuntime
    {
        internal static StringBuilder RtNew(int capacity) => new StringBuilder(capacity);
        internal static StringBuilder RtAppend(StringBuilder sb, string value) => sb.Append(value);
        internal static string RtToString(StringBuilder sb) => sb.ToString();

        /// <summary>
        /// AppendInterpolatedStringHandler.AppendFormatted with an alignment, as .NET appends it: right-aligned
        /// values get their padding first, left-aligned ones after (two appends, which decide where chunks split).
        /// A null value formats as nothing.
        /// </summary>
        internal static StringBuilder RtAppendAligned(StringBuilder sb, string value, int alignment)
        {
            value ??= string.Empty;
            if (alignment == 0) return sb.Append(value);
            if (alignment < 0)
            {
                sb.Append(value);
                int padding = -alignment - value.Length;
                if (padding > 0) sb.Append(' ', padding);
                return sb;
            }
            int leftPadding = alignment - value.Length;
            if (leftPadding > 0) sb.Append(' ', leftPadding);
            return sb.Append(value);
        }
    }
}
