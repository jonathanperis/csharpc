// System.Math's internal throw helpers, which csharpc's Clamp lowering (FunctionCodeGen.ClampBounds.cs) and the integer types'
// generic-math IL (Clamp, Abs; upstream-types/System.IntegerMembers.txt) call. The floating-point members of float, double, Math
// and MathF come from CoreLib's IL (upstream-types/System.Math.txt); what .NET reaches through the C runtime (sin, pow, ...) is
// mapped to libm in CodeGen/Intrinsics.cs, as are the JIT-expanded members whose IL only calls itself (ReciprocalEstimate,
// ReciprocalSqrtEstimate, MultiplyAddEstimate).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Math")]
    internal static class MathMoreMembers
    {
        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        public static void ThrowMinMaxException<T>(T min, T max) => throw new ArgumentException("'" + min + "' cannot be greater than " + max + ".");
        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        public static void ThrowNegateTwosCompOverflow() => throw new OverflowException("Negating the minimum value of a twos complement number is invalid.");
    }
}
