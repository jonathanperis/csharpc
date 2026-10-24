// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Support types for the System.Globalization port (dotnet/runtime release/11.0, commit bc123d0bd173; see
// THIRD-PARTY-NOTICES.md): small stand-ins for the internal CoreLib helpers the ported files call.
#nullable enable annotations

using System;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    /// <summary>
    /// Debug.Assert/Fail compiled out: the corelib is parsed with the user's DEBUG symbol, and the asserts
    /// only document upstream's invariants. (Type lookup finds this before System.Diagnostics.Debug.)
    /// </summary>
    internal static class Debug
    {
        [System.Diagnostics.Conditional("CSHARPC_GLOBALIZATION_ASSERTS")]
        public static void Assert(bool condition) { }

        [System.Diagnostics.Conditional("CSHARPC_GLOBALIZATION_ASSERTS")]
        public static void Assert(bool condition, string? message) { }

        [System.Diagnostics.Conditional("CSHARPC_GLOBALIZATION_ASSERTS")]
        public static void Fail(string? message) { }
    }

    internal enum ExceptionArgument { count, options, prefix, source, startIndex, suffix, value }

    internal enum ExceptionResource { ArgumentOutOfRange_Count, ArgumentOutOfRange_IndexMustBeLess, ArgumentOutOfRange_IndexMustBeLessOrEqual, Argument_InvalidFlag }

    /// <summary>The members of CoreLib's System.ThrowHelper the port calls (same exceptions and messages).</summary>
    internal static class ThrowHelper
    {
        private static string GetResourceString(ExceptionResource resource) => resource switch
        {
            ExceptionResource.ArgumentOutOfRange_Count => SR.ArgumentOutOfRange_Count,
            ExceptionResource.ArgumentOutOfRange_IndexMustBeLess => SR.ArgumentOutOfRange_IndexMustBeLess,
            ExceptionResource.ArgumentOutOfRange_IndexMustBeLessOrEqual => SR.ArgumentOutOfRange_IndexMustBeLessOrEqual,
            _ => SR.Argument_InvalidFlag,
        };

        internal static void ThrowArgumentException(ExceptionResource resource, ExceptionArgument argument) =>
            throw new ArgumentException(GetResourceString(resource), argument.ToString());

        internal static void ThrowArgumentException_DestinationTooShort() =>
            throw new ArgumentException(SR.Argument_DestinationTooShort, "destination");

        internal static void ThrowArgumentNullException(ExceptionArgument argument) =>
            throw new ArgumentNullException(argument.ToString());

        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument, ExceptionResource resource) =>
            throw new ArgumentOutOfRangeException(argument.ToString(), GetResourceString(resource));

        internal static void ThrowCountArgumentOutOfRange_ArgumentOutOfRange_Count() =>
            throw new ArgumentOutOfRangeException("count", SR.ArgumentOutOfRange_Count);

        internal static void ThrowArgumentOutOfRange_Month(int month) =>
            throw new ArgumentOutOfRangeException(nameof(month), month, SR.ArgumentOutOfRange_Month);

        internal static void ThrowArgumentOutOfRange_Range<T>(string parameterName, T value, T minInclusive, T maxInclusive) =>
            throw new ArgumentOutOfRangeException(parameterName, value, SR.Format(SR.ArgumentOutOfRange_Range, minInclusive, maxInclusive));
    }

    /// <summary>System.NullableBool (CoreLib-internal).</summary>
    internal enum NullableBool : sbyte
    {
        Undefined = 0,
        False = -1,
        True = 1,
    }
}
