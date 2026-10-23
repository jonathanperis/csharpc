// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// BitConverter.ToString(byte[]...) ported from dotnet/runtime (release/11.0),
// src/libraries/System.Private.CoreLib/src/System/BitConverter.cs; see THIRD-PARTY-NOTICES.md.
// The other BitConverter members are intrinsics (CodeGen/Intrinsics.cs).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.BitConverter")]
    internal static class BitConverterExtras
    {
        public static string ToString(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");
            return ToString(value, 0, value.Length);
        }

        public static string ToString(byte[] value, int startIndex)
        {
            if (value == null) throw new ArgumentNullException("value");
            return ToString(value, startIndex, value.Length - startIndex);
        }

        public static string ToString(byte[] value, int startIndex, int length)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (startIndex < 0 || startIndex >= value.Length && startIndex > 0)
                throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than the size of the collection.");
            ThrowHelper.IfNegative(length, "length");
            if (startIndex > value.Length - length)
                throw new ArgumentException("The array starting from the specified index is not long enough to read a value of the specified type.", "value");
            if (length == 0) return string.Empty;
            // (int.MaxValue / 3) == 715,827,882 Bytes == 699 MB
            ThrowHelper.IfGreaterThan(length, int.MaxValue / 3, "length");

            string result = CoreIntrinsics.FastAllocateString(length * 3 - 1);
            Span<char> dst = CoreIntrinsics.StringChars(result);
            int j = 0;
            for (int i = startIndex; i < startIndex + length; i++)
            {
                byte b = value[i];
                if (j > 0) dst[j++] = '-';
                dst[j++] = ToCharUpper(b >> 4);
                dst[j++] = ToCharUpper(b);
            }
            return result;
        }

        private static char ToCharUpper(int value)
        {
            value &= 0xF;
            return (char)(value < 10 ? '0' + value : 'A' + value - 10);
        }
    }
}
