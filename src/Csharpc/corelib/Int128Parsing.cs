// Int128 and UInt128: their formatting and parsing members are the framework's own, hosted from their IL (#426,
// src/Csharpc/upstream-types/System.Globalization.txt); the corelib keeps the internal members that IL reads.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.UInt128")]
    internal static class UInt128Members
    {
        // The halves (internal upstream), which the framework's IL reads (Number.BigInteger.Multiply10).
        [ShadowInstance] public static ulong get_Lower(UInt128 value) => (ulong)value;
        [ShadowInstance] public static ulong get_Upper(UInt128 value) => (ulong)(value >> 64);
    }
}
