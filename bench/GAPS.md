# Compiler gaps found while building the benchmark suite

Probed on branch `bench` (based on `main` @ ec75bdb) with one-line repros, each compared
against `dotnet run` on .NET 11 RC2. Ordered by priority.

## P0 — internal compiler errors (crash instead of a diagnostic)

| # | Construct | Symptom | Minimal repro |
|---|---|---|---|
| 1 | **Non-`static` local function** (even one that captures nothing) | `NullReferenceException` in `FunctionCodeGen.EmitValue` (Expressions.cs:13), reached from `EmitInvocation`: `EmitValue(inv.Instance!)` gets a null instance because the local function's `IsStatic` is false | `Console.WriteLine(Twice(3)); int Twice(int x) => x * 2;` |
| 2 | **Static field initializer** in any class (implicit static constructor) | `NullReferenceException` in `ModuleCodeGen.EmitMethod` (ModuleCodeGen.cs:211): the implicit `.cctor` has no syntax, so it reaches the `default:` arm and dereferences `syntax` (also flagged by warning CS8602). Static initializers are already handled by `__csharpc_static_init`, so implicit cctors should be skipped | `Console.WriteLine(S.V); class S { public static int V = 7; }` |
| 3 | **Records** | Same crash as #2 (implicit members with no syntax) instead of the intended "record not supported" diagnostic | `Console.WriteLine(new R(1).X); record R(int X);` |

## P1 — wrong output (compiles, but differs from .NET)

| # | Construct | csharpc | .NET | Repro |
|---|---|---|---|---|
| 4 | Formatting ±infinity at **runtime** | `∞` / `-∞` | `Infinity` / `-Infinity` (invariant culture) | `int z = args.Length; Console.WriteLine(1.0 / z);` (`tests/samples/11_divide_by_zero` fails because of this). Compile-time-folded constants are already correct because they go through .NET's own formatter |

## P2 — unsupported (clean `CSLLVM000x` diagnostic)

| Construct | Message | Repro |
|---|---|---|
| `foreach` over an array | try/catch/finally (and constructs lowered to it: …) | `int[] a = [1, 2]; foreach (var x in a) Console.WriteLine(x);` |
| `try`/`finally`, `try`/`catch`, `using` | same | `try { Console.WriteLine("a"); } finally { Console.WriteLine("b"); }` |
| Primary constructors (classes) | primary constructors + "closure … capturing parameter" | `Console.WriteLine(new P(3).V); class P(int v) { public int V => v; }` |
| Tuple deconstruction / swap | tuples | `int a = 1, b = 2; (a, b) = (b, a);` |
| User-defined structs | struct 'Pt' | `var p = new Pt(); p.X = 3; struct Pt { public int X; }` |
| Generic methods / classes | generic method 'Id' / generic type parameter 'T' | `static T Id<T>(T x) => x;` |
| Interfaces | interface 'I' | `interface I { int M(); } class C : I { public int M() => 1; }` |
| Lambdas / delegates / closures | lambdas and delegates | `Func<int, int> f = x => x * 2;` |
| Capturing local functions | (crashes today, see #1) | `int k = 2; int Mul(int x) => x * k;` |
| Enum formatting | formatting enum values | `enum Color { Red } … Console.WriteLine(Color.Red);` |
| Boxing / `is` on object | boxing a 'int' to 'object' | `object o = 5; Console.WriteLine(o is int);` |
| Nullable value types | nullable value type 'int?' | `int? n = null;` |
| Multi-dimensional arrays | multi-dimensional arrays | `int[,] m = new int[2,2];` |
| `checked` arithmetic | checked arithmetic | `checked(x + 1)` |
| Interpolation format/alignment | interpolation alignment/format specifiers | `$"{d:F2}"` |
| `Console.WriteLine(format, args)` | `'System.Console.WriteLine(string, object)' has no implementation` | `Console.WriteLine("{0} items", 3);` |
| `string.Format`, `int.Parse`, `char.IsDigit`, `string.ToUpper/Substring/Split` | CSLLVM0002 no implementation | — |
| BCL classes (`List<T>`, `StringBuilder`, `StringWriter`) | creating instances of '…' | `new List<int>()` |

## Works today (verified against .NET)

`ref`/`out` parameters, default parameter values, `params` arrays, user-defined operators,
object initializers, `switch` on strings, enum comparison/casts, `static int Main(string[] args)`,
`args.Length`, `long` interpolation, jagged arrays — plus everything in `bench/programs`.
