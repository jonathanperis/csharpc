// Internal compiler error: a floating-point constant pattern against an `object` operand produces
// invalid LLVM IR ("Both operands to ICmp instruction are not of the same type!
//   icmp eq ptr %x, double 0xFFF8000000000000"): the operand is never type-tested/unboxed.
// .NET: True False True
// Found while minimizing the NaN constant-pattern bug (nan_constant_pattern.cs).
using System;

object o = double.NaN;
Console.WriteLine(o is double.NaN);
object p = 1.5;
Console.WriteLine(p is 2.5);
Console.WriteLine(p is 1.5);
