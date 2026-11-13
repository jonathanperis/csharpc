// Floating-point generic-math statics of float and double (float.Sin, double.Hypot, ...) and the System.MathF /
// System.Math members that are plain C# in .NET. Ported from dotnet/runtime (MIT; the v11.0.0-rc.2
// System.Private.CoreLib sources of Single.cs, Double.cs, Math.cs, MathF.cs and Number.Rounding.cs), so results,
// NaN rules and exception messages are .NET's. What .NET reaches through the C runtime (sinf, pow, ...) is
// mapped to libm in CodeGen/Intrinsics.cs, as for Math. The bodies of the members that are long in .NET
// (Hypot, RootN, the *Pi family, the degree conversions of double) are copied from those files with only these
// edits: doc comments and Debug.Assert dropped, `int.IsOddInteger(n)` spelled out, `internal` constants private.
#nullable disable
using System;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.MathF")]
    internal static class MathFMembers
    {
        public static float BitDecrement(float x)
        {
            uint bits = BitConverter.SingleToUInt32Bits(x);
            if (!float.IsFinite(x)) return bits == 0x7F800000u ? float.MaxValue : x;
            if (bits == 0u) return -float.Epsilon;
            bits = float.IsNegative(x) ? bits + 1 : bits - 1;
            return BitConverter.UInt32BitsToSingle(bits);
        }

        public static float BitIncrement(float x)
        {
            uint bits = BitConverter.SingleToUInt32Bits(x);
            if (!float.IsFinite(x)) return bits == 0xFF800000u ? float.MinValue : x;
            if (bits == 0x80000000u) return float.Epsilon;
            bits = float.IsNegative(x) ? bits - 1 : bits + 1;
            return BitConverter.UInt32BitsToSingle(bits);
        }

        public static float IEEERemainder(float x, float y)
        {
            if (float.IsNaN(x)) return x;
            if (float.IsNaN(y)) return y;
            float regularMod = x % y;
            if (float.IsNaN(regularMod)) return float.NaN;
            if (regularMod == 0 && float.IsNegative(x)) return -0.0f;
            float alternativeResult = regularMod - (MathF.Abs(y) * MathF.Sign(x));
            if (MathF.Abs(alternativeResult) == MathF.Abs(regularMod))
            {
                float divisionResult = x / y;
                float roundedResult = MathF.Round(divisionResult);
                return MathF.Abs(roundedResult) > MathF.Abs(divisionResult) ? alternativeResult : regularMod;
            }
            return MathF.Abs(alternativeResult) < MathF.Abs(regularMod) ? alternativeResult : regularMod;
        }

        public static int ILogB(float x)
        {
            uint bits = BitConverter.SingleToUInt32Bits(x) & 0x7FFFFFFFu;
            if (bits - 0x00800000u >= 0x7F800000u - 0x00800000u) // zero, subnormal, infinity or NaN
            {
                if (bits == 0) return int.MinValue;
                if (bits >= 0x7F800000u) return int.MaxValue;
                return -126 - (System.Numerics.BitOperations.LeadingZeroCount(bits) - 8);
            }
            return (int)(bits >> 23) - 127;
        }

        public static float Log(float x, float y)
        {
            if (float.IsNaN(x)) return x;
            if (float.IsNaN(y)) return y;
            if (y == 1) return float.NaN;
            if (x != 1 && (y == 0 || float.IsPositiveInfinity(y))) return float.NaN;
            return MathF.Log(x) / MathF.Log(y);
        }

        public static float MaxMagnitude(float x, float y)
        {
            float ax = MathF.Abs(x), ay = MathF.Abs(y);
            if (ax > ay || float.IsNaN(ax)) return x;
            if (ax == ay) return float.IsNegative(x) ? y : x;
            return y;
        }

        public static float MinMagnitude(float x, float y)
        {
            float ax = MathF.Abs(x), ay = MathF.Abs(y);
            if (ax < ay || float.IsNaN(ax)) return x;
            if (ax == ay) return float.IsNegative(x) ? x : y;
            return y;
        }

        // .NET leaves the precision open (RCPSS / FRECPE where the hardware has them, 1 / x elsewhere), and the
        // estimate it picks differs between CPUs; the exact quotient is always within what it allows.
        public static float ReciprocalEstimate(float x) => 1.0f / x;

        public static float ReciprocalSqrtEstimate(float x) => 1.0f / MathF.Sqrt(x);

        public static float Round(float x, int digits) => Round(x, digits, MidpointRounding.ToEven);

        public static float Round(float x, MidpointRounding mode)
        {
            switch (mode)
            {
                case MidpointRounding.AwayFromZero: return MathF.Truncate(x + MathF.CopySign(0.49999997f, x));
                case MidpointRounding.ToEven: return MathF.Round(x);
                case MidpointRounding.ToZero: return MathF.Truncate(x);
                case MidpointRounding.ToNegativeInfinity: return MathF.Floor(x);
                case MidpointRounding.ToPositiveInfinity: return MathF.Ceiling(x);
                default: return Number.RoundModeSingle(x, 0, (int)mode); // throws .NET's ArgumentException
            }
        }

        public static float Round(float x, int digits, MidpointRounding mode)
        {
            if (digits < 0) throw new ArgumentOutOfRangeException("digits", "Rounding digits must be greater than or equal to 0.");
            if ((uint)mode > 4u) return Number.RoundModeSingle(x, 0, (int)mode); // throws .NET's ArgumentException
            if (digits == 0) return Round(x, mode);
            // Only finite values below 2^23 have a fractional part; NaN and infinities fail the comparison.
            return MathF.Abs(x) < 8388608.0f ? Number.RoundModeSingle(x, digits, (int)mode) : x;
        }

        public static float ScaleB(float x, int n)
        {
            // musl's scalblnf, as .NET's: x * 2^n with the scaling split so only the last multiplication rounds.
            const float C1 = 1.7014118E+38f; // 2^127
            const float C2 = 1.1754944E-38f; // 2^-126
            const float C3 = 16777216f;      // 2^24
            float y = x;
            if (n > 127)
            {
                y *= C1;
                n -= 127;
                if (n > 127)
                {
                    y *= C1;
                    n -= 127;
                    if (n > 127) n = 127;
                }
            }
            else if (n < -126)
            {
                y *= C2 * C3;
                n += 126 - 24;
                if (n < -126)
                {
                    y *= C2 * C3;
                    n += 126 - 24;
                    if (n < -126) n = -126;
                }
            }
            return y * BitConverter.Int32BitsToSingle((0x7f + n) << 23);
        }

        public static (float Sin, float Cos) SinCos(float x) => (MathF.Sin(x), MathF.Cos(x));

        public static int Sign(float x)
        {
            if (x < 0) return -1;
            if (x > 0) return 1;
            if (x == 0) return 0;
            throw new ArithmeticException("Function does not accept floating point Not-a-Number values.");
        }
    }

    // The System.Math members of double that csharpc's intrinsics (Sqrt, Pow, ...) don't cover.
    [ShadowMembers("System.Math")]
    internal static class MathMoreMembers
    {
        public static double BitDecrement(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x);
            if (!double.IsFinite(x)) return bits == 0x7FF0000000000000ul ? double.MaxValue : x;
            if (bits == 0ul) return -double.Epsilon;
            bits = double.IsNegative(x) ? bits + 1 : bits - 1;
            return BitConverter.UInt64BitsToDouble(bits);
        }

        public static double BitIncrement(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x);
            if (!double.IsFinite(x)) return bits == 0xFFF0000000000000ul ? double.MinValue : x;
            if (bits == 0x8000000000000000ul) return double.Epsilon;
            bits = double.IsNegative(x) ? bits - 1 : bits + 1;
            return BitConverter.UInt64BitsToDouble(bits);
        }

        public static int ILogB(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x) & 0x7FFFFFFFFFFFFFFFul;
            if (bits - 0x0010000000000000ul >= 0x7FF0000000000000ul - 0x0010000000000000ul) // zero, subnormal, infinity or NaN
            {
                if (bits == 0) return int.MinValue;
                if (bits >= 0x7FF0000000000000ul) return int.MaxValue;
                return -1022 - (System.Numerics.BitOperations.LeadingZeroCount(bits) - 11);
            }
            return (int)(bits >> 52) - 1023;
        }

        public static double MaxMagnitude(double x, double y)
        {
            double ax = Math.Abs(x), ay = Math.Abs(y);
            if (ax > ay || double.IsNaN(ax)) return x;
            if (ax == ay) return double.IsNegative(x) ? y : x;
            return y;
        }

        public static double MinMagnitude(double x, double y)
        {
            double ax = Math.Abs(x), ay = Math.Abs(y);
            if (ax < ay || double.IsNaN(ax)) return x;
            if (ax == ay) return double.IsNegative(x) ? x : y;
            return y;
        }

        public static double ReciprocalEstimate(double d) => 1.0 / d;

        public static double ReciprocalSqrtEstimate(double d) => 1.0 / Math.Sqrt(d);

        public static (double Sin, double Cos) SinCos(double x) => (Math.Sin(x), Math.Cos(x));

        public static int Sign(double value)
        {
            if (value < 0) return -1;
            if (value > 0) return 1;
            if (value == 0) return 0;
            throw new ArithmeticException("Function does not accept floating point Not-a-Number values.");
        }
    }

    [ShadowMembers("System.Single")]
    internal static class SingleMath
    {
        private const float Pi = 3.14159265f;
        private const float NaN = float.NaN;
        private const float PositiveInfinity = float.PositiveInfinity;
        private const float NegativeInfinity = float.NegativeInfinity;
        private const double DegreesToRadiansHead = 0.017453292519943295;
        private const double RadiansToDegreesHead = 57.29577951308232;

        private static bool OddInt(long value) => (value & 1) != 0;
        private static bool EvenInt(long value) => (value & 1) == 0;

        // ---- forwarded to MathF ----
        public static float Acos(float x) => MathF.Acos(x);
        public static float Acosh(float x) => MathF.Acosh(x);
        public static float Asin(float x) => MathF.Asin(x);
        public static float Asinh(float x) => MathF.Asinh(x);
        public static float Atan(float x) => MathF.Atan(x);
        public static float Atanh(float x) => MathF.Atanh(x);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public static float Atan2Pi(float y, float x) => Atan2(y, x) / Pi;
        public static float Cbrt(float x) => MathF.Cbrt(x);
        public static float Cos(float x) => MathF.Cos(x);
        public static float Cosh(float x) => MathF.Cosh(x);
        public static float Exp(float x) => MathF.Exp(x);
        public static float ExpM1(float x) => MathF.Exp(x) - 1;
        public static float Exp2(float x) => MathF.Pow(2, x);
        public static float Exp2M1(float x) => MathF.Pow(2, x) - 1;
        public static float Exp10(float x) => MathF.Pow(10, x);
        public static float Exp10M1(float x) => MathF.Pow(10, x) - 1;
        public static float Log(float x) => MathF.Log(x);
        public static float Log(float x, float newBase) => MathF.Log(x, newBase);
        public static float Log2(float value) => MathF.Log2(value);
        public static float Log10(float x) => MathF.Log10(x);
        public static float LogP1(float x) => MathF.Log(x + 1);
        public static float Log2P1(float x) => MathF.Log2(x + 1);
        public static float Log10P1(float x) => MathF.Log10(x + 1);
        public static float Pow(float x, float y) => MathF.Pow(x, y);
        public static float Sin(float x) => MathF.Sin(x);
        public static (float Sin, float Cos) SinCos(float x) => MathF.SinCos(x);
        public static float Sinh(float x) => MathF.Sinh(x);
        public static float Tan(float x) => MathF.Tan(x);
        public static float Tanh(float x) => MathF.Tanh(x);
        public static float Sqrt(float x) => MathF.Sqrt(x);
        public static float Abs(float value) => MathF.Abs(value);
        public static float Truncate(float x) => MathF.Truncate(x);
        public static float Min(float x, float y) => MathF.Min(x, y);
        public static float Max(float x, float y) => MathF.Max(x, y);
        public static float BitDecrement(float x) => MathF.BitDecrement(x);
        public static float BitIncrement(float x) => MathF.BitIncrement(x);
        public static float CopySign(float value, float sign) => MathF.CopySign(value, sign);
        public static float FusedMultiplyAdd(float left, float right, float addend) => MathF.FusedMultiplyAdd(left, right, addend);
        public static float Ieee754Remainder(float left, float right) => MathF.IEEERemainder(left, right);
        public static int ILogB(float x) => MathF.ILogB(x);
        public static float MaxMagnitude(float x, float y) => MathF.MaxMagnitude(x, y);
        public static float MinMagnitude(float x, float y) => MathF.MinMagnitude(x, y);
        public static float ReciprocalEstimate(float x) => MathF.ReciprocalEstimate(x);
        public static float ReciprocalSqrtEstimate(float x) => MathF.ReciprocalSqrtEstimate(x);
        public static float ScaleB(float x, int n) => MathF.ScaleB(x, n);
        public static float Round(float x, int digits) => MathF.Round(x, digits);
        public static float Round(float x, MidpointRounding mode) => MathF.Round(x, mode);
        public static float Round(float x, int digits, MidpointRounding mode) => MathF.Round(x, digits, mode);
        public static int Sign(float value) => MathF.Sign(value);

        // ---- Single.cs, short ones ----
        public static float MaxNative(float x, float y) => (x > y) ? x : y;
        public static float MinNative(float x, float y) => (x < y) ? x : y;
        public static float ClampNative(float value, float min, float max)
        {
            if (min > max) throw new ArgumentException(string.Format("'{0}' cannot be greater than {1}.", min, max));
            return MinNative(MaxNative(value, min), max);
        }
        public static float MultiplyAddEstimate(float left, float right, float addend) => MathF.FusedMultiplyAdd(left, right, addend);
        public static float Lerp(float value1, float value2, float amount) => MultiplyAddEstimate(value1, 1.0f - amount, value2 * amount);
        public static float DegreesToRadians(float degrees) => (float)(degrees * DegreesToRadiansHead);
        public static float RadiansToDegrees(float radians) => (float)(radians * RadiansToDegreesHead);
        public static bool IsNegative(float f) => BitConverter.SingleToInt32Bits(f) < 0;
        public static bool IsNormal(float f) => (BitConverter.SingleToUInt32Bits(MathF.Abs(f)) - 0x00800000u) < (0x7F800000u - 0x00800000u);
        public static bool IsSubnormal(float f) => (BitConverter.SingleToUInt32Bits(MathF.Abs(f)) - 1) < 0x007FFFFFu;

        // ---- Single.cs, copied ----
        public static bool IsEvenInteger(float value)
        {
            uint bits = BitConverter.SingleToUInt32Bits(Abs(value));

            if (bits < 0x3F80_0000)
            {
                return bits == 0;
            }

            if (bits >= 0x4B80_0000)
            {
                return bits < 0x7F80_0000;
            }

            uint exponent = ((bits >> 23) & 0xFF) - 127;
            uint fractionalBits = 23 - exponent;
            uint firstIntegerBit = 1u << (int)fractionalBits;
            uint fractionalBitMask = firstIntegerBit - 1;

            return ((bits & fractionalBitMask) == 0) && ((bits & firstIntegerBit) == 0);
        }

        public static bool IsInteger(float value) => float.IsFinite(value) && (value == Truncate(value));

        public static bool IsOddInteger(float value) => IsInteger(value) && (Abs((value) % 2) == 1);

        public static bool IsPositive(float value) => BitConverter.SingleToInt32Bits(value) >= 0;

        public static bool IsRealNumber(float value)
        {
            // A NaN will never equal itself so this is an
            // easy and efficient way to check for a real number.

#pragma warning disable CS1718
            return value == value;
#pragma warning restore CS1718
        }

        public static float MaxNumber(float x, float y)
        {
            // This matches the IEEE 754:2019 `maximumNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the larger of the inputs. It
            // treats +0 as larger than -0 as per the specification.

            if (x != y)
            {
                if (!float.IsNaN(y))
                {
                    return y < x ? x : y;
                }

                return x;
            }

            return IsNegative(y) ? x : y;
        }

        public static float MinNumber(float x, float y)
        {
            // This matches the IEEE 754:2019 `minimumNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the larger of the inputs. It
            // treats +0 as larger than -0 as per the specification.

            if (x != y)
            {
                if (!float.IsNaN(y))
                {
                    return x < y ? x : y;
                }

                return x;
            }

            return IsNegative(x) ? x : y;
        }

        public static float MaxMagnitudeNumber(float x, float y)
        {
            // This matches the IEEE 754:2019 `maximumMagnitudeNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the input with a larger magnitude.
            // It treats +0 as larger than -0 as per the specification.

            float ax = Abs(x);
            float ay = Abs(y);

            if ((ax > ay) || float.IsNaN(ay))
            {
                return x;
            }

            if (ax == ay)
            {
                return IsNegative(x) ? y : x;
            }

            return y;
        }

        public static float MinMagnitudeNumber(float x, float y)
        {
            // This matches the IEEE 754:2019 `minimumMagnitudeNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the input with a larger magnitude.
            // It treats +0 as larger than -0 as per the specification.

            float ax = Abs(x);
            float ay = Abs(y);

            if ((ax < ay) || float.IsNaN(ay))
            {
                return x;
            }

            if (ax == ay)
            {
                return IsNegative(x) ? x : y;
            }

            return y;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (min > max)
            {
                throw new ArgumentException(string.Format("'{0}' cannot be greater than {1}.", min, max));
            }
            return Min(Max(value, min), max);
        }

        public static float Hypot(float x, float y)
        {
            // This code is based on `hypotf` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float result;

            if (float.IsFinite(x) && float.IsFinite(y))
            {
                float ax = Abs(x);
                float ay = Abs(y);

                if (ax == 0.0f)
                {
                    result = ay;
                }
                else if (ay == 0.0f)
                {
                    result = ax;
                }
                else
                {
                    double xx = ax;
                    xx *= xx;

                    double yy = ay;
                    yy *= yy;

                    result = (float)double.Sqrt(xx + yy);
                }
            }
            else if (float.IsInfinity(x) || float.IsInfinity(y))
            {
                // IEEE 754 requires that we return +Infinity
                // even if one of the inputs is NaN

                result = PositiveInfinity;
            }
            else
            {
                // IEEE 754 requires that we return NaN
                // if either input is NaN and neither is Infinity

                result = NaN;
            }

            return result;
        }

        public static float RootN(float x, int n)
        {
            float result;

            if (n > 0)
            {
                if (n == 2)
                {
                    result = (x != 0.0f) ? Sqrt(x) : 0.0f;
                }
                else if (n == 3)
                {
                    result = Cbrt(x);
                }
                else
                {
                    result = PositiveN(x, n);
                }
            }
            else if (n < 0)
            {
                result = NegativeN(x, n);
            }
            else
            {
                result = NaN;
            }

            return result;

            static float PositiveN(float x, int n)
            {
                float result;

                if (float.IsFinite(x))
                {
                    if (x != 0)
                    {
                        if ((x > 0) || IsOddInteger(n))
                        {
                            result = (float)double.Pow(Abs(x), 1.0 / n);
                            result = CopySign(result, x);
                        }
                        else
                        {
                            result = NaN;
                        }
                    }
                    else if (IsEvenInteger(n))
                    {
                        result = 0.0f;
                    }
                    else
                    {
                        result = CopySign(0.0f, x);
                    }
                }
                else if (float.IsNaN(x))
                {
                    result = NaN;
                }
                else if (x > 0)
                {
                    result = PositiveInfinity;
                }
                else
                {
                    result = OddInt(n) ? NegativeInfinity : NaN;
                }

                return result;
            }

            static float NegativeN(float x, int n)
            {
                float result;

                if (float.IsFinite(x))
                {
                    if (x != 0)
                    {
                        if ((x > 0) || IsOddInteger(n))
                        {
                            result = (float)double.Pow(Abs(x), 1.0 / n);
                            result = CopySign(result, x);
                        }
                        else
                        {
                            result = NaN;
                        }
                    }
                    else if (IsEvenInteger(n))
                    {
                        result = PositiveInfinity;
                    }
                    else
                    {
                        result = CopySign(PositiveInfinity, x);
                    }
                }
                else if (float.IsNaN(x))
                {
                    result = NaN;
                }
                else if (x > 0)
                {
                    result = 0.0f;
                }
                else
                {
                    result = OddInt(n) ? -0.0f : NaN;
                }

                return result;
            }
        }

        public static float AcosPi(float x)
        {
            return Acos(x) / Pi;
        }

        public static float AsinPi(float x)
        {
            return Asin(x) / Pi;
        }

        public static float AtanPi(float x)
        {
            return Atan(x) / Pi;
        }

        public static float CosPi(float x)
        {
            // This code is based on `cospif` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float result;

            if (float.IsFinite(x))
            {
                float ax = Abs(x);

                if (ax < 8_388_608.0f)              // |x| < 2^23
                {
                    if (ax > 0.25f)
                    {
                        int integral = (int)ax;

                        float fractional = ax - integral;
                        float sign = OddInt(integral) ? -1.0f : +1.0f;

                        if (fractional <= 0.25f)
                        {
                            if (fractional != 0.00f)
                            {
                                result = sign * CosForIntervalPiBy4(fractional * Pi);
                            }
                            else
                            {
                                result = sign;
                            }
                        }
                        else if (fractional <= 0.50f)
                        {
                            if (fractional != 0.50f)
                            {
                                result = sign * SinForIntervalPiBy4((0.5f - fractional) * Pi);
                            }
                            else
                            {
                                result = 0.0f;
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            result = -sign * SinForIntervalPiBy4((fractional - 0.5f) * Pi);
                        }
                        else
                        {
                            result = -sign * CosForIntervalPiBy4((1.0f - fractional) * Pi);
                        }
                    }
                    else if (ax >= 7.8125E-3f)      // |x| >= 2^-7
                    {
                        result = CosForIntervalPiBy4(x * Pi);
                    }
                    else if (ax >= 1.22070313E-4f)  // |x| >= 2^-13
                    {
                        float value = x * Pi;
                        result = 1.0f - (value * value * 0.5f);
                    }
                    else
                    {
                        result = 1.0f;
                    }
                }
                else if (ax < 16_777_216.0f)        // |x| < 2^24
                {
                    // x is an integer
                    int bits = BitConverter.SingleToInt32Bits(ax);
                    result = OddInt(bits) ? -1.0f : +1.0f;
                }
                else
                {
                    // x is an even integer
                    result = 1.0f;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        public static (float SinPi, float CosPi) SinCosPi(float x)
        {
            // This code is based on `cospif` and `sinpif` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float sinPi;
            float cosPi;

            if (float.IsFinite(x))
            {
                float ax = Abs(x);

                if (ax < 8_388_608.0f)              // |x| < 2^23
                {
                    if (ax > 0.25f)
                    {
                        int integral = (int)ax;

                        float fractional = ax - integral;
                        float sign = OddInt(integral) ? -1.0f : +1.0f;

                        float sinSign = ((x > 0.0f) ? +1.0f : -1.0f) * sign;
                        float cosSign = sign;

                        if (fractional <= 0.25f)
                        {
                            if (fractional != 0.00f)
                            {
                                float value = fractional * Pi;

                                sinPi = sinSign * SinForIntervalPiBy4(value);
                                cosPi = cosSign * CosForIntervalPiBy4(value);
                            }
                            else
                            {
                                sinPi = x * 0.0f;
                                cosPi = cosSign;
                            }
                        }
                        else if (fractional <= 0.50f)
                        {
                            if (fractional != 0.50f)
                            {
                                float value = (0.5f - fractional) * Pi;

                                sinPi = sinSign * CosForIntervalPiBy4(value);
                                cosPi = cosSign * SinForIntervalPiBy4(value);
                            }
                            else
                            {
                                sinPi = sinSign;
                                cosPi = 0.0f;
                            }
                        }
                        else if (fractional <= 0.75f)
                        {
                            float value = (fractional - 0.5f) * Pi;

                            sinPi = +sinSign * CosForIntervalPiBy4(value);
                            cosPi = -cosSign * SinForIntervalPiBy4(value);
                        }
                        else
                        {
                            float value = (1.0f - fractional) * Pi;

                            sinPi = +sinSign * SinForIntervalPiBy4(value);
                            cosPi = -cosSign * CosForIntervalPiBy4(value);
                        }
                    }
                    else if (ax >= 7.8125E-3f)      // |x| >= 2^-7
                    {
                        float value = x * Pi;

                        sinPi = SinForIntervalPiBy4(value);
                        cosPi = CosForIntervalPiBy4(value);
                    }
                    else if (ax >= 1.22070313E-4f)  // |x| >= 2^-13
                    {
                        float value = x * Pi;
                        float valueSq = value * value;

                        sinPi = value - (valueSq * value * (1.0f / 6.0f));
                        cosPi = 1.0f - (valueSq * 0.5f);
                    }
                    else
                    {
                        sinPi = x * Pi;
                        cosPi = 1.0f;
                    }
                }
                else if (ax < 16_777_216.0f)        // |x| < 2^24
                {
                    // x is an integer
                    sinPi = x * 0.0f;

                    int bits = BitConverter.SingleToInt32Bits(ax);
                    cosPi = OddInt(bits) ? -1.0f : +1.0f;
                }
                else
                {
                    // x is an even integer
                    sinPi = x * 0.0f;
                    cosPi = 1.0f;
                }
            }
            else
            {
                sinPi = NaN;
                cosPi = NaN;
            }

            return (sinPi, cosPi);
        }

        public static float SinPi(float x)
        {
            // This code is based on `sinpif` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float result;

            if (float.IsFinite(x))
            {
                float ax = Abs(x);

                if (ax < 8_388_608.0f)              // |x| < 2^23
                {
                    if (ax > 0.25f)
                    {
                        int integral = (int)ax;

                        float fractional = ax - integral;
                        float sign = ((x > 0.0f) ? +1.0f : -1.0f) * (OddInt(integral) ? -1.0f : +1.0f);

                        if (fractional <= 0.25f)
                        {
                            if (fractional != 0.00f)
                            {
                                result = sign * SinForIntervalPiBy4(fractional * Pi);
                            }
                            else
                            {
                                result = x * 0.0f;
                            }
                        }
                        else if (fractional <= 0.50f)
                        {
                            if (fractional != 0.50f)
                            {
                                result = sign * CosForIntervalPiBy4((0.5f - fractional) * Pi);
                            }
                            else
                            {
                                result = sign;
                            }
                        }
                        else if (fractional <= 0.75f)
                        {
                            result = sign * CosForIntervalPiBy4((fractional - 0.5f) * Pi);
                        }
                        else
                        {
                            result = sign * SinForIntervalPiBy4((1.0f - fractional) * Pi);
                        }
                    }
                    else if (ax >= 7.8125E-3f)      // |x| >= 2^-7
                    {
                        result = SinForIntervalPiBy4(x * Pi);
                    }
                    else if (ax >= 1.22070313E-4f)  // |x| >= 2^-13
                    {
                        float value = x * Pi;
                        result = value - (value * value * value * (1.0f / 6.0f));
                    }
                    else
                    {
                        result = x * Pi;
                    }
                }
                else
                {
                    // x is an integer
                    result = x * 0.0f;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        public static float TanPi(float x)
        {
            // This code is based on `tanpif` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float result;

            if (float.IsFinite(x))
            {
                float ax = Abs(x);
                float sign = (x > 0.0f) ? +1.0f : -1.0f;

                if (ax < 8_388_608.0f)              // |x| < 2^23
                {
                    if (ax > 0.25f)
                    {
                        int integral = (int)ax;
                        float fractional = ax - integral;

                        if (fractional <= 0.25f)
                        {
                            if (fractional != 0.00f)
                            {
                                result = sign * TanForIntervalPiBy4(fractional * Pi, isReciprocal: false);
                            }
                            else
                            {
                                result = sign * (OddInt(integral) ? -0.0f : +0.0f);
                            }
                        }
                        else if (fractional <= 0.50f)
                        {
                            if (fractional != 0.50f)
                            {
                                result = -sign * TanForIntervalPiBy4((0.5f - fractional) * Pi, isReciprocal: true);
                            }
                            else
                            {
                                result = +sign * (OddInt(integral) ? NegativeInfinity : PositiveInfinity);
                            }
                        }
                        else if (fractional <= 0.75f)
                        {
                            result = +sign * TanForIntervalPiBy4((fractional - 0.5f) * Pi, isReciprocal: true);
                        }
                        else
                        {
                            result = -sign * TanForIntervalPiBy4((1.0f - fractional) * Pi, isReciprocal: false);
                        }
                    }
                    else if (ax >= 7.8125E-3f)      // |x| >= 2^-7
                    {
                        result = TanForIntervalPiBy4(x * Pi, isReciprocal: false);
                    }
                    else if (ax >= 1.22070313E-4f)  // |x| >= 2^-13
                    {
                        float value = x * Pi;
                        result = value + (value * value * value * (1.0f / 3.0f));
                    }
                    else
                    {
                        result = x * Pi;
                    }
                }
                else if (ax < 16_777_216)           // |x| < 2^24
                {
                    // x is an integer
                    int bits = BitConverter.SingleToInt32Bits(ax);
                    result = sign * (OddInt(bits) ? -0.0f : +0.0f);
                }
                else
                {
                    // x is an even integer
                    result = sign * 0.0f;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        private static float CosForIntervalPiBy4(float x)
        {
            // This code is based on `cos_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // Taylor series for cos(x) is: 1 - (x^2 / 2!) + (x^4 / 4!) - (x^6 / 6!) ...
            //
            // Then define f(xx) where xx = (x * x)
            // and f(xx) = 1 - (xx / 2!) + (xx^2 / 4!) - (xx^3 / 6!) ...
            //
            // We use a minimax approximation of (f(xx) - 1 + (xx / 2)) / (xx * xx)
            // because this produces an expansion in even powers of x.

            const double C1 = +0.41666666666666665390037E-1;        // approx: +1 / 4!
            const double C2 = -0.13888888888887398280412E-2;        // approx: -1 / 6!
            const double C3 = +0.248015872987670414957399E-4;       // approx: +1 / 8!
            const double C4 = -0.275573172723441909470836E-6;       // approx: -1 / 10!

            double xx = x * x;
            double result = C4;

            result = (result * xx) + C3;
            result = (result * xx) + C2;
            result = (result * xx) + C1;

            result *= xx * xx;
            result += 1.0 - (0.5 * xx);

            return (float)result;
        }

        private static float SinForIntervalPiBy4(float x)
        {
            // This code is based on `sin_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // Taylor series for sin(x) is x - (x^3 / 3!) + (x^5 / 5!) - (x^7 / 7!) ...
            // Which can be expressed as x * (1 - (x^2 / 3!) + (x^4 /5!) - (x^6 /7!) ...)
            //
            // Then define f(xx) where xx = (x * x)
            // and f(xx) = 1 - (xx / 3!) + (xx^2 / 5!) - (xx^3 / 7!) ...
            //
            // We use a minimax approximation of (f(xx) - 1) / xx
            // because this produces an expansion in even powers of x.

            const double C1 = -0.166666666666666646259241729;       // approx: -1 / 3!
            const double C2 = +0.833333333333095043065222816E-2;    // approx: +1 / 5!
            const double C3 = -0.19841269836761125688538679E-3;     // approx: -1 / 7!
            const double C4 = +0.275573161037288022676895908448E-5; // approx: +1 / 9!

            double xx = x * x;
            double result = C4;

            result = (result * xx) + C3;
            result = (result * xx) + C2;
            result = (result * xx) + C1;

            result *= x * xx;
            result += x;

            return (float)result;
        }

        private static float TanForIntervalPiBy4(float x, bool isReciprocal)
        {
            // This code is based on `tan_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // Core Remez [1, 2] approximation to tan(x) on the interval [0, pi / 4].

            double xx = x * x;

            double denominator = +0.1844239256901656082986661E-1;
            denominator = -0.51396505478854532132342E+0 + (denominator * xx);
            denominator = +0.115588821434688393452299E+1 + (denominator * xx);

            double numerator = -0.172032480471481694693109E-1;
            numerator = 0.385296071263995406715129E+0 + (numerator * xx);

            double result = x * xx;
            result *= numerator / denominator;
            result += x;

            if (isReciprocal)
            {
                result = -1.0 / result;
            }

            return (float)result;
        }

    }

    [ShadowMembers("System.Double")]
    internal static class DoubleMath
    {
        private const double Pi = 3.1415926535897931;
        private const double NaN = double.NaN;
        private const double PositiveInfinity = double.PositiveInfinity;
        private const double NegativeInfinity = double.NegativeInfinity;
        private const int ExponentBias = 1023;
        private const int SignificandLength = 53;
        private const int BiasedExponentShift = 52;
        private const int ShiftedBiasedExponentMask = 0x7FF;

        private static bool OddInt(long value) => (value & 1) != 0;
        private static bool EvenInt(long value) => (value & 1) == 0;

        /// <summary>
        /// double.ConvertToIntegerNative: the platform's own conversion instruction. In range it truncates toward zero on every platform; for
        /// NaN and out-of-range values the instruction decides (x64: the minimum value, Arm64: saturation). For int and long on x64 and Arm64
        /// that instruction is what csharpc emits (CoreIntrinsics.ConvertToInt32Native, #351); elsewhere csharpc stops with an explicit
        /// error instead of picking a result.
        /// </summary>
        public static TInteger ConvertToIntegerNative<TInteger>(double value) where TInteger : System.Numerics.IBinaryInteger<TInteger>
        {
            if (CoreIntrinsics.HasNativeIntegerConversion())
            {
                if (typeof(TInteger) == typeof(int)) { int native = CoreIntrinsics.ConvertToInt32Native(value); return Unsafe.As<int, TInteger>(ref native); }
                if (typeof(TInteger) == typeof(long)) { long native = CoreIntrinsics.ConvertToInt64Native(value); return Unsafe.As<long, TInteger>(ref native); }
            }
            if (typeof(TInteger) == typeof(int))
            {
                // In range (strictly between int.MinValue - 1 and int.MaxValue + 1, so NaN fails too): the truncation is exact everywhere.
                if (value > -2147483649.0 && value < 2147483648.0) { int result = (int)value; return Unsafe.As<int, TInteger>(ref result); }
            }
            else if (!double.IsNaN(value))
            {
                try { return TInteger.CreateChecked(value); }
                catch (OverflowException) { }
            }
            ThrowNativeConversion();
            return default;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNativeConversion()
        {
            throw new NotSupportedException("double.ConvertToIntegerNative of NaN or of a value outside the integer type's range is not supported by csharpc yet (the result is the platform instruction's).");
        }

        // ---- forwarded to Math ----
        public static double Acos(double x) => Math.Acos(x);
        public static double Acosh(double x) => Math.Acosh(x);
        public static double Asin(double x) => Math.Asin(x);
        public static double Asinh(double x) => Math.Asinh(x);
        public static double Atan(double x) => Math.Atan(x);
        public static double Atanh(double x) => Math.Atanh(x);
        public static double Atan2(double y, double x) => Math.Atan2(y, x);
        public static double Atan2Pi(double y, double x) => Atan2(y, x) / Pi;
        public static double Cbrt(double x) => Math.Cbrt(x);
        public static double Cos(double x) => Math.Cos(x);
        public static double Cosh(double x) => Math.Cosh(x);
        public static double Exp(double x) => Math.Exp(x);
        public static double ExpM1(double x) => Math.Exp(x) - 1;
        public static double Exp2(double x) => Math.Pow(2, x);
        public static double Exp2M1(double x) => Math.Pow(2, x) - 1;
        public static double Exp10(double x) => Math.Pow(10, x);
        public static double Exp10M1(double x) => Math.Pow(10, x) - 1;
        public static double Log(double x) => Math.Log(x);
        public static double Log(double x, double newBase)
        {
            if (double.IsNaN(x)) return x;
            if (double.IsNaN(newBase)) return newBase;
            if (newBase == 1) return double.NaN;
            if (x != 1 && (newBase == 0 || double.IsPositiveInfinity(newBase))) return double.NaN;
            return Math.Log(x) / Math.Log(newBase);
        }
        public static double Log2(double value) => Math.Log2(value);
        public static double Log10(double x) => Math.Log10(x);
        public static double LogP1(double x) => Math.Log(x + 1);
        public static double Log2P1(double x) => Math.Log2(x + 1);
        public static double Log10P1(double x) => Math.Log10(x + 1);
        public static double Pow(double x, double y) => Math.Pow(x, y);
        public static double Sin(double x) => Math.Sin(x);
        public static (double Sin, double Cos) SinCos(double x) => Math.SinCos(x);
        public static double Sinh(double x) => Math.Sinh(x);
        public static double Tan(double x) => Math.Tan(x);
        public static double Tanh(double x) => Math.Tanh(x);
        public static double Sqrt(double x) => Math.Sqrt(x);
        public static double Abs(double value) => Math.Abs(value);
        public static double Truncate(double x) => Math.Truncate(x);
        public static double Min(double x, double y) => Math.Min(x, y);
        public static double Max(double x, double y) => Math.Max(x, y);
        public static double BitDecrement(double x) => Math.BitDecrement(x);
        public static double BitIncrement(double x) => Math.BitIncrement(x);
        public static double CopySign(double value, double sign) => Math.CopySign(value, sign);
        public static double FusedMultiplyAdd(double left, double right, double addend) => Math.FusedMultiplyAdd(left, right, addend);
        public static double Ieee754Remainder(double left, double right) => Math.IEEERemainder(left, right);
        public static int ILogB(double x) => Math.ILogB(x);
        public static double MaxMagnitude(double x, double y) => Math.MaxMagnitude(x, y);
        public static double MinMagnitude(double x, double y) => Math.MinMagnitude(x, y);
        public static double ReciprocalEstimate(double x) => Math.ReciprocalEstimate(x);
        public static double ReciprocalSqrtEstimate(double x) => Math.ReciprocalSqrtEstimate(x);
        public static double ScaleB(double x, int n) => Math.ScaleB(x, n);
        public static int Sign(double value) => Math.Sign(value);

        // ---- Double.cs, short ones ----
        public static double MaxNative(double x, double y) => (x > y) ? x : y;
        public static double MinNative(double x, double y) => (x < y) ? x : y;
        public static double ClampNative(double value, double min, double max)
        {
            if (min > max) throw new ArgumentException(string.Format("'{0}' cannot be greater than {1}.", min, max));
            return MinNative(MaxNative(value, min), max);
        }
        public static double MultiplyAddEstimate(double left, double right, double addend) => Math.FusedMultiplyAdd(left, right, addend);
        public static double Lerp(double value1, double value2, double amount) => MultiplyAddEstimate(value1, 1.0 - amount, value2 * amount);
        public static bool IsNormal(double d) => (BitConverter.DoubleToUInt64Bits(Math.Abs(d)) - 0x0010000000000000ul) < (0x7FF0000000000000ul - 0x0010000000000000ul);
        public static bool IsSubnormal(double d) => (BitConverter.DoubleToUInt64Bits(Math.Abs(d)) - 1) < 0x000FFFFFFFFFFFFFul;

        // ---- Double.cs, copied ----
        public static bool IsEvenInteger(double value) => IsInteger(value) && (Abs(value % 2) == 0);

        public static bool IsInteger(double value) => double.IsFinite(value) && (value == Truncate(value));

        public static bool IsOddInteger(double value) => IsInteger(value) && (Abs(value % 2) == 1);

        public static bool IsPositive(double value) => BitConverter.DoubleToInt64Bits(value) >= 0;

        public static bool IsRealNumber(double value)
        {
            // A NaN will never equal itself so this is an
            // easy and efficient way to check for a real number.

#pragma warning disable CS1718
            return value == value;
#pragma warning restore CS1718
        }

        public static double MaxNumber(double x, double y)
        {
            // This matches the IEEE 754:2019 `maximumNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the larger of the inputs. It
            // treats +0 as larger than -0 as per the specification.

            if (x != y)
            {
                if (!double.IsNaN(y))
                {
                    return y < x ? x : y;
                }

                return x;
            }

            return double.IsNegative(y) ? x : y;
        }

        public static double MinNumber(double x, double y)
        {
            // This matches the IEEE 754:2019 `minimumNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the larger of the inputs. It
            // treats +0 as larger than -0 as per the specification.

            if (x != y)
            {
                if (!double.IsNaN(y))
                {
                    return x < y ? x : y;
                }

                return x;
            }

            return double.IsNegative(x) ? x : y;
        }

        public static double MaxMagnitudeNumber(double x, double y)
        {
            // This matches the IEEE 754:2019 `maximumMagnitudeNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the input with a larger magnitude.
            // It treats +0 as larger than -0 as per the specification.

            double ax = Abs(x);
            double ay = Abs(y);

            if ((ax > ay) || double.IsNaN(ay))
            {
                return x;
            }

            if (ax == ay)
            {
                return double.IsNegative(x) ? y : x;
            }

            return y;
        }

        public static double MinMagnitudeNumber(double x, double y)
        {
            // This matches the IEEE 754:2019 `minimumMagnitudeNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the input with a larger magnitude.
            // It treats +0 as larger than -0 as per the specification.

            double ax = Abs(x);
            double ay = Abs(y);

            if ((ax < ay) || double.IsNaN(ay))
            {
                return x;
            }

            if (ax == ay)
            {
                return double.IsNegative(x) ? x : y;
            }

            return y;
        }

        public static double Clamp(double value, double min, double max)
        {
            if (min > max)
            {
                throw new ArgumentException(string.Format("'{0}' cannot be greater than {1}.", min, max));
            }
            return Min(Max(value, min), max);
        }

        public static double Hypot(double x, double y)
        {
            // This code is based on `hypot` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double result;

            if (double.IsFinite(x) && double.IsFinite(y))
            {
                double ax = Abs(x);
                double ay = Abs(y);

                if (ax == 0.0f)
                {
                    result = ay;
                }
                else if (ay == 0.0f)
                {
                    result = ax;
                }
                else
                {
                    ulong xBits = BitConverter.DoubleToUInt64Bits(ax);
                    ulong yBits = BitConverter.DoubleToUInt64Bits(ay);

                    uint xExp = (uint)((xBits >> BiasedExponentShift) & ShiftedBiasedExponentMask);
                    uint yExp = (uint)((yBits >> BiasedExponentShift) & ShiftedBiasedExponentMask);

                    int expDiff = (int)(xExp - yExp);
                    double expFix = 1.0;

                    if ((expDiff <= (SignificandLength + 1)) && (expDiff >= (-SignificandLength - 1)))
                    {
                        if ((xExp > (ExponentBias + 500)) || (yExp > (ExponentBias + 500)))
                        {
                            // To prevent overflow, scale down by 2^+600
                            expFix = 4.149515568880993E+180;

                            xBits -= 0x2580000000000000;
                            yBits -= 0x2580000000000000;
                        }
                        else if ((xExp < (ExponentBias - 500)) || (yExp < (ExponentBias - 500)))
                        {
                            // To prevent underflow, scale up by 2^-600
                            expFix = 2.409919865102884E-181;

                            xBits += 0x2580000000000000;
                            yBits += 0x2580000000000000;

                            // For subnormal values, do an additional fixing up changing the
                            // adjustment to scale up by 2^601 instead and then subtract a
                            // correction of 2^601 to account for the implicit bit.

                            if (xExp == 0) // x is subnormal
                            {
                                xBits += 0x0010000000000000;

                                ax = BitConverter.UInt64BitsToDouble(xBits);
                                ax -= 9.232978617785736E-128;

                                xBits = BitConverter.DoubleToUInt64Bits(ax);
                            }

                            if (yExp == 0) // y is subnormal
                            {
                                yBits += 0x0010000000000000;

                                ay = BitConverter.UInt64BitsToDouble(yBits);
                                ay -= 9.232978617785736E-128;

                                yBits = BitConverter.DoubleToUInt64Bits(ay);
                            }
                        }

                        ax = BitConverter.UInt64BitsToDouble(xBits);
                        ay = BitConverter.UInt64BitsToDouble(yBits);

                        if (ax < ay)
                        {
                            // Sort so ax is greater than ay
                            double tmp = ax;

                            ax = ay;
                            ay = tmp;

                            ulong tmpBits = xBits;

                            xBits = yBits;
                            yBits = tmpBits;
                        }


                        // Split ax and ay into a head and tail portion

                        double xHead = BitConverter.UInt64BitsToDouble(xBits & 0xFFFF_FFFF_F800_0000);
                        double yHead = BitConverter.UInt64BitsToDouble(yBits & 0xFFFF_FFFF_F800_0000);

                        double xTail = ax - xHead;
                        double yTail = ay - yHead;

                        // Compute (x * x) + (y * y) with extra precision
                        //
                        // This includes taking into account expFix which may
                        // cause an underflow or overflow, but if it does that
                        // will still be the correct result.

                        double xx = ax * ax;
                        double yy = ay * ay;

                        double rHead = xx + yy;
                        double rTail = (xx - rHead) + yy;

                        rTail += (xHead * xHead) - xx;
                        rTail += 2 * xHead * xTail;
                        rTail += xTail * xTail;

                        if (expDiff == 0)
                        {
                            // We only need to do extra accounting when ax and ay have equal exponents

                            rTail += (yHead * yHead) - yy;
                            rTail += 2 * yHead * yTail;
                            rTail += yTail * yTail;
                        }

                        result = Sqrt(rHead + rTail) * expFix;
                    }
                    else
                    {
                        // x or y is insignificant compared to the other
                        result = ax + ay;
                    }
                }
            }
            else if (double.IsInfinity(x) || double.IsInfinity(y))
            {
                // IEEE 754 requires that we return +Infinity
                // even if one of the inputs is NaN

                result = PositiveInfinity;
            }
            else
            {
                // IEEE 754 requires that we return NaN
                // if either input is NaN and neither is Infinity

                result = NaN;
            }

            return result;
        }

        public static double RootN(double x, int n)
        {
            double result;

            if (n > 0)
            {
                if (n == 2)
                {
                    result = (x != 0.0) ? Sqrt(x) : 0.0;
                }
                else if (n == 3)
                {
                    result = Cbrt(x);
                }
                else
                {
                    result = PositiveN(x, n);
                }
            }
            else if (n < 0)
            {
                result = NegativeN(x, n);
            }
            else
            {
                result = NaN;
            }

            return result;

            static double PositiveN(double x, int n)
            {
                double result;

                if (double.IsFinite(x))
                {
                    if (x != 0)
                    {
                        if ((x > 0) || IsOddInteger(n))
                        {
                            result = Pow(Abs(x), 1.0 / n);
                            result = CopySign(result, x);
                        }
                        else
                        {
                            result = NaN;
                        }
                    }
                    else if (IsEvenInteger(n))
                    {
                        result = 0.0;
                    }
                    else
                    {
                        result = CopySign(0.0, x);
                    }
                }
                else if (double.IsNaN(x))
                {
                    result = NaN;
                }
                else if (x > 0)
                {
                    result = PositiveInfinity;
                }
                else
                {
                    result = OddInt(n) ? NegativeInfinity : NaN;
                }

                return result;
            }

            static double NegativeN(double x, int n)
            {
                double result;

                if (double.IsFinite(x))
                {
                    if (x != 0)
                    {
                        if ((x > 0) || IsOddInteger(n))
                        {
                            result = Pow(Abs(x), 1.0 / n);
                            result = CopySign(result, x);
                        }
                        else
                        {
                            result = NaN;
                        }
                    }
                    else if (IsEvenInteger(n))
                    {
                        result = PositiveInfinity;
                    }
                    else
                    {
                        result = CopySign(PositiveInfinity, x);
                    }
                }
                else if (double.IsNaN(x))
                {
                    result = NaN;
                }
                else if (x > 0)
                {
                    result = 0.0;
                }
                else
                {
                    result = OddInt(n) ? -0.0 : NaN;
                }

                return result;
            }
        }

        public static double AcosPi(double x)
        {
            return Acos(x) / Pi;
        }

        public static double AsinPi(double x)
        {
            return Asin(x) / Pi;
        }

        public static double AtanPi(double x)
        {
            return Atan(x) / Pi;
        }

        public static double CosPi(double x)
        {
            // This code is based on `cospi` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double result;

            if (double.IsFinite(x))
            {
                double ax = Abs(x);

                if (ax < 4_503_599_627_370_496.0)           // |x| < 2^52
                {
                    if (ax > 0.25)
                    {
                        long integral = (long)ax;

                        double fractional = ax - integral;
                        double sign = OddInt(integral) ? -1.0 : +1.0;

                        if (fractional <= 0.25)
                        {
                            if (fractional != 0.00)
                            {
                                result = sign * CosForIntervalPiBy4(fractional * Pi, 0.0);
                            }
                            else
                            {
                                result = sign;
                            }
                        }
                        else if (fractional <= 0.50)
                        {
                            if (fractional != 0.50)
                            {
                                result = sign * SinForIntervalPiBy4((0.5 - fractional) * Pi, 0.0);
                            }
                            else
                            {
                                result = 0.0;
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            result = -sign * SinForIntervalPiBy4((fractional - 0.5) * Pi, 0.0);
                        }
                        else
                        {
                            result = -sign * CosForIntervalPiBy4((1.0 - fractional) * Pi, 0.0);
                        }
                    }
                    else if (ax >= 6.103515625E-05)         // |x| >= 2^-14
                    {
                        result = CosForIntervalPiBy4(x * Pi, 0.0);
                    }
                    else if (ax >= 7.450580596923828E-09)   // |x| >= 2^-27
                    {
                        double value = x * Pi;
                        result = 1.0 - (value * value * 0.5);
                    }
                    else
                    {
                        result = 1.0;
                    }
                }
                else if (ax < 9_007_199_254_740_992.0)      // |x| < 2^53
                {
                    // x is an integer
                    long bits = BitConverter.DoubleToInt64Bits(ax);
                    result = OddInt(bits) ? -1.0 : +1.0;
                }
                else
                {
                    // x is an even integer
                    result = 1.0;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        public static (double SinPi, double CosPi) SinCosPi(double x)
        {
            // This code is based on `cospi` and `sinpi` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double sinPi;
            double cosPi;

            if (double.IsFinite(x))
            {
                double ax = Abs(x);

                if (ax < 4_503_599_627_370_496.0)           // |x| < 2^52
                {
                    if (ax > 0.25)
                    {
                        long integral = (long)ax;

                        double fractional = ax - integral;
                        double sign = OddInt(integral) ? -1.0 : +1.0;

                        double sinSign = ((x > 0.0) ? +1.0 : -1.0) * sign;
                        double cosSign = sign;

                        if (fractional <= 0.25)
                        {
                            if (fractional != 0.00)
                            {
                                double value = fractional * Pi;

                                sinPi = sinSign * SinForIntervalPiBy4(value, 0.0);
                                cosPi = cosSign * CosForIntervalPiBy4(value, 0.0);
                            }
                            else
                            {
                                sinPi = x * 0.0;
                                cosPi = cosSign;
                            }
                        }
                        else if (fractional <= 0.50)
                        {
                            if (fractional != 0.50)
                            {
                                double value = (0.5 - fractional) * Pi;

                                sinPi = sinSign * CosForIntervalPiBy4(value, 0.0);
                                cosPi = cosSign * SinForIntervalPiBy4(value, 0.0);
                            }
                            else
                            {
                                sinPi = sinSign;
                                cosPi = 0.0;
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            double value = (fractional - 0.5) * Pi;

                            sinPi = +sinSign * CosForIntervalPiBy4(value, 0.0);
                            cosPi = -cosSign * SinForIntervalPiBy4(value, 0.0);
                        }
                        else
                        {
                            double value = (1.0 - fractional) * Pi;

                            sinPi = +sinSign * SinForIntervalPiBy4(value, 0.0);
                            cosPi = -cosSign * CosForIntervalPiBy4(value, 0.0);
                        }
                    }
                    else if (ax >= 1.220703125E-4)          // |x| >= 2^-13
                    {
                        double value = x * Pi;

                        sinPi = SinForIntervalPiBy4(value, 0.0);
                        cosPi = CosForIntervalPiBy4(value, 0.0);
                    }
                    else if (ax >= 7.450580596923828E-09)   // |x| >= 2^-27
                    {
                        double value = x * Pi;
                        double valueSq = value * value;

                        sinPi = value - (valueSq * value * (1.0 / 6.0));
                        cosPi = 1.0 - (valueSq * 0.5);
                    }
                    else
                    {
                        sinPi = x * Pi;
                        cosPi = 1.0;
                    }
                }
                else if (ax < 9_007_199_254_740_992.0)      // |x| < 2^53
                {
                    // x is an integer
                    sinPi = x * 0.0;

                    long bits = BitConverter.DoubleToInt64Bits(ax);
                    cosPi = OddInt(bits) ? -1.0 : +1.0;
                }
                else
                {
                    // x is an even integer
                    sinPi = x * 0.0;
                    cosPi = 1.0;
                }
            }
            else
            {
                sinPi = NaN;
                cosPi = NaN;
            }

            return (sinPi, cosPi);
        }

        public static double SinPi(double x)
        {
            // This code is based on `sinpi` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double result;

            if (double.IsFinite(x))
            {
                double ax = Abs(x);

                if (ax < 4_503_599_627_370_496.0)           // |x| < 2^52
                {
                    if (ax > 0.25)
                    {
                        long integral = (long)ax;

                        double fractional = ax - integral;
                        double sign = ((x > 0.0) ? +1.0 : -1.0) * (OddInt(integral) ? -1.0 : +1.0);

                        if (fractional <= 0.25)
                        {
                            if (fractional != 0.00)
                            {
                                result = sign * SinForIntervalPiBy4(fractional * Pi, 0.0);
                            }
                            else
                            {
                                result = x * 0.0;
                            }
                        }
                        else if (fractional <= 0.50)
                        {
                            if (fractional != 0.50)
                            {
                                result = sign * CosForIntervalPiBy4((0.5 - fractional) * Pi, 0.0);
                            }
                            else
                            {
                                result = sign;
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            result = sign * CosForIntervalPiBy4((fractional - 0.5) * Pi, 0.0);
                        }
                        else
                        {
                            result = sign * SinForIntervalPiBy4((1.0 - fractional) * Pi, 0.0);
                        }
                    }
                    else if (ax >= 1.220703125E-4)          // |x| >= 2^-13
                    {
                        result = SinForIntervalPiBy4(x * Pi, 0.0);
                    }
                    else if (ax >= 7.450580596923828E-09)   // |x| >= 2^-27
                    {
                        double value = x * Pi;
                        result = value - (value * value * value * (1.0 / 6.0));
                    }
                    else
                    {
                        result = x * Pi;
                    }
                }
                else
                {
                    // x is an integer
                    result = x * 0.0;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        public static double TanPi(double x)
        {
            // This code is based on `tanpi` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double result;

            if (double.IsFinite(x))
            {
                double ax = Abs(x);
                double sign = (x > 0.0) ? +1.0 : -1.0;

                if (ax < 4_503_599_627_370_496.0)           // |x| < 2^52
                {
                    if (ax > 0.25)
                    {
                        long integral = (long)ax;
                        double fractional = ax - integral;

                        if (fractional <= 0.25)
                        {
                            if (fractional != 0.00)
                            {
                                result = sign * TanForIntervalPiBy4(fractional * Pi, 0.0, isReciprocal: false);
                            }
                            else
                            {
                                result = sign * (OddInt(integral) ? -0.0 : +0.0);
                            }
                        }
                        else if (fractional <= 0.50)
                        {
                            if (fractional != 0.50)
                            {
                                result = -sign * TanForIntervalPiBy4((0.5 - fractional) * Pi, 0.0, isReciprocal: true);
                            }
                            else
                            {
                                result = +sign * (OddInt(integral) ? NegativeInfinity : PositiveInfinity);
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            result = +sign * TanForIntervalPiBy4((fractional - 0.5) * Pi, 0.0, isReciprocal: true);
                        }
                        else
                        {
                            result = -sign * TanForIntervalPiBy4((1.0 - fractional) * Pi, 0.0, isReciprocal: false);
                        }
                    }
                    else if (ax >= 6.103515625E-05)         // |x| >= 2^-14
                    {
                        result = TanForIntervalPiBy4(x * Pi, 0.0, isReciprocal: false);
                    }
                    else if (ax >= 7.450580596923828E-09)   // |x| >= 2^-27
                    {
                        double value = x * Pi;
                        result = value + (value * value * value * (1.0 / 3.0));
                    }
                    else
                    {
                        result = x * Pi;
                    }
                }
                else if (ax < 9_007_199_254_740_992.0)      // |x| < 2^53
                {
                    // x is an integer
                    long bits = BitConverter.DoubleToInt64Bits(ax);
                    result = sign * (OddInt(bits) ? -0.0 : +0.0);
                }
                else
                {
                    // x is an even integer
                    result = sign * 0.0;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        private static double CosForIntervalPiBy4(double x, double xTail)
        {
            // This code is based on `cos_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // Taylor series for cos(x) is: 1 - (x^2 / 2!) + (x^4 / 4!) - (x^6 / 6!) ...
            //
            // Then define f(xx) where xx = (x * x)
            // and f(xx) = 1 - (xx / 2!) + (xx^2 / 4!) - (xx^3 / 6!) ...
            //
            // We use a minimax approximation of (f(xx) - 1 + (xx / 2)) / (xx * xx)
            // because this produces an expansion in even powers of x.
            //
            // If xTail is non-zero, we subtract a correction term g(x, xTail) = (x * xTail)
            // to the result, where g(x, xTail) is an approximation to sin(x) * sin(xTail)
            //
            // This is valid because xTail is tiny relative to x.

            const double C1 = +0.41666666666666665390037E-1;        // approx: +1 / 4!
            const double C2 = -0.13888888888887398280412E-2;        // approx: -1 / 6!
            const double C3 = +0.248015872987670414957399E-4;       // approx: +1 / 8!
            const double C4 = -0.275573172723441909470836E-6;       // approx: -1 / 10!
            const double C5 = +0.208761463822329611076335E-8;       // approx: +1 / 12!
            const double C6 = -0.113826398067944859590880E-10;      // approx: -1 / 14!

            double xx = x * x;

            double tmp1 = 0.5 * xx;
            double tmp2 = 1.0 - tmp1;

            double result = C6;

            result = (result * xx) + C5;
            result = (result * xx) + C4;
            result = (result * xx) + C3;
            result = (result * xx) + C2;
            result = (result * xx) + C1;

            result *= (xx * xx);
            result += 1.0 - tmp2 - tmp1 - (x * xTail);
            result += tmp2;

            return result;
        }

        private static double SinForIntervalPiBy4(double x, double xTail)
        {
            // This code is based on `sin_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // Taylor series for sin(x) is x - (x^3 / 3!) + (x^5 / 5!) - (x^7 / 7!) ...
            // Which can be expressed as x * (1 - (x^2 / 3!) + (x^4 /5!) - (x^6 /7!) ...)
            //
            // Then define f(xx) where xx = (x * x)
            // and f(xx) = 1 - (xx / 3!) + (xx^2 / 5!) - (xx^3 / 7!) ...
            //
            // We use a minimax approximation of (f(xx) - 1) / xx
            // because this produces an expansion in even powers of x.
            //
            // If xTail is non-zero, we add a correction term g(x, xTail) = (1 - xx / 2) * xTail
            // to the result, where g(x, xTail) is an approximation to cos(x) * sin(xTail)
            //
            // This is valid because xTail is tiny relative to x.

            const double C1 = -0.166666666666666646259241729;       // approx: -1 / 3!
            const double C2 = +0.833333333333095043065222816E-2;    // approx: +1 / 5!
            const double C3 = -0.19841269836761125688538679E-3;     // approx: -1 / 7!
            const double C4 = +0.275573161037288022676895908448E-5; // approx: +1 / 9!
            const double C5 = -0.25051132068021699772257377197E-7;  // approx: -1 / 11!
            const double C6 = +0.159181443044859136852668200E-9;    // approx: +1 / 13!

            double xx = x * x;
            double xxx = xx * x;

            double result = C6;

            result = (result * xx) + C5;
            result = (result * xx) + C4;
            result = (result * xx) + C3;
            result = (result * xx) + C2;

            if (xTail == 0.0)
            {
                result = (xx * result) + C1;
                result = (xxx * result) + x;
            }
            else
            {
                result = x - ((xx * ((0.5 * xTail) - (xxx * result))) - xTail - (xxx * C1));
            }

            return result;
        }

        private static double TanForIntervalPiBy4(double x, double xTail, bool isReciprocal)
        {
            // This code is based on `tan_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // In order to maintain relative precision transform using the identity:
            //  tan((pi / 4) - x) = (1 - tan(x)) / (1 + tan(x)) for arguments close to (pi / 4).
            //
            // Similarly use tan(x - (pi / 4)) = (tan(x) - 1) / (tan(x) + 1) close to (-pi / 4).

            const double PiBy4Head = 7.85398163397448278999E-01;
            const double PiBy4Tail = 3.06161699786838240164E-17;

            int transform = 0;

            if (x > +0.68)
            {
                transform = 1;
                x = (PiBy4Head - x) + (PiBy4Tail - xTail);
                xTail = 0.0;
            }
            else if (x < -0.68)
            {
                transform = -1;
                x = (PiBy4Head + x) + (PiBy4Tail + xTail);
                xTail = 0.0;
            }

            // Core Remez [2, 3] approximation to tan(x + xTail) on the interval [0, 0.68].

            double tmp1 = (x * x) + (2.0 * x * xTail);

            double denominator = -0.232371494088563558304549252913E-3;
            denominator = +0.260656620398645407524064091208E-1 + (denominator * tmp1);
            denominator = -0.515658515729031149329237816945E+0 + (denominator * tmp1);
            denominator = +0.111713747927937668539901657944E+1 + (denominator * tmp1);

            double numerator = +0.224044448537022097264602535574E-3;
            numerator = -0.229345080057565662883358588111E-1 + (numerator * tmp1);
            numerator = +0.372379159759792203640806338901E+0 + (numerator * tmp1);

            double tmp2 = x * tmp1;
            tmp2 *= numerator / denominator;
            tmp2 += xTail;

            // Reconstruct tan(x) in the transformed case

            double result = x + tmp2;

            if (transform != 0)
            {
                if (isReciprocal)
                {
                    result = (transform * (2 * result / (result - 1))) - 1.0;
                }
                else
                {
                    result = transform * (1.0 - (2 * result / (1 + result)));
                }
            }
            else if (isReciprocal)
            {
                // Compute -1.0 / (x + tmp2) accurately

                ulong bits = BitConverter.DoubleToUInt64Bits(result);
                bits &= 0xFFFFFFFF00000000;

                double z1 = BitConverter.UInt64BitsToDouble(bits);
                double z2 = tmp2 - (z1 - x);

                double reciprocal = -1.0 / result;

                bits = BitConverter.DoubleToUInt64Bits(reciprocal);
                bits &= 0xFFFFFFFF00000000;

                double reciprocalHead = BitConverter.UInt64BitsToDouble(bits);
                result = reciprocalHead + (reciprocal * (1.0 + (reciprocalHead * z1) + (reciprocalHead * z2)));
            }

            return result;
        }


        // ---- Double.cs, the degree/radian conversions ----
        private const double DegreesToRadiansHead = 0.017453292519943295;     // 0x1.1DF46A2529D39p-6
        private const double DegreesToRadiansMid = 2.9486522708701687E-19;    // 0x1.5C1D8BECDD291p-62
        private const double DegreesToRadiansTail = -1.3427726813345382E-35;   // -0x1.1D937FA428858p-116
        private const double RadiansToDegreesHead = 57.29577951308232;        // 0x1.CA5DC1A63C1F8p+5
        private const double RadiansToDegreesMid = -1.9878495670576283E-15;   // -0x1.1E7AB456405F9p-49
        private const double RadiansToDegreesTail = -1.6833394980391744E-31;   // -0x1.B505196FABB41p-103

        // Recovering the roundoff of `value * mid` is what both the two limb and the three limb form
        // rest on, and below these that roundoff falls onto the subnormal grid, losing up to one
        // smallest subnormal. That loss has to stay under the closest an input brings the exact
        // product to a rounding boundary, which is the `2^-55.58` and `2^-55.99` ulp above. Writing
        // the smallest subnormal as `2^(emin - p + 1)` and an ulp of the result as
        // `2^(e + e_head - p + 1)`, the `p` cancels and the floor on the input exponent `e` is just
        // `emin - e_head + w`, giving `-960.42` and `-971.01`.
        private const double DegreesToRadiansMin = 1.0261342003245941E-289;   // 0x1p-960
        private const double RadiansToDegreesMin = 5.010420900022432E-293;    // 0x1p-971

        // At or above these `value * head` overflows, which the triple cannot recover from since the
        // roundoff of an infinite product is `inf - inf`. Paired with the minimums above they bound
        // the domain MultiplyWide covers, which is what the vector paths range test on. `pi / 180`
        // is under one, so nothing finite overflows there and the bound only excludes the non-finite;
        // the single unsigned range test covers that the same as any other value.
        private const double DegreesToRadiansMax = PositiveInfinity;
        private const double RadiansToDegreesMax = 3.137566414384587E+306;    // 0x1.1DF46A2529D39p+1018

        // The cold path lifts `value` by `ConversionScaleUp` so the whole computation runs as it
        // would in the normal range. Scaling by a power of two is exact in both directions, so any
        // exponent in the valid window gives bit-identical results; only the window itself matters.
        //
        // The floor is the larger of two conditions, evaluated for the smallest input `2^(emin-p+1)`
        // and taken over both directions, where `k` is the scale exponent being solved for,
        // `p` is the precision in bits including the implicit leading bit, `emin` is the minimum
        // normal exponent, and `e_head`/`e_mid` are the constants' own exponents. The `p - 1` term
        // is the gap from the smallest subnormal to the smallest normal, so it is the lift needed to
        // bring any input into the normal range:
        //
        //   k >= (p - 1) - e_mid           so `scaled * mid` stays normal
        //   k >= 2 * (p - 1) - e_head      so the smallest Veltkamp sub-product in the no-FMA
        //                                  MultiplyRoundoff stays normal, since it is `2^-(p-1)`
        //                                  relative to `scaled * head`
        //
        // For `double` those give 114 and 110 for degrees-to-radians, 101 and 99 for the reverse,
        // so the floor is `2^114`. The ceiling is `2^1022`, keeping `ConversionScaleDown` normal.
        //
        // `tail` gets no condition. Losing it outright costs `2^-110` relative, and since a result
        // is under `2^p` ulp that is under `2^-57` ulp, which the worst case above already clears.
        //
        // Both conditions are sufficient rather than tight, since an input small enough to violate
        // either produces a result with too few significand bits for what is lost to reach the
        // rounding position.
        private const double ConversionScaleUp = 2.076918743413931E+34;        // 0x1p+114
        private const double ConversionScaleDown = 4.81482486096809E-35;       // 0x1p-114

        // Keeps the top 26 significand bits, which is the Veltkamp split MultiplyRoundoff uses.
        private const ulong ConversionSplitMask = 0xFFFF_FFFF_F800_0000;

        // Bounds the error of the two limb form relative to `value * head`, which is what lets that
        // form tell whether it resolved the rounding. Three things are dropped or rounded away
        // there, none of them reaching `2^-105`:
        //
        //   `value * (C - head - mid)`, which `tail` puts under `2^-108`
        //   the roundoff of `value * mid`, so `2^-53` of a term already under `2^-54`
        //   the roundoff of their sum, so `2^-53` of a sum already under `2^-52`
        //
        // `2^-100` clears the total by more than 16x, leaving room for rounding the ends of the
        // interval it forms as well. A wider bound stays correct and only sends more inputs to the
        // three limb form; at this width that is about one in `2^46`.
        private const double ConversionErrorScale = 7.888609052210118E-31;     // 0x1p-100

        // The roundoff of `value * head` is itself exactly representable, and recovering it is
        // what lets each limb be spent without losing what fell off the end. A fused multiply-add
        // gives it directly; otherwise splitting both operands into 26 and 27 bit halves makes
        // every sub-product exact, which recovers the same value using only multiplies and adds.
        private static double MultiplyRoundoff(double value, double head, double product)
        {
            return FusedMultiplyAdd(value, head, -product);
        }

        // Computes `value * (head + mid + tail)` as the unevaluated pair `result + residual`, where
        // `result` is the correctly rounded product. `value * head` must be known finite and
        // non-zero, which is what lets `head` be the nearest representable value rather than being
        // biased to keep a sign.
        //
        // The three limb products are accumulated exactly apart from the two terms already below
        // `2^-106` relative, then folded back onto `product` in decreasing order of magnitude. The
        // final fold rounds twice, so the correction is first rounded to odd, which is the standard
        // way of making the second rounding agree with rounding the exact value once: the odd
        // neighbour is never a midpoint, so nothing that decides the outcome can be discarded.
        private static double MultiplyWide(double value, double head, double mid, double tail, out double residual)
        {
            double product = value * head;
            double productError = MultiplyRoundoff(value, head, product);   // exact
            double middle = value * mid;
            double middleError = MultiplyRoundoff(value, mid, middle);      // exact but for the
                                                                            // subnormal loss the
                                                                            // minimums bound
            double bottom = middleError + (value * tail);

            // The magnitudes of `productError` and `middle` are not ordered, so the sum needs the
            // form that does not assume it
            double sum = productError + middle;
            double bias = sum - productError;
            double sumError = (productError - (sum - bias)) + (middle - bias);
            double lower = sumError + bottom;

            double upper = product + sum;
            double upperError = sum - (upper - product);                    // exact, |product| >= |sum|

            double correction = upperError + lower;
            double lost = (upperError - correction) + lower;                 // exact

            if ((lost != 0) && ((BitConverter.DoubleToUInt64Bits(correction) & 1) == 0))
            {
                // Consecutive values alternate in significand parity, so exactly one of the two
                // bracketing values is odd and it lies in the direction of what was lost
                correction = (lost > 0) ? BitIncrement(correction) : BitDecrement(correction);
            }

            double result = upper + correction;
            residual = (upper - result) + correction;                       // exact
            return result;
        }

        // Computes `value * (head + mid + tail)` as a single rounding, over the domain the minimums
        // and maximums above bound. Two limbs are enough for all but the rare input, and whether
        // they were is itself decidable: their sum is known to within `bound`, so if both ends of
        // that interval round to the same value then so does the exact product.
        private static double MultiplyWide(double value, double head, double mid, double tail)
        {
            double product = value * head;

            if (!double.IsFinite(product) || (product == 0))
            {
                // The roundoff of an infinite product is `inf - inf`, and a zero product would
                // come back from the sum having lost its sign
                return product;
            }

            double sum = MultiplyRoundoff(value, head, product) + (value * mid);
            double bound = Abs(product) * ConversionErrorScale;
            double result = product + (sum - bound);

            if (result == (product + (sum + bound)))
            {
                return result;
            }

            return MultiplyWide(value, head, mid, tail, out _);
        }

        // Computes `value * (head + mid + tail)` for the inputs too small for MultiplyWide, namely
        // those below the minimums above. Scaling up moves the product back into the range where
        // the triple is resolved exactly; scaling back down then rounds a second time, with
        // `residual` breaking any tie that rounding would otherwise resolve to even.
        private static double ScaleAndMultiplyWide(double value, double head, double mid, double tail)
        {
            double scaled = value * ConversionScaleUp;

            if (scaled == 0)
            {
                // Multiplying keeps the sign of zero, which the sum would lose
                return scaled * head;
            }

            double sum = MultiplyWide(scaled, head, mid, tail, out double residual);
            double result = sum * ConversionScaleDown;

            if (residual == 0)
            {
                return result;
            }

            double dropped = sum - (result * ConversionScaleUp);            // exact

            if (dropped == 0)
            {
                // Scaling back down lost nothing, so it was already the single rounding
                return result;
            }

            double next = (dropped > 0) ? BitIncrement(result) : BitDecrement(result);

            if ((Abs(next - result) * ConversionScaleUp) != (2.0 * Abs(dropped)))
            {
                return result;
            }

            // `sum` sat exactly between `result` and `next`, so the true value did not
            return ((residual > 0) == (dropped > 0)) ? next : result;
        }

        public static double DegreesToRadians(double degrees)
        {
            if (Abs(degrees) < DegreesToRadiansMin)
            {
                return ScaleAndMultiplyWide(degrees, DegreesToRadiansHead, DegreesToRadiansMid, DegreesToRadiansTail);
            }

            return MultiplyWide(degrees, DegreesToRadiansHead, DegreesToRadiansMid, DegreesToRadiansTail);
        }

        public static double RadiansToDegrees(double radians)
        {
            if (Abs(radians) < RadiansToDegreesMin)
            {
                return ScaleAndMultiplyWide(radians, RadiansToDegreesHead, RadiansToDegreesMid, RadiansToDegreesTail);
            }

            return MultiplyWide(radians, RadiansToDegreesHead, RadiansToDegreesMid, RadiansToDegreesTail);
        }

    }
}
