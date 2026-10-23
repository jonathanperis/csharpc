// .NET 8+ argument throw helpers: exact messages with [CallerArgumentExpression] parameter names;
// ObjectDisposedException's "Object name: 'X'." line.
using System;
static void T(Action a)
{
    try { a(); Console.WriteLine("no throw"); }
    catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.GetType().Name}|{e.ParamName}|{e.Message}"); }
    catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}|{e.ParamName}|{e.Message}"); }
    catch (ObjectDisposedException e) { Console.WriteLine($"{e.GetType().Name}|{e.Message}"); }
}
string? s = null; string empty = ""; string ws = " \t"; object? o = null;
int n = -3; decimal d = 0m; double x = 2.5; long big = 10;
T(() => ArgumentNullException.ThrowIfNull(o));
T(() => ArgumentNullException.ThrowIfNull(s, "custom"));
T(() => ArgumentNullException.ThrowIfNull("ok"));
T(() => ArgumentException.ThrowIfNullOrEmpty(s));
T(() => ArgumentException.ThrowIfNullOrEmpty(empty));
T(() => ArgumentException.ThrowIfNullOrWhiteSpace(ws));
T(() => ArgumentException.ThrowIfNullOrWhiteSpace(s));
T(() => ArgumentException.ThrowIfNullOrWhiteSpace("a"));
T(() => ArgumentOutOfRangeException.ThrowIfNegative(n));
T(() => ArgumentOutOfRangeException.ThrowIfNegative(-0.5));
T(() => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(d));
T(() => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(-1.5m));
T(() => ArgumentOutOfRangeException.ThrowIfZero(0L));
T(() => ArgumentOutOfRangeException.ThrowIfZero(x - 2.5));
T(() => ArgumentOutOfRangeException.ThrowIfGreaterThan(big, 5));
T(() => ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, 2.5));
T(() => ArgumentOutOfRangeException.ThrowIfLessThan(n, 0));
T(() => ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(n, -3));
T(() => ArgumentOutOfRangeException.ThrowIfEqual(n, -3));
T(() => ArgumentOutOfRangeException.ThrowIfNotEqual(n, 4));
T(() => ArgumentOutOfRangeException.ThrowIfEqual("a", "a"));
T(() => ArgumentOutOfRangeException.ThrowIfNotEqual<string?>(null, "a"));
T(() => ArgumentOutOfRangeException.ThrowIfGreaterThan("b", "a"));
T(() => ArgumentOutOfRangeException.ThrowIfNegative(double.NaN));
T(() => ArgumentOutOfRangeException.ThrowIfNegative(-0.0));
T(() => ArgumentOutOfRangeException.ThrowIfZero(-0.0));
T(() => ArgumentOutOfRangeException.ThrowIfNegative((short)-1));
T(() => ObjectDisposedException.ThrowIf(true, new System.Text.StringBuilder()));
T(() => ObjectDisposedException.ThrowIf(true, typeof(System.IO.MemoryStream)));
T(() => ObjectDisposedException.ThrowIf(false, o!));
T(() => ObjectDisposedException.ThrowIf(true, new Foo()));
T(() => throw new ObjectDisposedException("x", "msg"));
T(() => throw new ObjectDisposedException(null));
class Foo { }
