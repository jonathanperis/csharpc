// ISpanFormattable / IUtf8SpanFormattable on boxed built-in values (issue #221). The framework's IL reaches them through the interface on a
// box (`box T; isinst ISpanFormattable; castclass ISpanFormattable; callvirt TryFormat`, StringBuilder's interpolation handler, string.Format,
// DefaultInterpolatedStringHandler): the slot of the boxed primitive's table is one of these, receiver first, as BoxedConvertible's are.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class BoxedSpanFormattable
    {
        // The slot of one boxed type (ModuleCodeGen.PrimitiveSlot instantiates it per primitive): only that type's formatter, so a program that
        // boxes an int does not carry the decimal, DateTime, TimeSpan and Int128 formatters the switch below reaches.
        public static bool TryFormatOf<T>(object self, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider)
            where T : ISpanFormattable
        {
            if (typeof(T) == typeof(char)) return CharTryFormat.TryFormat((char)self, destination, out charsWritten, format, provider);
            // Every other boxed built-in value (the numbers, decimal, Int128/UInt128, DateTime, TimeSpan, BigInteger): the text of its
            // IFormattable thunk, if it fits. It is the same text (.NET's TryFormat and ToString agree for them), and the thunk is linked
            // anyway, while the type's own TryFormat would add a second formatter per boxed type to every program whose composite
            // formatting or key messages test ISpanFormattable (Number.AppendCompositeItem).
            return CopyFormatted((IFormattable)self, destination, out charsWritten, format, provider);
        }

        public static bool TryFormatOf<T>(object self, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider provider)
            where T : IUtf8SpanFormattable
        {
            if (typeof(T) == typeof(char)) return CharTryFormat.TryFormat((char)self, utf8Destination, out bytesWritten, format, provider);
            if (typeof(T) == typeof(System.Numerics.BigInteger))
            {
                string text = ((IFormattable)self).ToString(format.IsEmpty ? null : format.ToString(), provider);
                return System.Text.Encoding.UTF8.TryGetBytes(text, utf8Destination, out bytesWritten);
            }
            return CoreIntrinsics.Unbox<T>(self).TryFormat(utf8Destination, out bytesWritten, format, provider);
        }

        private static bool CopyFormatted(IFormattable self, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider)
        {
            string text = self.ToString(format.IsEmpty ? null : format.ToString(), provider);
            if (text.AsSpan().TryCopyTo(destination))
            {
                charsWritten = text.Length;
                return true;
            }
            charsWritten = 0;
            return false;
        }

        public static bool TryFormat(object self, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider)
        {
            switch (self)
            {
                case char v: return CharTryFormat.TryFormat(v, destination, out charsWritten, format, provider);
                case sbyte v: return v.TryFormat(destination, out charsWritten, format, provider);
                case byte v: return v.TryFormat(destination, out charsWritten, format, provider);
                case short v: return v.TryFormat(destination, out charsWritten, format, provider);
                case ushort v: return v.TryFormat(destination, out charsWritten, format, provider);
                case int v: return v.TryFormat(destination, out charsWritten, format, provider);
                case uint v: return v.TryFormat(destination, out charsWritten, format, provider);
                case long v: return v.TryFormat(destination, out charsWritten, format, provider);
                case ulong v: return v.TryFormat(destination, out charsWritten, format, provider);
                case float v: return v.TryFormat(destination, out charsWritten, format, provider);
                case double v: return v.TryFormat(destination, out charsWritten, format, provider);
                case decimal v: return v.TryFormat(destination, out charsWritten, format, provider);
                case DateTime v: return v.TryFormat(destination, out charsWritten, format, provider);
                case TimeSpan v: return v.TryFormat(destination, out charsWritten, format, provider);
                case Int128 v: return Int128TryFormat(v, destination, out charsWritten, format, provider);
                case UInt128 v: return UInt128TryFormat(v, destination, out charsWritten, format, provider);
            }
            throw new InvalidCastException();
        }

        // Int128's and UInt128's TryFormat are the framework's own (hosted from their IL, #426).
        private static bool Int128TryFormat(Int128 v, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider) =>
            v.TryFormat(destination, out charsWritten, format, provider);

        private static bool UInt128TryFormat(UInt128 v, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider) =>
            v.TryFormat(destination, out charsWritten, format, provider);

        public static bool TryFormat(object self, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider provider)
        {
            switch (self)
            {
                case char v: return CharTryFormat.TryFormat(v, utf8Destination, out bytesWritten, format, provider);
                case sbyte v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case byte v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case short v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case ushort v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case int v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case uint v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case long v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case ulong v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case float v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case double v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case decimal v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case DateTime v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case TimeSpan v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case Int128 v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
                case UInt128 v: return v.TryFormat(utf8Destination, out bytesWritten, format, provider);
            }
            throw new InvalidCastException();
        }
    }

    /// <summary>ISpanFormattable.TryFormat of a boxed enum (System.Enum implements it): the text IFormattable.ToString gives, if it fits.</summary>
    internal static class BoxedEnumSpanFormattable
    {
        public static bool TryFormat(object self, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider)
        {
            string text = ((IFormattable)self).ToString(format.IsEmpty ? null : format.ToString(), provider);
            if (text.AsSpan().TryCopyTo(destination))
            {
                charsWritten = text.Length;
                return true;
            }
            charsWritten = 0;
            return false;
        }
    }
}
