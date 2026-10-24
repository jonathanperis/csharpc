// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0, commit bc123d0bd173), src/libraries/System.Private.CoreLib/src/System/Globalization;
// see THIRD-PARTY-NOTICES.md. Adapted to the csharpc corelib (docs/12-corelib.md); changes of substance
// are marked `csharpc:`.
#nullable enable annotations

using System.Globalization;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System.Globalization;
    using System;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;
    internal static class InvariantModeCasing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static char ToLower(char c) => CharUnicodeInfo.ToLower(c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static char ToUpper(char c) => CharUnicodeInfo.ToUpper(c);

        internal static string ToLower(string s)
        {
            if (s.Length == 0)
            {
                return string.Empty;
            }

            ReadOnlySpan<char> source = s;

            int i = 0;
            while (i < s.Length)
            {
                if (char.IsHighSurrogate(source[i]) && i < s.Length - 1 && char.IsLowSurrogate(source[i + 1]))
                {
                    SurrogateCasing.ToLower(source[i], source[i + 1], out char h, out char l);
                    if (source[i] != h || source[i + 1] != l)
                    {
                        break;
                    }

                    i += 2;
                    continue;
                }

                if (ToLower(source[i]) != source[i])
                {
                    break;
                }

                i++;
            }

            if (i >= s.Length)
            {
                return s;
            }

            string result = CoreIntrinsics.FastAllocateString(s.Length);
            var destination = CoreIntrinsics.StringChars(result);
            ReadOnlySpan<char> src = s;
            src.Slice(0, i).CopyTo(destination);
            ToLower(src.Slice(i), destination.Slice(i));

            return result;
        }

        internal static string ToUpper(string s)
        {
            if (s.Length == 0)
            {
                return string.Empty;
            }

            ReadOnlySpan<char> source = s;

            int i = 0;
            while (i < s.Length)
            {
                if (char.IsHighSurrogate(source[i]) && i < s.Length - 1 && char.IsLowSurrogate(source[i + 1]))
                {
                    SurrogateCasing.ToUpper(source[i], source[i + 1], out char h, out char l);
                    if (source[i] != h || source[i + 1] != l)
                    {
                        break;
                    }

                    i += 2;
                    continue;
                }

                if (ToUpper(source[i]) != source[i])
                {
                    break;
                }

                i++;
            }

            if (i >= s.Length)
            {
                return s;
            }

            string result = CoreIntrinsics.FastAllocateString(s.Length);
            var destination = CoreIntrinsics.StringChars(result);
            ReadOnlySpan<char> src = s;
            src.Slice(0, i).CopyTo(destination);
            ToUpper(src.Slice(i), destination.Slice(i));

            return result;
        }

        internal static void ToUpper(ReadOnlySpan<char> source, Span<char> destination)
        {
            Debug.Assert(GlobalizationMode.Invariant);
            Debug.Assert(source.Length <= destination.Length);

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsHighSurrogate(c) && i < source.Length - 1)
                {
                    char cl = source[i + 1];
                    if (char.IsLowSurrogate(cl))
                    {
                        // well formed surrogates
                        SurrogateCasing.ToUpper(c, cl, out char h, out char l);
                        destination[i] = h;
                        destination[i + 1] = l;
                        i++; // skip the low surrogate
                        continue;
                    }
                }

                destination[i] = ToUpper(c);
            }
        }

        internal static void ToLower(ReadOnlySpan<char> source, Span<char> destination)
        {
            Debug.Assert(GlobalizationMode.Invariant);
            Debug.Assert(source.Length <= destination.Length);

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsHighSurrogate(c) && i < source.Length - 1)
                {
                    char cl = source[i + 1];
                    if (char.IsLowSurrogate(cl))
                    {
                        // well formed surrogates
                        SurrogateCasing.ToLower(c, cl, out char h, out char l);
                        destination[i] = h;
                        destination[i + 1] = l;
                        i++; // skip the low surrogate
                        continue;
                    }
                }

                destination[i] = ToLower(c);
            }
        }

        // csharpc: spans instead of refs walked with Unsafe.Add; same comparison.
        private static (uint, int) GetScalar(ReadOnlySpan<char> source, int index)
        {
            char charA = source[index];
            if (!char.IsHighSurrogate(charA) || index >= source.Length - 1)
            {
                return ((uint)charA, 1);
            }

            char charB = source[index + 1];
            if (!char.IsLowSurrogate(charB))
            {
                return ((uint)charA, 1);
            }

            return (UnicodeUtility.GetScalarFromUtf16SurrogatePair(charA, charB), 2);
        }

        internal static int CompareStringIgnoreCase(ReadOnlySpan<char> strA, ReadOnlySpan<char> strB)
        {
            Debug.Assert(GlobalizationMode.Invariant);

            int lengthA = strA.Length;
            int lengthB = strB.Length;
            int length = Math.Min(lengthA, lengthB);

            int index = 0;

            while (index < length)
            {
                (uint codePointA, int codePointLengthA) = GetScalar(strA, index);
                (uint codePointB, int codePointLengthB) = GetScalar(strB, index);

                if (codePointA == codePointB)
                {
                    Debug.Assert(codePointLengthA == codePointLengthB);
                    index += codePointLengthA;
                    continue;
                }

                uint aUpper = CharUnicodeInfo.ToUpper(codePointA);
                uint bUpper = CharUnicodeInfo.ToUpper(codePointB);

                if (aUpper == bUpper)
                {
                    Debug.Assert(codePointLengthA == codePointLengthB);
                    index += codePointLengthA;
                    continue;
                }

                return (int)aUpper - (int)bUpper;
            }

            return lengthA - lengthB;
        }

        internal static unsafe int IndexOfIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
        {
            Debug.Assert(value.Length > 0);
            Debug.Assert(value.Length <= source.Length);
            Debug.Assert(GlobalizationMode.Invariant);

            fixed (char* pSource = source)
            fixed (char* pValue = value)
            {
                char* pSourceLimit = pSource + (source.Length - value.Length);
                char* pValueLimit = pValue + value.Length - 1;
                char* pCurrentSource = pSource;

                while (pCurrentSource <= pSourceLimit)
                {
                    char *pVal = pValue;
                    char *pSrc = pCurrentSource;

                    while (pVal <= pValueLimit)
                    {
                        if (!char.IsHighSurrogate(*pVal) || pVal == pValueLimit)
                        {
                            if (*pVal != *pSrc && ToUpper(*pVal) != ToUpper(*pSrc))
                                break; // no match

                            pVal++;
                            pSrc++;
                            continue;
                        }

                        if (char.IsHighSurrogate(*pSrc) && char.IsLowSurrogate(*(pSrc + 1)) && char.IsLowSurrogate(*(pVal + 1)))
                        {
                            // Well formed surrogates
                            // both the source and the Value have well-formed surrogates.
                            if (!SurrogateCasing.Equal(*pSrc, *(pSrc + 1), *pVal, *(pVal + 1)))
                                break; // no match

                            pSrc += 2;
                            pVal += 2;
                            continue;
                        }

                        if (*pVal != *pSrc)
                            break; // no match

                        pSrc++;
                        pVal++;
                    }

                    if (pVal > pValueLimit)
                    {
                        // Found match.
                        return (int)(pCurrentSource - pSource);
                    }

                    pCurrentSource++;
                }

                return -1;
            }
        }

        internal static unsafe int LastIndexOfIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
        {
            Debug.Assert(value.Length > 0);
            Debug.Assert(value.Length <= source.Length);
            Debug.Assert(GlobalizationMode.Invariant);

            fixed (char* pSource = source)
            fixed (char* pValue = value)
            {
                char* pValueLimit = pValue + value.Length - 1;
                char* pCurrentSource = pSource + (source.Length - value.Length);

                while (pCurrentSource >= pSource)
                {
                    char *pVal = pValue;
                    char *pSrc = pCurrentSource;

                    while (pVal <= pValueLimit)
                    {
                        if (!char.IsHighSurrogate(*pVal) || pVal == pValueLimit)
                        {
                            if (*pVal != *pSrc && ToUpper(*pVal) != ToUpper(*pSrc))
                                break; // no match

                            pVal++;
                            pSrc++;
                            continue;
                        }

                        if (char.IsHighSurrogate(*pSrc) && char.IsLowSurrogate(*(pSrc + 1)) && char.IsLowSurrogate(*(pVal + 1)))
                        {
                            // Well formed surrogates
                            // both the source and the Value have well-formed surrogates.
                            if (!SurrogateCasing.Equal(*pSrc, *(pSrc + 1), *pVal, *(pVal + 1)))
                                break; // no match

                            pSrc += 2;
                            pVal += 2;
                            continue;
                        }

                        if (*pVal != *pSrc)
                            break; // no match

                        pSrc++;
                        pVal++;
                    }

                    if (pVal > pValueLimit)
                    {
                        // Found match.
                        return (int)(pCurrentSource - pSource);
                    }

                    pCurrentSource--;
                }

                return -1;
            }
        }
    }
}
