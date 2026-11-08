// (decimal)Int128 and (decimal)UInt128: exact up to 96 bits of magnitude, OverflowException beyond; also through
// a compiled expression tree's Convert.
using System;
using System.Globalization;
using System.Linq.Expressions;

Int128 DecimalMax = Int128.Parse("79228162514264337593543950335", CultureInfo.InvariantCulture); // decimal.MaxValue
Int128[] signed = [0, 1, -1, 12345678901234567890, -(Int128)12345678901234567890, (Int128)ulong.MaxValue + 1, DecimalMax, -DecimalMax,
    DecimalMax + 1, Int128.MaxValue, Int128.MinValue];
foreach (Int128 v in signed)
{
    try { Console.WriteLine(v.ToString(CultureInfo.InvariantCulture) + " -> " + ((decimal)v).ToString(CultureInfo.InvariantCulture)); }
    catch (OverflowException e) { Console.WriteLine(v.ToString(CultureInfo.InvariantCulture) + " -> " + e.GetType().Name + ": " + e.Message); }
}
UInt128[] unsigned = [0, 1, ulong.MaxValue, (UInt128)ulong.MaxValue + 1, (UInt128)DecimalMax, (UInt128)DecimalMax + 1, UInt128.MaxValue];
foreach (UInt128 v in unsigned)
{
    try { Console.WriteLine(v.ToString(CultureInfo.InvariantCulture) + " -> " + ((decimal)v).ToString(CultureInfo.InvariantCulture)); }
    catch (OverflowException e) { Console.WriteLine(v.ToString(CultureInfo.InvariantCulture) + " -> " + e.GetType().Name + ": " + e.Message); }
}

// Through an expression tree's Convert, compiled.
var x = Expression.Parameter(typeof(Int128), "x");
var convert = Expression.Lambda<Func<Int128, decimal>>(Expression.Convert(x, typeof(decimal)), x).Compile();
Console.WriteLine(convert((Int128)(-42)).ToString(CultureInfo.InvariantCulture));
