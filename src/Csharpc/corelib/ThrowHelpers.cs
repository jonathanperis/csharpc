// The .NET 8+ argument-validation throw helpers (ArgumentNullException.ThrowIfNull,
// ArgumentException.ThrowIfNullOrEmpty, ArgumentOutOfRangeException.ThrowIf*, ObjectDisposedException.ThrowIf).
// paramName is filled in by the user's compiler from [CallerArgumentExpression], so the messages
// name the caller's argument expression exactly like .NET.
#nullable disable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.ArgumentNullException")]
    internal static class ArgumentNullExceptionExtras
    {
        public static void ThrowIfNull(object argument, string paramName)
        {
            if (argument == null) throw new ArgumentNullException(paramName);
        }
    }

    [ShadowMembers("System.ArgumentException")]
    internal static class ArgumentExceptionExtras
    {
        public static void ThrowIfNullOrEmpty(string argument, string paramName)
        {
            if (argument == null) throw new ArgumentNullException(paramName);
            if (argument.Length == 0) throw new ArgumentException("The value cannot be an empty string.", paramName);
        }

        public static void ThrowIfNullOrWhiteSpace(string argument, string paramName)
        {
            if (argument == null) throw new ArgumentNullException(paramName);
            if (string.IsNullOrWhiteSpace(argument))
                throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", paramName);
        }
    }

    [ShadowMembers("System.ArgumentOutOfRangeException")]
    internal static class ArgumentOutOfRangeExceptionExtras
    {
        // .NET formats the values with string.Format, which prints null as "null" (passed explicitly).
        private static string Text<T>(T value) => value == null ? "null" : value.ToString();

        private static void Throw<T>(T value, string paramName, string message)
        {
            // ActualValue (and the "Actual value was X." line of Message) is only present for non-null values.
            if (value == null) throw new ArgumentOutOfRangeException(paramName, message);
            throw new ArgumentOutOfRangeException(paramName, value, message);
        }

        public static void ThrowIfZero<T>(T value, string paramName) where T : INumberBase<T>
        {
            if (T.IsZero(value)) Throw(value, paramName, paramName + " ('" + Text(value) + "') must be a non-zero value.");
        }

        public static void ThrowIfNegative<T>(T value, string paramName) where T : INumberBase<T>
        {
            if (T.IsNegative(value)) Throw(value, paramName, paramName + " ('" + Text(value) + "') must be a non-negative value.");
        }

        public static void ThrowIfNegativeOrZero<T>(T value, string paramName) where T : INumberBase<T>
        {
            if (T.IsNegative(value) || T.IsZero(value))
                Throw(value, paramName, paramName + " ('" + Text(value) + "') must be a non-negative and non-zero value.");
        }

        public static void ThrowIfEqual<T>(T value, T other, string paramName)
        {
            if (EqualityComparer<T>.Default.Equals(value, other))
                Throw(value, paramName, paramName + " ('" + Text(value) + "') must not be equal to '" + Text(other) + "'.");
        }

        public static void ThrowIfNotEqual<T>(T value, T other, string paramName)
        {
            if (!EqualityComparer<T>.Default.Equals(value, other))
                Throw(value, paramName, paramName + " ('" + Text(value) + "') must be equal to '" + Text(other) + "'.");
        }

        public static void ThrowIfGreaterThan<T>(T value, T other, string paramName) where T : IComparable<T>
        {
            if (value.CompareTo(other) > 0)
                Throw(value, paramName, paramName + " ('" + Text(value) + "') must be less than or equal to '" + Text(other) + "'.");
        }

        public static void ThrowIfGreaterThanOrEqual<T>(T value, T other, string paramName) where T : IComparable<T>
        {
            if (value.CompareTo(other) >= 0)
                Throw(value, paramName, paramName + " ('" + Text(value) + "') must be less than '" + Text(other) + "'.");
        }

        public static void ThrowIfLessThan<T>(T value, T other, string paramName) where T : IComparable<T>
        {
            if (value.CompareTo(other) < 0)
                Throw(value, paramName, paramName + " ('" + Text(value) + "') must be greater than or equal to '" + Text(other) + "'.");
        }

        public static void ThrowIfLessThanOrEqual<T>(T value, T other, string paramName) where T : IComparable<T>
        {
            if (value.CompareTo(other) <= 0)
                Throw(value, paramName, paramName + " ('" + Text(value) + "') must be greater than '" + Text(other) + "'.");
        }
    }

    /// <summary>
    /// The body of a user `extern` method that is neither a P/Invoke nor an [UnsafeAccessor]: .NET compiles
    /// such a program (warning CS0626) and throws when the method is called.
    /// </summary>
    internal static class BodylessExtern
    {
        // The HRESULT text as .NET prints it: on its own line on Unix, after the message on Windows.
        public static void Throw() =>
#if CSHARPC_TARGET_WINDOWS
            throw new BadImageFormatException("An attempt was made to load a program with an incorrect format. (0x8007000B)");
#else
            throw new BadImageFormatException("An attempt was made to load a program with an incorrect format.\n (0x8007000B)");
#endif
    }

    [ShadowMembers("System.ObjectDisposedException")]
    internal static class ObjectDisposedExceptionExtras
    {
        public static void ThrowIf(bool condition, object instance)
        {
            if (condition) throw new ObjectDisposedException(instance == null ? null : instance.GetType().FullName);
        }

        public static void ThrowIf(bool condition, Type type)
        {
            if (condition) throw new ObjectDisposedException(type == null ? null : type.FullName);
        }
    }
}
