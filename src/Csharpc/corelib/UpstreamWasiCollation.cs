// Culture-aware string comparison on WASI (#424, #426). WASI has no ICU, so globalization runs in invariant mode there, where .NET's
// CompareInfo turns a linguistic comparison ordinal ("Banana" < "apple"). csharpc approximates ICU's root collation instead
// (csrt_root_collation_compare, src/runtime/bclmisc.c) for comparisons with no option or IgnoreCase, so sorting Latin text agrees with
// .NET with ICU, as it did before CompareInfo came from the framework's IL. Only the slice wasm32 uses (the slicer's `--wasi`, cut from the
// 32-bit CoreLib) points CompareInfo.Compare(ReadOnlySpan<char>, ReadOnlySpan<char>, CompareOptions) here; every other target runs
// CoreLib's own. This is that method's IL step for step, with the root collation in the invariant branch.
#nullable disable
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GlobalizationMode = CsharpcCoreLib.Globalization.GlobalizationMode;

namespace CsharpcCoreLib
{
    public static class UpstreamWasiCollation
    {
        private const string OrdinalType = "CsharpcCoreLib.Upstream.System.Globalization.Ordinal, CsharpcUpstream";

        // CompareInfo.ValidCompareMaskOffFlags: ~(IgnoreCase | IgnoreSymbols | IgnoreNonSpace | IgnoreWidth | IgnoreKanaType | StringSort).
        private const CompareOptions ValidCompareMaskOffFlags = (CompareOptions)(-536870976);

        public static int Compare(object self, ReadOnlySpan<char> string1, ReadOnlySpan<char> string2, CompareOptions options)
        {
            var compareInfo = (CompareInfo)self;
            if (string1 == string2)
            {
                CheckCompareOptionsForCompare(null, options);
                return 0;
            }
            if ((options & ValidCompareMaskOffFlags) == 0)
            {
                if (!GlobalizationMode.Invariant) return CompareStringCore(compareInfo, string1, string2, options);
                if ((options & ~CompareOptions.IgnoreCase) == 0)
                    return RootCollationCompare(CoreIntrinsics.ObjectToPointer(string1.ToString()), CoreIntrinsics.ObjectToPointer(string2.ToString()),
                        (options & CompareOptions.IgnoreCase) != 0 ? 1 : 0);
                if ((options & CompareOptions.IgnoreCase) == 0) return string1.SequenceCompareTo(string2);
                return CompareStringIgnoreCase(null, ref MemoryMarshal.GetReference(string1), string1.Length, ref MemoryMarshal.GetReference(string2), string2.Length);
            }
            if (options == CompareOptions.Ordinal) return string1.SequenceCompareTo(string2);
            if (options == CompareOptions.OrdinalIgnoreCase)
                return CompareStringIgnoreCase(null, ref MemoryMarshal.GetReference(string1), string1.Length, ref MemoryMarshal.GetReference(string2), string2.Length);
            ThrowCompareOptionsCheckFailed(null, options);
            return -1;
        }

        [DllImport("__Internal", EntryPoint = "csrt_root_collation_compare")]
        private static extern int RootCollationCompare(IntPtr string1, IntPtr string2, int ignoreCase);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "CompareStringCore")]
        private static extern int CompareStringCore(CompareInfo compareInfo, ReadOnlySpan<char> string1, ReadOnlySpan<char> string2, CompareOptions options);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "CheckCompareOptionsForCompare")]
        private static extern void CheckCompareOptionsForCompare(CompareInfo type, CompareOptions options);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ThrowCompareOptionsCheckFailed")]
        private static extern void ThrowCompareOptionsCheckFailed(CompareInfo type, CompareOptions options);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "CompareStringIgnoreCase")]
        private static extern int CompareStringIgnoreCase([UnsafeAccessorType(OrdinalType)] object type, ref char strA, int lengthA, ref char strB, int lengthB);
    }
}
