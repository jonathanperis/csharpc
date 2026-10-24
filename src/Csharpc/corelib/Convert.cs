// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// System.Convert's conversions between primitives, strings (with providers and bases) and DateTime,
// ported from dotnet/runtime (release/11.0) src/libraries/System.Private.CoreLib/src/System/Convert.cs and
// ParseNumbers.cs; see THIRD-PARTY-NOTICES.md. The object/IConvertible overloads, ChangeType and the
// Base64/hex encoders are not ported (CSLLVM0002). Conversions that .NET routes through IConvertible and
// that always fail (e.g. ToBoolean(char), ToDateTime(int)) throw its InvalidCastException directly.
#nullable disable
using System;
using System.Diagnostics;
using System.Globalization;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Convert")]
    internal static class ConvertExtras
    {
        internal static void ThrowOverflow(string type)
        {
            switch (type)
            {
                case "sbyte": throw new OverflowException(ConvertSR.Overflow_SByte);
                case "short": throw new OverflowException(ConvertSR.Overflow_Int16);
                case "int": throw new OverflowException(ConvertSR.Overflow_Int32);
                case "uint": throw new OverflowException(ConvertSR.Overflow_UInt32);
                default: throw new OverflowException(ConvertSR.Overflow_Int64);
            }
        }

        public static bool ToBoolean(bool value)
        {
            return value;
        }

        public static bool ToBoolean(sbyte value)
        {
            return value != 0;
        }

        public static bool ToBoolean(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'Boolean'.");
        }

        public static bool ToBoolean(byte value)
        {
            return value != 0;
        }

        public static bool ToBoolean(short value)
        {
            return value != 0;
        }

        public static bool ToBoolean(ushort value)
        {
            return value != 0;
        }

        public static bool ToBoolean(int value)
        {
            return value != 0;
        }

        public static bool ToBoolean(uint value)
        {
            return value != 0;
        }

        public static bool ToBoolean(long value)
        {
            return value != 0;
        }

        public static bool ToBoolean(ulong value)
        {
            return value != 0;
        }

        public static bool ToBoolean(string? value)
        {
            if (value == null)
                return false;
            return bool.Parse(value);
        }

        public static bool ToBoolean(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return false;
            return bool.Parse(value);
        }

        public static bool ToBoolean(float value)
        {
            return value != 0;
        }

        public static bool ToBoolean(double value)
        {
            return value != 0;
        }

        public static bool ToBoolean(decimal value)
        {
            return value != 0;
        }

        public static bool ToBoolean(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Boolean'.");
        }

        public static char ToChar(bool value)
        {
            throw new InvalidCastException("Invalid cast from 'Boolean' to 'Char'.");
        }

        public static char ToChar(char value)
        {
            return value;
        }

        public static char ToChar(sbyte value)
        {
            if (value < 0) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(byte value)
        {
            return (char)value;
        }

        public static char ToChar(short value)
        {
            if (value < 0) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(ushort value)
        {
            return (char)value;
        }

        public static char ToChar(int value) => Convert.ToChar((uint)value);

        public static char ToChar(uint value)
        {
            if (value > char.MaxValue) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(long value) => Convert.ToChar((ulong)value);

        public static char ToChar(ulong value)
        {
            if (value > char.MaxValue) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(string value)
        {
            return Convert.ToChar(value, null);
        }

        public static char ToChar(string value, IFormatProvider? provider)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Length != 1)
                throw new FormatException(ConvertSR.Format_NeedSingleChar);

            return value[0];
        }

        public static char ToChar(float value)
        {
            throw new InvalidCastException("Invalid cast from 'Single' to 'Char'.");
        }

        public static char ToChar(double value)
        {
            throw new InvalidCastException("Invalid cast from 'Double' to 'Char'.");
        }

        public static char ToChar(decimal value)
        {
            throw new InvalidCastException("Invalid cast from 'Decimal' to 'Char'.");
        }

        public static char ToChar(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Char'.");
        }

        public static sbyte ToSByte(bool value)
        {
            return value ? (sbyte)1 : (sbyte)0;
        }

        public static sbyte ToSByte(sbyte value)
        {
            return value;
        }

        public static sbyte ToSByte(char value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(byte value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(short value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(ushort value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(int value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(uint value)
        {
            if (value > (uint)sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(long value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(ulong value)
        {
            if (value > (ulong)sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(float value)
        {
            return Convert.ToSByte((double)value);
        }

        public static sbyte ToSByte(double value)
        {
            return Convert.ToSByte(Convert.ToInt32(value));
        }

        public static sbyte ToSByte(decimal value)
        {
            return decimal.ToSByte(decimal.Round(value, 0));
        }

        public static sbyte ToSByte(string? value)
        {
            if (value == null)
                return 0;
            return sbyte.Parse(value);
        }

        public static sbyte ToSByte(string value, IFormatProvider? provider)
        {
            return sbyte.Parse(value, provider);
        }

        public static sbyte ToSByte(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'SByte'.");
        }

        public static byte ToByte(bool value)
        {
            return value ? (byte)1 : (byte)0;
        }

        public static byte ToByte(byte value)
        {
            return value;
        }

        public static byte ToByte(char value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(sbyte value)
        {
            if (value < 0) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(short value)
        {
            if ((uint)value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(ushort value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(int value) => Convert.ToByte((uint)value);

        public static byte ToByte(uint value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(long value) => Convert.ToByte((ulong)value);

        public static byte ToByte(ulong value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(float value)
        {
            return Convert.ToByte((double)value);
        }

        public static byte ToByte(double value)
        {
            return Convert.ToByte(Convert.ToInt32(value));
        }

        public static byte ToByte(decimal value)
        {
            return decimal.ToByte(decimal.Round(value, 0));
        }

        public static byte ToByte(string? value)
        {
            if (value == null)
                return 0;
            return byte.Parse(value);
        }

        public static byte ToByte(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return byte.Parse(value, provider);
        }

        public static byte ToByte(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Byte'.");
        }

        public static short ToInt16(bool value)
        {
            return value ? (short)1 : (short)0;
        }

        public static short ToInt16(char value)
        {
            if (value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(sbyte value)
        {
            return value;
        }

        public static short ToInt16(byte value)
        {
            return value;
        }

        public static short ToInt16(ushort value)
        {
            if (value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(int value)
        {
            if (value < short.MinValue || value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(uint value)
        {
            if (value > (uint)short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(short value)
        {
            return value;
        }

        public static short ToInt16(long value)
        {
            if (value < short.MinValue || value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(ulong value)
        {
            if (value > (ulong)short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(float value)
        {
            return Convert.ToInt16((double)value);
        }

        public static short ToInt16(double value)
        {
            return Convert.ToInt16(Convert.ToInt32(value));
        }

        public static short ToInt16(decimal value)
        {
            return decimal.ToInt16(decimal.Round(value, 0));
        }

        public static short ToInt16(string? value)
        {
            if (value == null)
                return 0;
            return short.Parse(value);
        }

        public static short ToInt16(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return short.Parse(value, provider);
        }

        public static short ToInt16(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Int16'.");
        }

        public static ushort ToUInt16(bool value)
        {
            return value ? (ushort)1 : (ushort)0;
        }

        public static ushort ToUInt16(char value)
        {
            return value;
        }

        public static ushort ToUInt16(sbyte value)
        {
            if (value < 0) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(byte value)
        {
            return value;
        }

        public static ushort ToUInt16(short value)
        {
            if (value < 0) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(int value) => Convert.ToUInt16((uint)value);

        public static ushort ToUInt16(ushort value)
        {
            return value;
        }

        public static ushort ToUInt16(uint value)
        {
            if (value > ushort.MaxValue) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(long value) => Convert.ToUInt16((ulong)value);

        public static ushort ToUInt16(ulong value)
        {
            if (value > ushort.MaxValue) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(float value)
        {
            return Convert.ToUInt16((double)value);
        }

        public static ushort ToUInt16(double value)
        {
            return Convert.ToUInt16(Convert.ToInt32(value));
        }

        public static ushort ToUInt16(decimal value)
        {
            return decimal.ToUInt16(decimal.Round(value, 0));
        }

        public static ushort ToUInt16(string? value)
        {
            if (value == null)
                return 0;
            return ushort.Parse(value);
        }

        public static ushort ToUInt16(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return ushort.Parse(value, provider);
        }

        public static ushort ToUInt16(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'UInt16'.");
        }

        public static int ToInt32(bool value)
        {
            return value ? 1 : 0;
        }

        public static int ToInt32(char value)
        {
            return value;
        }

        public static int ToInt32(sbyte value)
        {
            return value;
        }

        public static int ToInt32(byte value)
        {
            return value;
        }

        public static int ToInt32(short value)
        {
            return value;
        }

        public static int ToInt32(ushort value)
        {
            return value;
        }

        public static int ToInt32(uint value)
        {
            if ((int)value < 0) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(int value)
        {
            return value;
        }

        public static int ToInt32(long value)
        {
            if (value < int.MinValue || value > int.MaxValue) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(ulong value)
        {
            if (value > int.MaxValue) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(float value)
        {
            return Convert.ToInt32((double)value);
        }

        public static int ToInt32(double value)
        {
            if (value >= 0)
            {
                if (value < 2147483647.5)
                {
                    int result = (int)value;
                    double dif = value - result;
                    if (dif > 0.5 || dif == 0.5 && (result & 1) != 0) result++;
                    return result;
                }
            }
            else
            {
                if (value >= -2147483648.5)
                {
                    int result = (int)value;
                    double dif = value - result;
                    if (dif < -0.5 || dif == -0.5 && (result & 1) != 0) result--;
                    return result;
                }
            }
            throw new OverflowException(ConvertSR.Overflow_Int32);
        }

        public static int ToInt32(decimal value)
        {
            return decimal.ToInt32(decimal.Round(value, 0));
        }

        public static int ToInt32(string? value)
        {
            if (value == null)
                return 0;
            return int.Parse(value);
        }

        public static int ToInt32(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return int.Parse(value, provider);
        }

        public static int ToInt32(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Int32'.");
        }

        public static uint ToUInt32(bool value)
        {
            return value ? (uint)1 : (uint)0;
        }

        public static uint ToUInt32(char value)
        {
            return value;
        }

        public static uint ToUInt32(sbyte value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(byte value)
        {
            return value;
        }

        public static uint ToUInt32(short value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(ushort value)
        {
            return value;
        }

        public static uint ToUInt32(int value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(uint value)
        {
            return value;
        }

        public static uint ToUInt32(long value) => Convert.ToUInt32((ulong)value);

        public static uint ToUInt32(ulong value)
        {
            if (value > uint.MaxValue) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(float value)
        {
            return Convert.ToUInt32((double)value);
        }

        public static uint ToUInt32(double value)
        {
            if (value >= -0.5 && value < 4294967295.5)
            {
                uint result = (uint)value;
                double dif = value - result;
                if (dif > 0.5 || dif == 0.5 && (result & 1) != 0) result++;
                return result;
            }
            throw new OverflowException(ConvertSR.Overflow_UInt32);
        }

        public static uint ToUInt32(decimal value)
        {
            return decimal.ToUInt32(decimal.Round(value, 0));
        }

        public static uint ToUInt32(string? value)
        {
            if (value == null)
                return 0;
            return uint.Parse(value);
        }

        public static uint ToUInt32(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return uint.Parse(value, provider);
        }

        public static uint ToUInt32(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'UInt32'.");
        }

        public static long ToInt64(bool value)
        {
            return value ? 1 : 0;
        }

        public static long ToInt64(char value)
        {
            return value;
        }

        public static long ToInt64(sbyte value)
        {
            return value;
        }

        public static long ToInt64(byte value)
        {
            return value;
        }

        public static long ToInt64(short value)
        {
            return value;
        }

        public static long ToInt64(ushort value)
        {
            return value;
        }

        public static long ToInt64(int value)
        {
            return value;
        }

        public static long ToInt64(uint value)
        {
            return value;
        }

        public static long ToInt64(ulong value)
        {
            if ((long)value < 0) ThrowInt64OverflowException();
            return (long)value;
        }

        public static long ToInt64(long value)
        {
            return value;
        }

        public static long ToInt64(float value)
        {
            return Convert.ToInt64((double)value);
        }

        public static long ToInt64(double value)
        {
            return checked((long)Math.Round(value));
        }

        public static long ToInt64(decimal value)
        {
            return decimal.ToInt64(decimal.Round(value, 0));
        }

        public static long ToInt64(string? value)
        {
            if (value == null)
                return 0;
            return long.Parse(value);
        }

        public static long ToInt64(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return long.Parse(value, provider);
        }

        public static long ToInt64(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Int64'.");
        }

        public static ulong ToUInt64(bool value)
        {
            return value ? (ulong)1 : (ulong)0;
        }

        public static ulong ToUInt64(char value)
        {
            return value;
        }

        public static ulong ToUInt64(sbyte value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(byte value)
        {
            return value;
        }

        public static ulong ToUInt64(short value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(ushort value)
        {
            return value;
        }

        public static ulong ToUInt64(int value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(uint value)
        {
            return value;
        }

        public static ulong ToUInt64(long value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(ulong value)
        {
            return value;
        }

        public static ulong ToUInt64(float value)
        {
            return Convert.ToUInt64((double)value);
        }

        public static ulong ToUInt64(double value)
        {
            return checked((ulong)Math.Round(value));
        }

        public static ulong ToUInt64(decimal value)
        {
            return decimal.ToUInt64(decimal.Round(value, 0));
        }

        public static ulong ToUInt64(string? value)
        {
            if (value == null)
                return 0;
            return ulong.Parse(value);
        }

        public static ulong ToUInt64(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return ulong.Parse(value, provider);
        }

        public static ulong ToUInt64(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'UInt64'.");
        }

        public static float ToSingle(sbyte value)
        {
            return value;
        }

        public static float ToSingle(byte value)
        {
            return value;
        }

        public static float ToSingle(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'Single'.");
        }

        public static float ToSingle(short value)
        {
            return value;
        }

        public static float ToSingle(ushort value)
        {
            return value;
        }

        public static float ToSingle(int value)
        {
            return value;
        }

        public static float ToSingle(uint value)
        {
            return value;
        }

        public static float ToSingle(long value)
        {
            return value;
        }

        public static float ToSingle(ulong value)
        {
            return value;
        }

        public static float ToSingle(float value)
        {
            return value;
        }

        public static float ToSingle(double value)
        {
            return (float)value;
        }

        public static float ToSingle(decimal value)
        {
            return (float)value;
        }

        public static float ToSingle(string? value)
        {
            if (value == null)
                return 0;
            return float.Parse(value);
        }

        public static float ToSingle(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return float.Parse(value, provider);
        }

        public static float ToSingle(bool value)
        {
            return value ? 1 : 0;
        }

        public static float ToSingle(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Single'.");
        }

        public static double ToDouble(sbyte value)
        {
            return value;
        }

        public static double ToDouble(byte value)
        {
            return value;
        }

        public static double ToDouble(short value)
        {
            return value;
        }

        public static double ToDouble(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'Double'.");
        }

        public static double ToDouble(ushort value)
        {
            return value;
        }

        public static double ToDouble(int value)
        {
            return value;
        }

        public static double ToDouble(uint value)
        {
            return value;
        }

        public static double ToDouble(long value)
        {
            return value;
        }

        public static double ToDouble(ulong value)
        {
            return value;
        }

        public static double ToDouble(float value)
        {
            return value;
        }

        public static double ToDouble(double value)
        {
            return value;
        }

        public static double ToDouble(decimal value)
        {
            return (double)value;
        }

        public static double ToDouble(string? value)
        {
            if (value == null)
                return 0;
            return double.Parse(value);
        }

        public static double ToDouble(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return 0;
            return double.Parse(value, provider);
        }

        public static double ToDouble(bool value)
        {
            return value ? 1 : 0;
        }

        public static double ToDouble(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Double'.");
        }

        public static decimal ToDecimal(sbyte value)
        {
            return value;
        }

        public static decimal ToDecimal(byte value)
        {
            return value;
        }

        public static decimal ToDecimal(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'Decimal'.");
        }

        public static decimal ToDecimal(short value)
        {
            return value;
        }

        public static decimal ToDecimal(ushort value)
        {
            return value;
        }

        public static decimal ToDecimal(int value)
        {
            return value;
        }

        public static decimal ToDecimal(uint value)
        {
            return value;
        }

        public static decimal ToDecimal(long value)
        {
            return value;
        }

        public static decimal ToDecimal(ulong value)
        {
            return value;
        }

        public static decimal ToDecimal(float value)
        {
            return (decimal)value;
        }

        public static decimal ToDecimal(double value)
        {
            return (decimal)value;
        }

        public static decimal ToDecimal(string? value)
        {
            if (value == null)
                return decimal.Zero;
            return decimal.Parse(value);
        }

        public static decimal ToDecimal(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return decimal.Zero;
            return decimal.Parse(value, provider);
        }

        public static decimal ToDecimal(decimal value)
        {
            return value;
        }

        public static decimal ToDecimal(bool value)
        {
            return value ? 1 : 0;
        }

        public static decimal ToDecimal(DateTime value)
        {
            throw new InvalidCastException("Invalid cast from 'DateTime' to 'Decimal'.");
        }

        public static DateTime ToDateTime(DateTime value)
        {
            return value;
        }

        public static DateTime ToDateTime(string? value)
        {
            if (value == null)
                return new DateTime(0);
            return DateTime.Parse(value);
        }

        public static DateTime ToDateTime(string? value, IFormatProvider? provider)
        {
            if (value == null)
                return new DateTime(0);
            return DateTime.Parse(value, provider);
        }

        public static DateTime ToDateTime(sbyte value)
        {
            throw new InvalidCastException("Invalid cast from 'SByte' to 'DateTime'.");
        }

        public static DateTime ToDateTime(byte value)
        {
            throw new InvalidCastException("Invalid cast from 'Byte' to 'DateTime'.");
        }

        public static DateTime ToDateTime(short value)
        {
            throw new InvalidCastException("Invalid cast from 'Int16' to 'DateTime'.");
        }

        public static DateTime ToDateTime(ushort value)
        {
            throw new InvalidCastException("Invalid cast from 'UInt16' to 'DateTime'.");
        }

        public static DateTime ToDateTime(int value)
        {
            throw new InvalidCastException("Invalid cast from 'Int32' to 'DateTime'.");
        }

        public static DateTime ToDateTime(uint value)
        {
            throw new InvalidCastException("Invalid cast from 'UInt32' to 'DateTime'.");
        }

        public static DateTime ToDateTime(long value)
        {
            throw new InvalidCastException("Invalid cast from 'Int64' to 'DateTime'.");
        }

        public static DateTime ToDateTime(ulong value)
        {
            throw new InvalidCastException("Invalid cast from 'UInt64' to 'DateTime'.");
        }

        public static DateTime ToDateTime(bool value)
        {
            throw new InvalidCastException("Invalid cast from 'Boolean' to 'DateTime'.");
        }

        public static DateTime ToDateTime(char value)
        {
            throw new InvalidCastException("Invalid cast from 'Char' to 'DateTime'.");
        }

        public static DateTime ToDateTime(float value)
        {
            throw new InvalidCastException("Invalid cast from 'Single' to 'DateTime'.");
        }

        public static DateTime ToDateTime(double value)
        {
            throw new InvalidCastException("Invalid cast from 'Double' to 'DateTime'.");
        }

        public static DateTime ToDateTime(decimal value)
        {
            throw new InvalidCastException("Invalid cast from 'Decimal' to 'DateTime'.");
        }

        public static string ToString(bool value)
        {
            return value.ToString();
        }

        public static string ToString(bool value, IFormatProvider? provider)
        {
            return value.ToString();
        }

        public static string ToString(char value)
        {
            return char.ToString(value);
        }

        public static string ToString(char value, IFormatProvider? provider)
        {
            return value.ToString();
        }

        public static string ToString(sbyte value)
        {
            return value.ToString();
        }

        public static string ToString(sbyte value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(byte value)
        {
            return value.ToString();
        }

        public static string ToString(byte value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(short value)
        {
            return value.ToString();
        }

        public static string ToString(short value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(ushort value)
        {
            return value.ToString();
        }

        public static string ToString(ushort value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(int value)
        {
            return value.ToString();
        }

        public static string ToString(int value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(uint value)
        {
            return value.ToString();
        }

        public static string ToString(uint value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(long value)
        {
            return value.ToString();
        }

        public static string ToString(long value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(ulong value)
        {
            return value.ToString();
        }

        public static string ToString(ulong value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(float value)
        {
            return value.ToString();
        }

        public static string ToString(float value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(double value)
        {
            return value.ToString();
        }

        public static string ToString(double value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(decimal value)
        {
            return value.ToString();
        }

        public static string ToString(decimal value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string ToString(DateTime value)
        {
            return value.ToString();
        }

        public static string ToString(DateTime value, IFormatProvider? provider)
        {
            return value.ToString(provider);
        }

        public static string? ToString(string? value)
        {
            return value;
        }

        public static string? ToString(string? value, IFormatProvider? provider)
        {
            return value;
        }

        public static byte ToByte(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }

            if (value == null)
            {
                return 0;
            }

            int r = ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight | ParseNumbers.TreatAsUnsigned);
            if ((uint)r > byte.MaxValue)
                ThrowByteOverflowException();
            return (byte)r;
        }

        public static sbyte ToSByte(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }

            if (value == null)
            {
                return 0;
            }

            int r = ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight | ParseNumbers.TreatAsI1);
            if (fromBase != 10 && r <= byte.MaxValue)
                return (sbyte)r;

            if (r < sbyte.MinValue || r > sbyte.MaxValue)
                ThrowSByteOverflowException();
            return (sbyte)r;
        }

        public static short ToInt16(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }

            if (value == null)
            {
                return 0;
            }

            int r = ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight | ParseNumbers.TreatAsI2);
            if (fromBase != 10 && r <= ushort.MaxValue)
                return (short)r;

            if (r < short.MinValue || r > short.MaxValue)
                ThrowInt16OverflowException();
            return (short)r;
        }

        public static ushort ToUInt16(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }

            if (value == null)
            {
                return 0;
            }

            int r = ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight | ParseNumbers.TreatAsUnsigned);
            if ((uint)r > ushort.MaxValue)
                ThrowUInt16OverflowException();
            return (ushort)r;
        }

        public static int ToInt32(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }
            return value != null ?
                ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.IsTight) :
                0;
        }

        public static uint ToUInt32(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }
            return value != null ?
                (uint)ParseNumbers.StringToInt(value.AsSpan(), fromBase, ParseNumbers.TreatAsUnsigned | ParseNumbers.IsTight) :
                0;
        }

        public static long ToInt64(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }
            return value != null ?
                ParseNumbers.StringToLong(value.AsSpan(), fromBase, ParseNumbers.IsTight) :
                0;
        }

        public static ulong ToUInt64(string? value, int fromBase)
        {
            if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
            {
                ThrowInvalidBase();
            }
            return value != null ?
                (ulong)ParseNumbers.StringToLong(value.AsSpan(), fromBase, ParseNumbers.TreatAsUnsigned | ParseNumbers.IsTight) :
                0;
        }

        public static string ToString(byte value, int toBase) =>
            Convert.ToString((int)value, toBase);

        public static string ToString(short value, int toBase)
        {
            string format = "d";

            switch (toBase)
            {
                case 2:
                    format = "b";
                    break;

                case 8:
                    return ToOctalString((ushort)value);

                case 10:
                    break;

                case 16:
                    format = "x";
                    break;

                default:
                    ThrowInvalidBase();
                    break;
            };

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string ToString(int value, int toBase)
        {
            string format = "d";

            switch (toBase)
            {
                case 2:
                    format = "b";
                    break;

                case 8:
                    return ToOctalString((uint)value);

                case 10:
                    break;

                case 16:
                    format = "x";
                    break;

                default:
                    ThrowInvalidBase();
                    break;
            };

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string ToString(long value, int toBase)
        {
            string format = "d";

            switch (toBase)
            {
                case 2:
                    format = "b";
                    break;

                case 8:
                    return ToOctalString((ulong)value);

                case 10:
                    break;

                case 16:
                    format = "x";
                    break;

                default:
                    ThrowInvalidBase();
                    break;
            };

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private static void ThrowCharOverflowException() { throw new OverflowException(ConvertSR.Overflow_Char); }

        
        private static void ThrowByteOverflowException() { throw new OverflowException(ConvertSR.Overflow_Byte); }

        
        private static void ThrowSByteOverflowException() { throw new OverflowException(ConvertSR.Overflow_SByte); }

        
        private static void ThrowInt16OverflowException() { throw new OverflowException(ConvertSR.Overflow_Int16); }

        
        private static void ThrowUInt16OverflowException() { throw new OverflowException(ConvertSR.Overflow_UInt16); }

        
        private static void ThrowInt32OverflowException() { throw new OverflowException(ConvertSR.Overflow_Int32); }

        
        private static void ThrowUInt32OverflowException() { throw new OverflowException(ConvertSR.Overflow_UInt32); }

        
        private static void ThrowInt64OverflowException() { throw new OverflowException(ConvertSR.Overflow_Int64); }

        
        private static void ThrowUInt64OverflowException() { throw new OverflowException(ConvertSR.Overflow_UInt64); }

        private static void ThrowInvalidBase() => throw new ArgumentException(ConvertSR.Arg_InvalidBase);
        private static unsafe string ToOctalString(ulong value)
        {
            Span<char> chars = stackalloc char[22]; // max length of a ulong in octal

            int i = chars.Length;
            do
            {
                chars[--i] = (char)('0' + (value & 7));
                value >>= 3;
            }
            while (value != 0);

            return chars.Slice(i).ToString();
        }
    }

    internal static class ParseNumbers
    {
        internal const int TreatAsUnsigned = 0x0200;
        internal const int TreatAsI1 = 0x0400;
        internal const int TreatAsI2 = 0x0800;
        internal const int IsTight = 0x1000;

        public static long StringToLong(ReadOnlySpan<char> s, int radix, int flags)
        {
            int pos = 0;
            return StringToLong(s, radix, flags, ref pos);
        }

        public static long StringToLong(ReadOnlySpan<char> s, int radix, int flags, ref int currPos)
        {
            int i = currPos;

            // Do some radix checking.
            // A radix of -1 says to use whatever base is spec'd on the number.
            // Parse in Base10 until we figure out what the base actually is.
            int r = (-1 == radix) ? 10 : radix;

            if (r != 2 && r != 10 && r != 8 && r != 16)
                throw new ArgumentException(ConvertSR.Arg_InvalidBase, nameof(radix));

            int length = s.Length;

            if (i < 0 || i >= length)
                throw new ArgumentOutOfRangeException(ConvertSR.ArgumentOutOfRange_IndexMustBeLess);

            // Get rid of the whitespace and then check that we've still got some digits to parse.
            if ((flags & IsTight) == 0)
            {
                EatWhiteSpace(s, ref i);
                if (i == length)
                    throw new FormatException(ConvertSR.Format_EmptyInputString);
            }

            // Check for a sign
            int sign = 1;
            if (s[i] == '-')
            {
                if (r != 10)
                    throw new ArgumentException(ConvertSR.Arg_CannotHaveNegativeValue);

                if ((flags & TreatAsUnsigned) != 0)
                    throw new OverflowException(ConvertSR.Overflow_NegativeUnsigned);

                sign = -1;
                i++;
            }
            else if (s[i] == '+')
            {
                i++;
            }

            if ((radix == -1 || radix == 16) && (i + 1 < length) && s[i] == '0')
            {
                if (s[i + 1] == 'x' || s[i + 1] == 'X')
                {
                    r = 16;
                    i += 2;
                }
            }

            int grabNumbersStart = i;
            long result = GrabLongs(r, s, ref i, (flags & TreatAsUnsigned) != 0);

            // Check if they passed us a string with no parsable digits.
            if (i == grabNumbersStart)
                throw new FormatException(ConvertSR.Format_NoParsibleDigits);

            if ((flags & IsTight) != 0)
            {
                // If we've got effluvia left at the end of the string, complain.
                if (i < length)
                    throw new FormatException(ConvertSR.Format_ExtraJunkAtEnd);
            }

            // Put the current index back into the correct place.
            currPos = i;

            // Return the value properly signed.
            if ((ulong)result == 0x8000000000000000 && sign == 1 && r == 10 && ((flags & TreatAsUnsigned) == 0))
                ConvertExtras.ThrowOverflow("long");

            if (r == 10)
            {
                result *= sign;
            }

            return result;
        }

        public static int StringToInt(ReadOnlySpan<char> s, int radix, int flags)
        {
            int pos = 0;
            return StringToInt(s, radix, flags, ref pos);
        }

        public static int StringToInt(ReadOnlySpan<char> s, int radix, int flags, ref int currPos)
        {
            // They're requied to tell me where to start parsing.
            int i = currPos;

            // Do some radix checking.
            // A radix of -1 says to use whatever base is spec'd on the number.
            // Parse in Base10 until we figure out what the base actually is.
            int r = (-1 == radix) ? 10 : radix;

            if (r != 2 && r != 10 && r != 8 && r != 16)
                throw new ArgumentException(ConvertSR.Arg_InvalidBase, nameof(radix));

            int length = s.Length;

            if (i < 0 || i >= length)
                throw new ArgumentOutOfRangeException(ConvertSR.ArgumentOutOfRange_IndexMustBeLess);

            // Get rid of the whitespace and then check that we've still got some digits to parse.
            if ((flags & IsTight) == 0)
            {
                EatWhiteSpace(s, ref i);
                if (i == length)
                    throw new FormatException(ConvertSR.Format_EmptyInputString);
            }

            // Check for a sign
            int sign = 1;
            if (s[i] == '-')
            {
                if (r != 10)
                    throw new ArgumentException(ConvertSR.Arg_CannotHaveNegativeValue);

                if ((flags & TreatAsUnsigned) != 0)
                    throw new OverflowException(ConvertSR.Overflow_NegativeUnsigned);

                sign = -1;
                i++;
            }
            else if (s[i] == '+')
            {
                i++;
            }

            // Consume the 0x if we're in an unknown base or in base-16.
            if ((radix == -1 || radix == 16) && (i + 1 < length) && s[i] == '0')
            {
                if (s[i + 1] == 'x' || s[i + 1] == 'X')
                {
                    r = 16;
                    i += 2;
                }
            }

            int grabNumbersStart = i;
            int result = GrabInts(r, s, ref i, (flags & TreatAsUnsigned) != 0);

            // Check if they passed us a string with no parsable digits.
            if (i == grabNumbersStart)
                throw new FormatException(ConvertSR.Format_NoParsibleDigits);

            if ((flags & IsTight) != 0)
            {
                // If we've got effluvia left at the end of the string, complain.
                if (i < length)
                    throw new FormatException(ConvertSR.Format_ExtraJunkAtEnd);
            }

            // Put the current index back into the correct place.
            currPos = i;

            // Return the value properly signed.
            if ((flags & TreatAsI1) != 0)
            {
                if ((uint)result > 0xFF)
                    ConvertExtras.ThrowOverflow("sbyte");
            }
            else if ((flags & TreatAsI2) != 0)
            {
                if ((uint)result > 0xFFFF)
                    ConvertExtras.ThrowOverflow("short");
            }
            else if ((uint)result == 0x80000000 && sign == 1 && r == 10 && ((flags & TreatAsUnsigned) == 0))
            {
                ConvertExtras.ThrowOverflow("int");
            }

            if (r == 10)
            {
                result *= sign;
            }

            return result;
        }

        private static void EatWhiteSpace(ReadOnlySpan<char> s, ref int i)
        {
            int localIndex = i;
            for (; localIndex < s.Length && char.IsWhiteSpace(s[localIndex]); localIndex++) ;
            i = localIndex;
        }

        private static long GrabLongs(int radix, ReadOnlySpan<char> s, ref int i, bool isUnsigned)
        {
            ulong result = 0;
            ulong maxVal;

            // Allow all non-decimal numbers to set the sign bit.
            if (radix == 10 && !isUnsigned)
            {
                maxVal = 0x7FFFFFFFFFFFFFFF / 10;

                // Read all of the digits and convert to a number
                while (i < s.Length && IsDigit(s[i], radix, out int value))
                {
                    // Check for overflows - this is sufficient & correct.
                    if (result > maxVal || ((long)result) < 0)
                    {
                        ConvertExtras.ThrowOverflow("long");
                    }

                    result = result * (ulong)radix + (ulong)value;
                    i++;
                }

                if ((long)result < 0 && result != 0x8000000000000000)
                {
                    ConvertExtras.ThrowOverflow("long");
                }
            }
            else
            {
                Debug.Assert(radix == 2 || radix == 8 || radix == 10 || radix == 16);
                maxVal =
                    radix == 10 ? 0xffffffffffffffff / 10 :
                    radix == 16 ? 0xffffffffffffffff / 16 :
                    radix == 8 ? 0xffffffffffffffff / 8 :
                    0xffffffffffffffff / 2;

                // Read all of the digits and convert to a number
                while (i < s.Length && IsDigit(s[i], radix, out int value))
                {
                    // Check for overflows - this is sufficient & correct.
                    if (result > maxVal)
                    {
                        ConvertExtras.ThrowOverflow("ulong");
                    }

                    ulong temp = result * (ulong)radix + (ulong)value;

                    if (temp < result) // this means overflow as well
                    {
                        ConvertExtras.ThrowOverflow("ulong");
                    }

                    result = temp;
                    i++;
                }
            }

            return (long)result;
        }

        private static int GrabInts(int radix, ReadOnlySpan<char> s, ref int i, bool isUnsigned)
        {
            uint result = 0;
            uint maxVal;

            // Allow all non-decimal numbers to set the sign bit.
            if (radix == 10 && !isUnsigned)
            {
                maxVal = (0x7FFFFFFF / 10);

                // Read all of the digits and convert to a number
                while (i < s.Length && IsDigit(s[i], radix, out int value))
                {
                    // Check for overflows - this is sufficient & correct.
                    if (result > maxVal || (int)result < 0)
                    {
                        ConvertExtras.ThrowOverflow("int");
                    }
                    result = result * (uint)radix + (uint)value;
                    i++;
                }
                if ((int)result < 0 && result != 0x80000000)
                {
                    ConvertExtras.ThrowOverflow("int");
                }
            }
            else
            {
                Debug.Assert(radix == 2 || radix == 8 || radix == 10 || radix == 16);
                maxVal =
                    radix == 10 ? 0xffffffff / 10 :
                    radix == 16 ? 0xffffffff / 16 :
                    radix == 8 ? 0xffffffff / 8 :
                    0xffffffff / 2;

                // Read all of the digits and convert to a number
                while (i < s.Length && IsDigit(s[i], radix, out int value))
                {
                    // Check for overflows - this is sufficient & correct.
                    if (result > maxVal)
                    {
                        ConvertExtras.ThrowOverflow("uint");
                    }

                    uint temp = result * (uint)radix + (uint)value;

                    if (temp < result) // this means overflow as well
                    {
                        ConvertExtras.ThrowOverflow("uint");
                    }

                    result = temp;
                    i++;
                }
            }

            return (int)result;
        }

        
        private static bool IsDigit(char c, int radix, out int result)
        {
            int tmp;
            if ((uint)(c - '0') <= 9)
            {
                result = tmp = c - '0';
            }
            else if ((uint)(c - 'A') <= 'Z' - 'A')
            {
                result = tmp = c - 'A' + 10;
            }
            else if ((uint)(c - 'a') <= 'z' - 'a')
            {
                result = tmp = c - 'a' + 10;
            }
            else
            {
                result = -1;
                return false;
            }

            return tmp < radix;
        }
    }

    internal static class ConvertSR
    {
        internal const string Arg_CannotHaveNegativeValue = "String cannot contain a minus sign if the base is not 10.";
        internal const string Arg_InvalidBase = "Invalid Base.";
        internal const string ArgumentOutOfRange_IndexMustBeLess = "Index was out of range. Must be non-negative and less than the size of the collection.";
        internal const string Format_EmptyInputString = "Input string was either empty or contained only whitespace.";
        internal const string Format_ExtraJunkAtEnd = "Additional non-parsable characters are at the end of the string.";
        internal const string Format_NeedSingleChar = "String must be exactly one character long.";
        internal const string Format_NoParsibleDigits = "Could not find any recognizable digits.";
        internal const string Overflow_Byte = "Value was either too large or too small for an unsigned byte.";
        internal const string Overflow_Char = "Value was either too large or too small for a character.";
        internal const string Overflow_Int16 = "Value was either too large or too small for an Int16.";
        internal const string Overflow_Int32 = "Value was either too large or too small for an Int32.";
        internal const string Overflow_Int64 = "Value was either too large or too small for an Int64.";
        internal const string Overflow_NegativeUnsigned = "The string was being parsed as an unsigned number and could not have a negative sign.";
        internal const string Overflow_SByte = "Value was either too large or too small for a signed byte.";
        internal const string Overflow_UInt16 = "Value was either too large or too small for a UInt16.";
        internal const string Overflow_UInt32 = "Value was either too large or too small for a UInt32.";
        internal const string Overflow_UInt64 = "Value was either too large or too small for a UInt64.";
    }
}
