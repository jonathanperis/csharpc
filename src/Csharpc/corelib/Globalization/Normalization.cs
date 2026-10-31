// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0): System.Private.CoreLib's Normalization.cs and
// Normalization.Icu.cs, string.Normalize/IsNormalized (String.cs) and StringNormalizationExtensions; see
// THIRD-PARTY-NOTICES.md. Unicode normalization is ICU's (unorm2, through the System.Globalization.Native shim
// linked into the program, src/runtime/globalization/pal_normalization.c), as on .NET; invariant mode and
// ASCII text need no ICU. csharpc: the pooled scratch buffers are plain arrays.
#nullable disable

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Text;

    internal static unsafe class Normalization
    {
        private const string InvalidForm = "Invalid or unsupported normalization form.";
        private const string InvalidSequence = "String contains invalid Unicode code points.";

        private static bool IsAscii(ReadOnlySpan<char> s)
        {
            foreach (char c in s) if (c > 0x7F) return false;
            return true;
        }

        private static bool IsMainForm(NormalizationForm form) =>
            form == NormalizationForm.FormC || form == NormalizationForm.FormD || form == NormalizationForm.FormKC || form == NormalizationForm.FormKD;

        private static void CheckNormalizationForm(NormalizationForm form)
        {
            if (!IsMainForm(form)) throw new ArgumentException(InvalidForm, "normalizationForm");
        }

        internal static bool IsNormalized(ReadOnlySpan<char> source, NormalizationForm normalizationForm)
        {
            CheckNormalizationForm(normalizationForm);
            if (GlobalizationMode.Invariant || IsAscii(source)) return true;
            ValidateArguments(source, "source");
            int result;
            fixed (char* src = source)
                result = Interop.Globalization.IsNormalized((int)normalizationForm, src, source.Length);
            if (result == -1) throw new ArgumentException(InvalidSequence, "source");
            return result == 1;
        }

        internal static string Normalize(string strInput, NormalizationForm normalizationForm)
        {
            CheckNormalizationForm(normalizationForm);
            if (GlobalizationMode.Invariant || IsAscii(strInput)) return strInput;
            ValidateArguments(strInput, "strInput");
            char[] buffer = new char[Math.Max(strInput.Length, 16)];
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int length;
                fixed (char* src = strInput)
                fixed (char* dst = buffer)
                    length = Interop.Globalization.NormalizeString((int)normalizationForm, src, strInput.Length, dst, buffer.Length);
                if (length == -1) throw new ArgumentException(InvalidSequence, "strInput");
                if (length <= buffer.Length)
                {
                    ReadOnlySpan<char> normalized = new ReadOnlySpan<char>(buffer, 0, length);
                    return normalized.SequenceEqual(strInput.AsSpan()) ? strInput : new string(normalized);
                }
                if (attempt == 0) buffer = new char[length];
            }
            throw new ArgumentException(InvalidSequence, "strInput");
        }

        internal static bool TryNormalize(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten, NormalizationForm normalizationForm)
        {
            CheckNormalizationForm(normalizationForm);
            if (source.Overlaps(destination))
                throw new ArgumentException("This operation is invalid on overlapping buffers.");
            if (GlobalizationMode.Invariant || IsAscii(source))
            {
                if (source.TryCopyTo(destination))
                {
                    charsWritten = source.Length;
                    return true;
                }
                charsWritten = 0;
                return false;
            }
            if (destination.IsEmpty)
            {
                charsWritten = 0;
                return false;
            }
            ValidateArguments(source, "source");
            int length;
            fixed (char* src = source)
            fixed (char* dst = destination)
                length = Interop.Globalization.NormalizeString((int)normalizationForm, src, source.Length, dst, destination.Length);
            if (length < 0) throw new ArgumentException(InvalidSequence, "source");
            if (length <= destination.Length)
            {
                charsWritten = length;
                return true;
            }
            charsWritten = 0;
            return false;
        }

        internal static int GetNormalizedLength(ReadOnlySpan<char> source, NormalizationForm normalizationForm)
        {
            CheckNormalizationForm(normalizationForm);
            if (GlobalizationMode.Invariant || IsAscii(source)) return source.Length;
            ValidateArguments(source, "source");
            int length;
            fixed (char* src = source)
                length = Interop.Globalization.NormalizeString((int)normalizationForm, src, source.Length, null, 0);
            if (length < 0) throw new ArgumentException(InvalidSequence, "source");
            return length;
        }

        private static void ValidateArguments(ReadOnlySpan<char> s, string paramName)
        {
            if (HasInvalidUnicodeSequence(s)) throw new ArgumentException(InvalidSequence, paramName);
        }

        // Lone surrogates and U+FFFE are invalid (ICU would normalize them silently).
        private static bool HasInvalidUnicodeSequence(ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '\ud800') continue;
                if (c == '￾') return true;
                if (char.IsLowSurrogate(c)) return true;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return true;
                    i++;
                }
            }
            return false;
        }

        internal static string StringNormalize(string s, NormalizationForm normalizationForm)
        {
            // string.Normalize's own fast path: ASCII in one of the four forms is returned as is.
            if (IsAscii(s) && IsMainForm(normalizationForm)) return s;
            return Normalize(s, normalizationForm);
        }

        internal static bool StringIsNormalized(string s, NormalizationForm normalizationForm)
        {
            if (IsAscii(s) && IsMainForm(normalizationForm)) return true;
            return IsNormalized(s, normalizationForm);
        }
    }
}

namespace CsharpcCoreLib
{
    using System;
    using System.Text;

    [ShadowMembers("System.String")]
    internal static class StringNormalizationMembers
    {
        [ShadowInstance]
        public static string Normalize(string s) => Globalization.Normalization.StringNormalize(s, NormalizationForm.FormC);
        [ShadowInstance]
        public static string Normalize(string s, NormalizationForm normalizationForm) => Globalization.Normalization.StringNormalize(s, normalizationForm);
        [ShadowInstance]
        public static bool IsNormalized(string s) => Globalization.Normalization.StringIsNormalized(s, NormalizationForm.FormC);
        [ShadowInstance]
        public static bool IsNormalized(string s, NormalizationForm normalizationForm) => Globalization.Normalization.StringIsNormalized(s, normalizationForm);
    }

    [ShadowMembers("System.StringNormalizationExtensions")]
    internal static class StringNormalizationExtensionsMembers
    {
        public static bool IsNormalized(string strInput)
        {
            ArgumentNullException.ThrowIfNull(strInput);
            return strInput.IsNormalized(NormalizationForm.FormC);
        }

        public static bool IsNormalized(string strInput, NormalizationForm normalizationForm)
        {
            ArgumentNullException.ThrowIfNull(strInput);
            return strInput.IsNormalized(normalizationForm);
        }

        public static bool IsNormalized(ReadOnlySpan<char> source, NormalizationForm normalizationForm = NormalizationForm.FormC) =>
            Globalization.Normalization.IsNormalized(source, normalizationForm);

        public static string Normalize(string strInput)
        {
            ArgumentNullException.ThrowIfNull(strInput);
            return strInput.Normalize(NormalizationForm.FormC);
        }

        public static string Normalize(string strInput, NormalizationForm normalizationForm)
        {
            ArgumentNullException.ThrowIfNull(strInput);
            return strInput.Normalize(normalizationForm);
        }

        public static bool TryNormalize(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten, NormalizationForm normalizationForm = NormalizationForm.FormC) =>
            Globalization.Normalization.TryNormalize(source, destination, out charsWritten, normalizationForm);

        public static int GetNormalizedLength(ReadOnlySpan<char> source, NormalizationForm normalizationForm = NormalizationForm.FormC) =>
            Globalization.Normalization.GetNormalizedLength(source, normalizationForm);
    }
}
