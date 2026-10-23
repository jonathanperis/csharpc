// [Conditional] calls are dropped, arguments and all, unless a symbol is defined at the call site:
// the build's symbols (DEBUG/TRACE in the default configuration) plus this file's #define/#undef.
#define FEATURE_X
#undef TRACE
using System.Diagnostics;

int counter = 0;
int Next() => ++counter;

Log("debug build", Next());          // DEBUG is defined by default (dotnet run / csharpc default)
Feature("feature x", Next());        // #define FEATURE_X above
Trace2("trace", Next());             // TRACE is #undef'd in this file
Never("never", Next());              // NOT_DEFINED: arguments are not evaluated either
Either("either", Next());            // [Conditional] twice: either symbol suffices
new Derived().Hook(Next());          // an override is conditional when the base method is
Debug.WriteLine("Debug.WriteLine prints nothing without a debugger");
Console.WriteLine("counter = " + counter);

[Conditional("DEBUG")] static void Log(string s, int n) => Console.WriteLine("LOG " + s + " " + n);
[Conditional("FEATURE_X")] static void Feature(string s, int n) => Console.WriteLine("FEATURE " + s + " " + n);
[Conditional("TRACE")] static void Trace2(string s, int n) => Console.WriteLine("TRACE " + s + " " + n);
[Conditional("NOT_DEFINED")] static void Never(string s, int n) => Console.WriteLine("NEVER " + s + " " + n);
[Conditional("NOT_DEFINED"), Conditional("FEATURE_X")] static void Either(string s, int n) => Console.WriteLine("EITHER " + s + " " + n);

class Base { [Conditional("NOT_DEFINED")] public virtual void Hook(int n) => Console.WriteLine("base hook " + n); }
class Derived : Base { public override void Hook(int n) => Console.WriteLine("derived hook " + n); }
