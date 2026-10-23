// Library members the IL front-end needed (compiled C# calls them), also reachable from source:
// DefaultInterpolatedStringHandler, Delegate.Combine/Remove, string.Format with a provider,
// type tests against delegate types, and IComparable/IEquatable through a constrained T = primitive.
using System.Globalization;
using System.Runtime.CompilerServices;

var handler = new DefaultInterpolatedStringHandler(8, 2);
handler.AppendLiteral("x = ");
handler.AppendFormatted(42);
handler.AppendLiteral(", y = ");
handler.AppendFormatted(1.5, "F3");
handler.AppendFormatted("|", 4);
Console.WriteLine(handler.ToStringAndClear());

Action a = () => Console.WriteLine("a");
Action b = () => Console.WriteLine("b");
var both = (Action)Delegate.Combine(a, b)!;
both();
((Action)Delegate.Remove(both, a)!)();
Console.WriteLine(Delegate.Remove(a, a) is null);

Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F2} and {1}", 2.5, "text"));
Console.WriteLine(string.Format(null, "{0}-{1}-{2}", 1, 2, 3));

object f = new Func<int>(() => 7);
Console.WriteLine($"{f is Func<int>} {f is Action} {f is Func<int, int>}");
object handlerObject = new EventHandler((_, _) => { });
Console.WriteLine(handlerObject is EventHandler);

Console.WriteLine(CompareBoxed(5, 3) + " " + CompareBoxed(2.5, 2.5) + " " + CompareBoxed('a', 'b'));
Console.WriteLine(Same(4L, 4L) + " " + Same(true, false));

static int CompareBoxed<T>(T value, object other) where T : IComparable => value.CompareTo(other);
static bool Same<T>(T x, T y) where T : IEquatable<T> => x.Equals(y);
