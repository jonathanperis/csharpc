// Number formatting and parsing are CoreLib's own (System.Number, served from the framework's IL with the rest of globalization: #426,
// src/Csharpc/upstream-types/System.Globalization.txt). The primitives' public ToString/TryFormat/Parse/TryParse members are hosted from
// their IL; these accessors are the entry points the compiler's own paths (string concatenation, interpolation, boxed values, Console.Write)
// reach through the corelib's Number class. Bound at compile time (CodeGen/FunctionCodeGen.UnsafeAccessor.cs); the signatures are CoreLib's.
#nullable disable
using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    internal static class UpstreamNumber
    {
        private const string Type = "CsharpcCoreLib.Upstream.System.Number, CsharpcUpstream";

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Int32ToDecStr")]
        internal static extern string Int32ToDecStr([UnsafeAccessorType(Type)] object type, int value);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "UInt32ToDecStr")]
        internal static extern string UInt32ToDecStr([UnsafeAccessorType(Type)] object type, uint value);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Int64ToDecStr")]
        internal static extern string Int64ToDecStr([UnsafeAccessorType(Type)] object type, long value);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "UInt64ToDecStr")]
        internal static extern string UInt64ToDecStr([UnsafeAccessorType(Type)] object type, ulong value);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FormatInt32")]
        internal static extern string FormatInt32([UnsafeAccessorType(Type)] object type, int value, int hexMask, string format, IFormatProvider provider);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FormatUInt32")]
        internal static extern string FormatUInt32([UnsafeAccessorType(Type)] object type, uint value, string format, IFormatProvider provider);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FormatInt64")]
        internal static extern string FormatInt64([UnsafeAccessorType(Type)] object type, long value, string format, IFormatProvider provider);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FormatUInt64")]
        internal static extern string FormatUInt64([UnsafeAccessorType(Type)] object type, ulong value, string format, IFormatProvider provider);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FormatInt128")]
        internal static extern string FormatInt128([UnsafeAccessorType(Type)] object type, Int128 value, string format, IFormatProvider provider);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FormatUInt128")]
        internal static extern string FormatUInt128([UnsafeAccessorType(Type)] object type, UInt128 value, string format, IFormatProvider provider);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FormatFloat")]
        internal static extern string FormatFloat<TNumber>([UnsafeAccessorType(Type)] object type, TNumber value, string format, NumberFormatInfo info);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FormatDecimal")]
        internal static extern string FormatDecimal([UnsafeAccessorType(Type)] object type, decimal value, ReadOnlySpan<char> format, NumberFormatInfo info);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ParseBinaryInteger")]
        internal static extern TInteger ParseBinaryInteger<TChar, TInteger>([UnsafeAccessorType(Type)] object type, ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ParseFloat")]
        internal static extern TFloat ParseFloat<TChar, TFloat>([UnsafeAccessorType(Type)] object type, ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info);
    }
}
