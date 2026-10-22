// Nullable<T>: { hasValue, value } with lifted operators, ??, ?., patterns and boxing.
#pragma warning disable CS8629 // (int)b on purpose: it throws
int? a = 5;
int? b = null;
Console.WriteLine(a.HasValue);
Console.WriteLine(b.HasValue);
Console.WriteLine(a.Value);
Console.WriteLine(b.GetValueOrDefault());
Console.WriteLine(b.GetValueOrDefault(42));
Console.WriteLine(a ?? -1);
Console.WriteLine(b ?? -1);
Console.WriteLine($"[{a}] [{b}]");
Console.WriteLine(a);
Console.WriteLine(b);

int? sum = a + b;
Console.WriteLine(sum.HasValue);
Console.WriteLine(a + 10);
Console.WriteLine(a * a);
Console.WriteLine(-a);
Console.WriteLine(a == 5);
Console.WriteLine(b == null);
Console.WriteLine(a == b);
Console.WriteLine(b < 3);
Console.WriteLine(a > 3);
int? n1 = null, n2 = null;
Console.WriteLine(n1 == n2);
Console.WriteLine((a / b).HasValue);   // a null divisor gives null, not DivideByZeroException

a += 1;
a++;
Console.WriteLine(a);
b++;
Console.WriteLine(b.HasValue);

string? text = args.Length > 100 ? "never" : null;
Console.WriteLine(text?.Length ?? -1);
text = "hello";
Console.WriteLine(text?.Length ?? -1);

double? d = 2.5;
long? widened = a;
Console.WriteLine(d * 2);
Console.WriteLine(widened);

if (a is int value) Console.WriteLine($"a holds {value}");
if (b is null) Console.WriteLine("b is null");
if (b is not int) Console.WriteLine("b is not an int");
Console.WriteLine(a is > 5 and < 10);

object? boxedA = a;
object? boxedB = b;
Console.WriteLine(boxedA);
Console.WriteLine(boxedB == null);
int? unboxed = (int?)boxedA;
Console.WriteLine(unboxed);
int? unboxedNull = (int?)boxedB;
Console.WriteLine(unboxedNull.HasValue);

Console.WriteLine(Find(new[] { 3, 1, 4, 1, 5 }, 4));
Console.WriteLine(Find(new[] { 3, 1, 4, 1, 5 }, 9) ?? -1);

bool? flag = null;
Console.WriteLine(flag ?? true);
Point? pt = new Point(1, 2);
Console.WriteLine(pt.Value.X + pt.Value.Y);
Console.WriteLine(pt?.Y);
Point? none = null;
Console.WriteLine(none?.Y ?? -100);

int forced = (int)a;
Console.WriteLine(forced);
int crash = (int)b;     // InvalidOperationException
Console.WriteLine(crash);

static int? Find(int[] items, int wanted)
{
    for (int i = 0; i < items.Length; i++)
        if (items[i] == wanted) return i;
    return null;
}

struct Point(int x, int y)
{
    public int X = x;
    public int Y = y;
}
