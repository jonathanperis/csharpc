// BitOperations on nuint (native-width) values (#389): on wasm32 nuint is 32 bits wide, and Log2 subtracted the leading-zero count from
// 63 there (a 64-bit width hard-coded for nuint), 32 too high, with no diagnostic (#382: the 32-bit CoreLib's Dragon4 calls it). Only
// operations whose results do not depend on the pointer size for values below 2^32 are printed, so one recording holds on every target.
// (nint/nuint generic-math statics such as nuint.Log2 are not supported by csharpc yet: #391.)
using System.Numerics;

nuint[] values = [1, 2, 3, 7, 8, 1000, 65535, 65536, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 123456789];
foreach (var v in values)
    Console.WriteLine($"{v}: Log2={BitOperations.Log2(v)} PopCount={BitOperations.PopCount(v)} TrailingZeros={BitOperations.TrailingZeroCount(v)} IsPow2={BitOperations.IsPow2(v)}");
Console.WriteLine($"Log2(0)={BitOperations.Log2((nuint)0)}");
