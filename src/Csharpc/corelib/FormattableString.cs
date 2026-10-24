// System.FormattableString and FormattableStringFactory (what C# converts `$"..."` to when the target type
// is FormattableString or IFormattable): a composite format string and its arguments, formatted with
// string.Format's engine (Number.FormatCompositeWith) and the given provider, as in .NET.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.FormattableString")]
    public abstract class FormattableStringImpl : IFormattable
    {
        public abstract string Format { get; }

        public abstract object[] GetArguments();

        public abstract int ArgumentCount { get; }

        public abstract object GetArgument(int index);

        public abstract string ToString(IFormatProvider formatProvider);

        string IFormattable.ToString(string ignored, IFormatProvider formatProvider) => ToString(formatProvider);

        public static string Invariant(FormattableString formattable)
        {
            if (formattable == null) throw new ArgumentNullException(nameof(formattable));
            return formattable.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string CurrentCulture(FormattableString formattable)
        {
            if (formattable == null) throw new ArgumentNullException(nameof(formattable));
            return formattable.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }

        public override string ToString() => ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    [Shadows("System.Runtime.CompilerServices.FormattableStringFactory")]
    public static class FormattableStringFactoryImpl
    {
        public static FormattableString Create(string format, params object[] arguments)
        {
            if (format == null) throw new ArgumentNullException(nameof(format));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            return (FormattableString)(object)new ConcreteFormattableString(format, arguments);
        }

        private sealed class ConcreteFormattableString : FormattableStringImpl
        {
            private readonly string _format;
            private readonly object[] _arguments;

            internal ConcreteFormattableString(string format, object[] arguments)
            {
                _format = format;
                _arguments = arguments;
            }

            public override string Format => _format;
            public override object[] GetArguments() => _arguments;
            public override int ArgumentCount => _arguments.Length;
            public override object GetArgument(int index) => _arguments[index];
            public override string ToString(IFormatProvider formatProvider) => Number.FormatCompositeWith(formatProvider, _format, _arguments);
        }
    }
}
