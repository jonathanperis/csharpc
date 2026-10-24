// System.Text.CompositeFormat: a parsed composite format string. csharpc keeps the string and formats
// through string.Format (StringExtras); Parse validates the holes like .NET (same offsets and reasons)
// so errors surface at the same call.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.CompositeFormat")]
    public sealed class CompositeFormatImpl
    {
        private const string EndsPrematurely = "Format item ends prematurely.";
        private const string ExpectedDigit = "Expected an ASCII digit.";
        private const string UnexpectedClose = "Unexpected closing brace without a corresponding opening brace.";

        private readonly string _format;
        private readonly int _minimumArgumentCount;

        private CompositeFormatImpl(string format, int minimumArgumentCount)
        {
            _format = format;
            _minimumArgumentCount = minimumArgumentCount;
        }

        public string Format => _format;
        public int MinimumArgumentCount => _minimumArgumentCount;

        public static System.Text.CompositeFormat Parse(string format)
        {
            if (format == null) throw new ArgumentNullException("format");
            int max = -1;
            int i = 0;
            while (i < format.Length)
            {
                char c = format[i++];
                if (c == '}')
                {
                    if (i < format.Length && format[i] == '}') { i++; continue; }
                    throw Invalid(i, UnexpectedClose);
                }
                if (c != '{') continue;
                if (i < format.Length && format[i] == '{') { i++; continue; }
                int start = i;
                int index = 0;
                while (i < format.Length && format[i] >= '0' && format[i] <= '9') index = index * 10 + (format[i++] - '0');
                if (i == start) throw Invalid(i, i >= format.Length ? EndsPrematurely : ExpectedDigit);
                while (i < format.Length && format[i] == ' ') i++;
                if (i < format.Length && format[i] == ',')
                {
                    i++;
                    while (i < format.Length && format[i] == ' ') i++;
                    if (i < format.Length && format[i] == '-') i++;
                    int digits = i;
                    while (i < format.Length && format[i] >= '0' && format[i] <= '9') i++;
                    if (i == digits) throw Invalid(i, i >= format.Length ? EndsPrematurely : ExpectedDigit);
                    while (i < format.Length && format[i] == ' ') i++;
                }
                if (i < format.Length && format[i] == ':')
                {
                    i++;
                    while (i < format.Length && format[i] != '}')
                    {
                        if (format[i] == '{') throw Invalid(i, EndsPrematurely);
                        i++;
                    }
                }
                if (i >= format.Length || format[i] != '}') throw Invalid(i, EndsPrematurely);
                i++;
                if (index > max) max = index;
            }
            return (System.Text.CompositeFormat)(object)new CompositeFormatImpl(format, max + 1);
        }

        private static FormatException Invalid(int offset, string reason) =>
            new FormatException("Input string was not in a correct format. Failure to parse near offset " + offset + ". " + reason);
    }
}
