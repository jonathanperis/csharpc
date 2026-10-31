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
}
