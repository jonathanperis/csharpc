// System.Collections.StructuralComparisons: dotnet/runtime's comparers (StructuralComparisons.cs),
// which delegate to IStructuralEquatable / IStructuralComparable and otherwise to Equals / Comparer.Default.
#nullable disable
using System.Collections;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.StructuralComparisons")]
    public static class StructuralComparisonsImpl
    {
        private static volatile IComparer s_StructuralComparer;
        private static volatile IEqualityComparer s_StructuralEqualityComparer;

        public static IComparer StructuralComparer => s_StructuralComparer ??= new StructuralComparerImpl();

        public static IEqualityComparer StructuralEqualityComparer => s_StructuralEqualityComparer ??= new StructuralEqualityComparerImpl();
    }

    internal sealed class StructuralEqualityComparerImpl : IEqualityComparer
    {
        public new bool Equals(object x, object y)
        {
            if (x != null)
            {
                if (x is IStructuralEquatable seObj) return seObj.Equals(y, this);
                if (y != null) return x.Equals(y);
                return false;
            }
            if (y != null) return false;
            return true;
        }

        public int GetHashCode(object obj)
        {
            if (obj == null) return 0;
            if (obj is IStructuralEquatable seObj) return seObj.GetHashCode(this);
            return obj.GetHashCode();
        }
    }

    internal sealed class StructuralComparerImpl : IComparer
    {
        public int Compare(object x, object y)
        {
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;
            if (x is IStructuralComparable scX) return scX.CompareTo(y, this);
            return Comparer.Default.Compare(x, y);
        }
    }
}
