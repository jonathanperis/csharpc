// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/
// Buffers/StandardFormat.cs; see THIRD-PARTY-NOTICES.md. The format argument of Utf8Formatter/Utf8Parser.
#nullable disable
using System;
using System.Buffers;

namespace CsharpcCoreLib
{
    [Shadows("System.Buffers.StandardFormat")]
    public readonly struct StandardFormatImpl : IEquatable<StandardFormat>
    {
        public const byte NoPrecision = byte.MaxValue;
        public const byte MaxPrecision = 99;

        private readonly byte _format;
        private readonly byte _precision;

        public char Symbol => (char)_format;
        public byte Precision => _precision;
        public bool HasPrecision => _precision != NoPrecision;
        internal byte PrecisionOrZero => _precision != NoPrecision ? _precision : (byte)0;
        public bool IsDefault => (_format | _precision) == 0;

        public StandardFormatImpl(char symbol, byte precision = NoPrecision)
        {
            if (precision != NoPrecision && precision > MaxPrecision)
                throw new ArgumentOutOfRangeException("precision", "Precision cannot be larger than 99.");
            if (symbol != (byte)symbol)
                throw new ArgumentOutOfRangeException("symbol", "Format specifier was invalid.");
            _format = (byte)symbol;
            _precision = precision;
        }

        public static implicit operator StandardFormatImpl(char symbol) => new StandardFormatImpl(symbol);

        public static StandardFormat Parse(ReadOnlySpan<char> format)
        {
            ParseHelper(format, out StandardFormat standardFormat, throws: true);
            return standardFormat;
        }

        public static StandardFormat Parse(string format) => format == null ? default : Parse(format.AsSpan());

        public static bool TryParse(ReadOnlySpan<char> format, out StandardFormat result) => ParseHelper(format, out result);

        private static bool ParseHelper(ReadOnlySpan<char> format, out StandardFormat standardFormat, bool throws = false)
        {
            standardFormat = default;
            if (format.Length == 0) return true;
            char symbol = format[0];
            byte precision;
            if (format.Length == 1) precision = NoPrecision;
            else
            {
                uint parsedPrecision = 0;
                for (int i = 1; i < format.Length; i++)
                {
                    uint digit = format[i] - 48u;
                    if (digit > 9)
                        return throws ? throw new FormatException("Characters following the format symbol must be a number of 99 or less.") : false;
                    parsedPrecision = parsedPrecision * 10 + digit;
                    if (parsedPrecision > MaxPrecision)
                        return throws ? throw new FormatException("Precision cannot be larger than 99.") : false;
                }
                precision = (byte)parsedPrecision;
            }
            standardFormat = new StandardFormat(symbol, precision);
            return true;
        }

        public override bool Equals(object obj) => obj is StandardFormat other && Equals(other);
        public override int GetHashCode() => _format.GetHashCode() ^ _precision.GetHashCode();
        public bool Equals(StandardFormat other) => _format == other.Symbol && _precision == other.Precision;

        public override string ToString()
        {
            if (_format == 0) return "";
            if (_precision == NoPrecision) return ((char)_format).ToString();
            return (char)_format + _precision.ToString();
        }

        public static bool operator ==(StandardFormatImpl left, StandardFormatImpl right) => left._format == right._format && left._precision == right._precision;
        public static bool operator !=(StandardFormatImpl left, StandardFormatImpl right) => !(left == right);
    }
}
