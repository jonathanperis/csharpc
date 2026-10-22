// Spike semantics: throw terminates the process exactly like an unhandled .NET exception
// (message on stderr, abort -> exit status 134). try/catch is on the roadmap.
Console.WriteLine("before");
int[] items = [1, 2, 3];
int index = items.Length;
Console.WriteLine(Divide(10, 2));
if (index > 2) throw new InvalidOperationException("index is too large");
Console.WriteLine("never printed");

static int Divide(int a, int b) => a / b;
