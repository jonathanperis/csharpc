// Static System.String members that need generics (the rest of string is runtime/strings.c intrinsics).
#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace CsharpcCoreLib
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    internal sealed class ShadowMembersAttribute : Attribute
    {
        public ShadowMembersAttribute(string name) { }
    }

    [ShadowMembers("System.String")]
    internal static class StringExtras
    {
        public static string Join<T>(string separator, IEnumerable<T> values)
        {
            if (values == null) ThrowHelper.ArgumentNull("values");
            var sb = new StringBuilder();
            bool first = true;
            foreach (T value in values)
            {
                if (!first) sb.Append(separator);
                first = false;
                if (value != null) sb.Append(value.ToString());
            }
            return sb.ToString();
        }

        public static string Join(string separator, IEnumerable<string> values)
        {
            if (values == null) ThrowHelper.ArgumentNull("values");
            var sb = new StringBuilder();
            bool first = true;
            foreach (string value in values)
            {
                if (!first) sb.Append(separator);
                first = false;
                sb.Append(value);
            }
            return sb.ToString();
        }

        public static string Join(char separator, string[] value) => string.Join(separator.ToString(), value);

        public static string Join(string separator, object[] values)
        {
            if (values == null) ThrowHelper.ArgumentNull("values");
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(separator);
                if (values[i] != null) sb.Append(values[i].ToString());
            }
            return sb.ToString();
        }

        public static string Concat<T>(IEnumerable<T> values)
        {
            var sb = new StringBuilder();
            foreach (T value in values)
                if (value != null) sb.Append(value.ToString());
            return sb.ToString();
        }

        public static string Concat(IEnumerable<string> values)
        {
            var sb = new StringBuilder();
            foreach (string value in values) sb.Append(value);
            return sb.ToString();
        }

        public static string Concat(string[] values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++) sb.Append(values[i]);
            return sb.ToString();
        }

        public static int Compare(string strA, string strB) => CoreIntrinsics.Compare(strA, strB);
    }
}

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Double")]
    internal static class DoubleExtras
    {
        public static bool IsNaN(double d) => d != d;
        public static bool IsInfinity(double d) => d == double.PositiveInfinity || d == double.NegativeInfinity;
        public static bool IsPositiveInfinity(double d) => d == double.PositiveInfinity;
        public static bool IsNegativeInfinity(double d) => d == double.NegativeInfinity;
        public static bool IsFinite(double d) => d == d && !IsInfinity(d);
        public static bool IsNegative(double d) => d < 0 || (d == 0 && 1 / d < 0);
        public static bool IsInteger(double d) => IsFinite(d) && d == Math.Floor(d);
    }

    [ShadowMembers("System.Single")]
    internal static class SingleExtras
    {
        public static bool IsNaN(float f) => f != f;
        public static bool IsInfinity(float f) => f == float.PositiveInfinity || f == float.NegativeInfinity;
        public static bool IsPositiveInfinity(float f) => f == float.PositiveInfinity;
        public static bool IsNegativeInfinity(float f) => f == float.NegativeInfinity;
        public static bool IsFinite(float f) => f == f && !IsInfinity(f);
    }
}
