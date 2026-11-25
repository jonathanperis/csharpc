// StringBuilder.Append(provider, $"...") with a provider (StringBuilderProviderHandler below). DefaultInterpolatedStringHandler
// itself comes from CoreLib's IL (upstream-types/System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.txt, #466).
#nullable disable
using System;

namespace CsharpcCoreLib
{
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
