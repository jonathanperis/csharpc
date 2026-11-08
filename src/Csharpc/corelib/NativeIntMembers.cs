// nint/nuint instance members: ToString with a format, and the width conversions. The text is the
// 64-bit integer's (csharpc's targets have 64- and 32-bit pointers; the 32-bit int formats the same).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.IntPtr")]
    internal static class IntPtrMembers
    {
        [ShadowInstance] public static string ToString(nint value, string format) => ((long)value).ToString(format);
        [ShadowInstance] public static string ToString(nint value, IFormatProvider provider) => ((long)value).ToString(provider);
        [ShadowInstance] public static string ToString(nint value, string format, IFormatProvider provider) => ((long)value).ToString(format, provider);
        [ShadowInstance] public static long ToInt64(nint value) => value;
        [ShadowInstance] public static int ToInt32(nint value) => checked((int)value);
        [ShadowInstance] public static unsafe void* ToPointer(nint value) => (void*)value;
    }

    [ShadowMembers("System.UIntPtr")]
    internal static class UIntPtrMembers
    {
        [ShadowInstance] public static string ToString(nuint value, string format) => ((ulong)value).ToString(format);
        [ShadowInstance] public static string ToString(nuint value, IFormatProvider provider) => ((ulong)value).ToString(provider);
        [ShadowInstance] public static string ToString(nuint value, string format, IFormatProvider provider) => ((ulong)value).ToString(format, provider);
        [ShadowInstance] public static ulong ToUInt64(nuint value) => value;
        [ShadowInstance] public static uint ToUInt32(nuint value) => checked((uint)value);
        [ShadowInstance] public static unsafe void* ToPointer(nuint value) => (void*)value;
    }
}
