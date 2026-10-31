// System.Runtime.CompilerServices.DefaultInterpolatedStringHandler: what C# builds for an interpolated
// string passed to string.Create(provider, ...) (plain `$"..."` strings are lowered by codegen directly).
// Holes format like .NET's handler: the provider's ICustomFormatter first, then IFormattable with the
// provider (numbers and dates through the corelib's culture-aware formatters), else ToString().
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler")]
    public struct DefaultInterpolatedStringHandlerImpl
    {
        private System.Text.StringBuilder _builder;
        private IFormatProvider _provider;
        private ICustomFormatter _customFormatter;

        public DefaultInterpolatedStringHandlerImpl(int literalLength, int formattedCount)
        {
            _builder = new System.Text.StringBuilder(literalLength + formattedCount * 11);
            _provider = null;
            _customFormatter = null;
        }

        public DefaultInterpolatedStringHandlerImpl(int literalLength, int formattedCount, IFormatProvider provider)
        {
            _builder = new System.Text.StringBuilder(literalLength + formattedCount * 11);
            _provider = provider;
            _customFormatter = provider == null || provider is System.Globalization.CultureInfo ? null
                : provider.GetFormat(typeof(ICustomFormatter)) as ICustomFormatter;
        }

        public DefaultInterpolatedStringHandlerImpl(int literalLength, int formattedCount, IFormatProvider provider, Span<char> initialBuffer)
            : this(literalLength, formattedCount, provider)
        {
        }

        private System.Text.StringBuilder Builder => _builder ??= new System.Text.StringBuilder();

        public override string ToString() => Builder.ToString();

        public string ToStringAndClear()
        {
            string result = Builder.ToString();
            _builder = null;
            return result;
        }

        public void AppendLiteral(string value) => Builder.Append(value);

        private string FormatHole<T>(T value, string format)
        {
            if (_customFormatter != null)
            {
                string custom = _customFormatter.Format(format, CoreIntrinsics.Box(value), _provider);
                if (custom != null) return custom;
            }
            string direct = CoreIntrinsics.FormatValue(value, format, _provider);
            if (direct != null) return direct;
            object boxed = CoreIntrinsics.Box(value);
            if (boxed == null) return "";
            return Number.FormatObjectWith(boxed, format, _provider);
        }

        private void AppendAligned(string text, int alignment)
        {
            int width = alignment < 0 ? -alignment : alignment;
            int padding = width - text.Length;
            if (padding > 0 && alignment > 0) Builder.Append(' ', padding);
            Builder.Append(text);
            if (padding > 0 && alignment < 0) Builder.Append(' ', padding);
        }

        public void AppendFormatted<T>(T value) => Builder.Append(FormatHole(value, null));

        public void AppendFormatted<T>(T value, string format) => Builder.Append(FormatHole(value, format));

        public void AppendFormatted<T>(T value, int alignment) => AppendAligned(FormatHole(value, null), alignment);

        public void AppendFormatted<T>(T value, int alignment, string format) => AppendAligned(FormatHole(value, format), alignment);

        public void AppendFormatted(ReadOnlySpan<char> value) => Builder.Append(value.ToString());

        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string format = null) => AppendAligned(value.ToString(), alignment);

        public void AppendFormatted(string value)
        {
            if (_customFormatter != null) Builder.Append(FormatHole(value, null));
            else Builder.Append(value);
        }

        public void AppendFormatted(string value, int alignment = 0, string format = null) => AppendFormatted<string>(value, alignment, format);

        public void AppendFormatted(object value, int alignment = 0, string format = null) => AppendFormatted<object>(value, alignment, format);
    }

    /// <summary>
    /// StringBuilder.Append(provider, $"..."): codegen represents a StringBuilder.AppendInterpolatedStringHandler by
    /// its builder (FunctionCodeGen.Tuples.cs); one created with a non-null provider is this object instead, and the
    /// handler calls of a function that creates one come here, so the holes format with the provider as .NET's
    /// handler does (its ICustomFormatter first, then IFormattable with the provider, else ToString()).
    /// </summary>
    public sealed class StringBuilderProviderHandler
    {
        private readonly System.Text.StringBuilder _builder;
        private readonly IFormatProvider _provider;
        private readonly ICustomFormatter _customFormatter;

        private StringBuilderProviderHandler(System.Text.StringBuilder builder, IFormatProvider provider)
        {
            _builder = builder;
            _provider = provider;
            _customFormatter = provider is System.Globalization.CultureInfo ? null : provider.GetFormat(typeof(ICustomFormatter)) as ICustomFormatter;
        }

        /// <summary>The handler: the builder itself without a provider (the holes format as without one).</summary>
        public static object Create(System.Text.StringBuilder builder, IFormatProvider provider) =>
            provider == null ? builder : new StringBuilderProviderHandler(builder, provider);

        private static System.Text.StringBuilder Builder(object handler) =>
            handler is StringBuilderProviderHandler h ? h._builder : (System.Text.StringBuilder)handler;

        public static void AppendLiteral(object handler, string value) => Builder(handler).Append(value);

        public static void AppendFormatted<T>(object handler, T value, string format, int alignment)
        {
            var h = handler as StringBuilderProviderHandler;
            string text;
            if (h != null && h._customFormatter != null && h._customFormatter.Format(format, CoreIntrinsics.Box(value), h._provider) is string custom)
                text = custom;
            else
            {
                IFormatProvider provider = h?._provider;
                text = CoreIntrinsics.FormatValue(value, format, provider);
                if (text == null)
                {
                    object boxed = CoreIntrinsics.Box(value);
                    text = boxed == null ? "" : Number.FormatObjectWith(boxed, format, provider);
                }
            }
            Append(Builder(handler), text, alignment);
        }

        // string and ReadOnlySpan<char> holes: appended as they are, unless a custom formatter takes the string.
        public static void AppendString(object handler, string value, string format, int alignment)
        {
            if (handler is StringBuilderProviderHandler { _customFormatter: not null }) AppendFormatted(handler, value, format, alignment);
            else Append(Builder(handler), value ?? "", alignment);
        }

        public static void AppendSpan(object handler, ReadOnlySpan<char> value, int alignment) => Append(Builder(handler), value.ToString(), alignment);

        private static void Append(System.Text.StringBuilder builder, string text, int alignment)
        {
            int width = alignment < 0 ? -alignment : alignment;
            int padding = width - text.Length;
            if (padding > 0 && alignment > 0) builder.Append(' ', padding);
            builder.Append(text);
            if (padding > 0 && alignment < 0) builder.Append(' ', padding);
        }
    }
}
