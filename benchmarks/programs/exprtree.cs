// Expression trees: build and Compile() a lambda (System.Linq.Expressions, interpreted as under
// NativeAOT), then call it — mostly here for the compile time and size of programs that use them.
using System.Linq.Expressions;

Expression<Func<long, long, long>> step = (acc, i) => acc + (i % 7 == 0 ? i * 3 : i) - 1;
var f = step.Compile();
long sum = 0;
for (long i = 0; i < 300_000; i++) sum = f(sum, i);
Console.WriteLine(sum);
Console.WriteLine(step);
