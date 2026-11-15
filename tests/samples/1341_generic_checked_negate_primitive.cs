// checked(-x) through a type parameter that is a signed primitive: an overflow only at MinValue. csharpc used to test the subtraction
// as unsigned (the operand's unsubstituted type T), so checked(-41) threw OverflowException.
using System;
using System.Numerics;

static class Program
{
    static T CheckedNegate<T>(T x) where T : IUnaryNegationOperators<T, T> => checked(-x);

    static void Try<T>(T x) where T : IUnaryNegationOperators<T, T>, IMinMaxValue<T>
    {
        try { Console.WriteLine($"{typeof(T).Name} -({x}) = {CheckedNegate(x)}"); }
        catch (OverflowException e) { Console.WriteLine($"{typeof(T).Name} -({x}): {e.GetType().Name}"); }
    }

    static void Main()
    {
        Try(41); Try(-41); Try(0); Try(int.MaxValue); Try(int.MinValue);
        Try(7L); Try(long.MinValue);
        Try((short)-3); Try(short.MinValue);
        Try((sbyte)100); Try(sbyte.MinValue);
        Try((nint)12);
    }
}
