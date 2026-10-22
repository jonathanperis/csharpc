// Integer division by zero throws (it's undefined behaviour in LLVM, so csharpc checks).
int zero = args.Length;
Console.WriteLine(1.0 / zero);
Console.WriteLine(10 / zero);
