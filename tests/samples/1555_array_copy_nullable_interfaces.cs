// Array.Copy between Nullable<T>[] and reference arrays (#365): the CLR assigns Nullable<T> only to object and ValueType, not to
// T's interfaces as C#'s boxing conversion does, so int?[] <-> IComparable[] throws ArrayTypeMismatchException.
using System;
static class Program
{
    static void T<S, D>(string name, S[] s)
    {
        var d = new D[s.Length];
        try { Array.Copy(s, d, s.Length); Console.WriteLine($"{name}: {string.Join(",", d)}"); }
        catch (Exception e) { Console.WriteLine($"{name}: {e.GetType().Name}"); }
    }
    static void Main()
    {
        T<int?, object>("int?->object", new int?[] { 1, null });
        T<int?, ValueType>("int?->ValueType", new int?[] { 1, null });
        T<int?, IComparable>("int?->IComparable", new int?[] { 1, null });
        T<int?, IFormattable>("int?->IFormattable", new int?[] { 2 });
        T<int?, IEquatable<int>>("int?->IEquatable<int>", new int?[] { 2 });
        T<object, int?>("object->int?", new object[] { 1, null! });
        T<ValueType, int?>("ValueType->int?", new ValueType[] { 1 });
        T<IComparable, int?>("IComparable->int?", new IComparable[] { 1 });
        T<IFormattable, int?>("IFormattable->int?", new IFormattable[] { 1 });
        T<int, IComparable>("int->IComparable", new[] { 3 });
        T<IComparable, int>("IComparable->int", new IComparable[] { 3 });
    }
}
