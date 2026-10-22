// conformance2 library fixes, re-implemented in the C# corelib when merging onto the C#-corelib
// runtime: primitive hashes, boxed-primitive IComparable/IFormattable dispatch, string members with
// StringComparison, Split with a count, StringBuilder.AppendFormat, Console.Write(char[]),
// Math.IEEERemainder, CreateSaturating and the span Max/Min empty-sequence message. Also compound
// assignment to interface properties/indexers through an interface-typed receiver (it used a class
// vtable slot instead of interface dispatch: `N++` in a default interface method crashed).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

Console.WriteLine($"{((short)-5).GetHashCode()} {((sbyte)-5).GetHashCode()} {'a'.GetHashCode()} {(-5L).GetHashCode()}");
Console.WriteLine($"{double.NaN.GetHashCode()} {BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000001)).GetHashCode()} {(-0.0).GetHashCode()} {(1.5).GetHashCode()}");
Console.WriteLine($"{(-0.0f).GetHashCode()} {float.NaN.GetHashCode()} {(1.5f).GetHashCode()}");
Console.WriteLine($"{5.Equals((object)5)} {5.Equals((object)5L)} {double.NaN.Equals((object)double.NaN)}");

IComparable c = 5;
Console.WriteLine($"{c.CompareTo(3)} {c.CompareTo(null)} {c.CompareTo(5)}");
try { c.CompareTo("x"); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
IComparable d = 2.5;
try { d.CompareTo(1); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
IFormattable f = 3.5;
Console.WriteLine(f.ToString("F3", null));
IFormattable g = 255;
Console.WriteLine(g.ToString("X4", null));

Console.WriteLine($"{"Hello".IndexOf("LL", StringComparison.OrdinalIgnoreCase)} {"Hello".IndexOf("l", 3, StringComparison.Ordinal)} {"ÄÖ".IndexOf("äö", StringComparison.OrdinalIgnoreCase)}");
Console.WriteLine($"{"Hello".LastIndexOf("l")} {"Hello".LastIndexOf("L", StringComparison.OrdinalIgnoreCase)} {"Hello".LastIndexOf("")}");
Console.WriteLine($"{"Hello".Contains("ELL", StringComparison.OrdinalIgnoreCase)} {"Hello".StartsWith("he", StringComparison.OrdinalIgnoreCase)} {"ΑΒ".EndsWith("β", StringComparison.OrdinalIgnoreCase)}");
Console.WriteLine($"{"Hello".StartsWith('H')} {"Hello".EndsWith('x')} {"".EndsWith('x')} {"abc".Equals((object)"abc")} {"abc".Equals((object)5)}");
try { "Hello".IndexOf("x", (StringComparison)9); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { "Hello".IndexOf("x", 9, StringComparison.Ordinal); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

foreach (var (s, count, options) in new[] { ("a,,b,,c", 2, StringSplitOptions.RemoveEmptyEntries), (",,a,,b", 2, StringSplitOptions.RemoveEmptyEntries),
             (" a , b , c ", 2, StringSplitOptions.TrimEntries), ("a,b", 1, StringSplitOptions.None), ("", 3, StringSplitOptions.None),
             (",,", 2, StringSplitOptions.RemoveEmptyEntries), ("a,b,c", 5, StringSplitOptions.None), ("a,b,c,d", 0, StringSplitOptions.None) })
{
    var parts = s.Split(',', count, options);
    Console.WriteLine($"[{string.Join("|", parts)}] {parts.Length}");
}
try { "a".Split(',', -1, StringSplitOptions.None); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

var sb = new StringBuilder();
sb.AppendFormat("{0}-{1}", 1, 2).AppendFormat("{0}", "x").AppendFormat("{0}{1}{2}", 1, 2, 3).AppendFormat("{0}", new object[] { 9 });
sb.AppendFormat("<{0,4}|{1:F1}|{2}|{3}>", 7, 2.25, 'z', true);
int n = 5;
sb.Append($"[{n}]").AppendLine($"({n:D3})");
Console.Write(sb.ToString());

Console.Write(new[] { 'a', 'b' });
Console.WriteLine(new[] { 'c' });

Console.WriteLine($"{Math.IEEERemainder(10, 3)} {Math.IEEERemainder(11, 3)} {Math.IEEERemainder(-4, 2)} {Math.IEEERemainder(5, 2)} {Math.IEEERemainder(7, 2)}");

Console.WriteLine($"{byte.CreateSaturating(300)} {byte.CreateSaturating(-5)} {sbyte.CreateSaturating(-500L)} {int.CreateSaturating(ulong.MaxValue)}");
Console.WriteLine($"{uint.CreateSaturating(-1)} {ulong.CreateSaturating(long.MinValue)} {long.CreateSaturating(ulong.MaxValue)} {short.CreateSaturating(70000u)}");
Console.WriteLine($"{int.CreateSaturating(1e20)} {int.CreateSaturating(double.NaN)} {Saturate<byte>(1000)} {Saturate<short>(-100000)} {Saturate<ulong>(-1)}");

int[] values = { 3, 9, 1 };
Console.WriteLine($"{values.Max()} {values.Min()}");
try { int[] empty = { }; empty.Max(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

ICount ic = new Counter();
ic.Bump(); ic.Bump();
ic.N += 5;
ic.N++;
Console.WriteLine(ic.N);
IList<int> list = new List<int> { 1, 2, 3 };
int k = 0;
list[k++] += 10;
list[k] *= 3;
Console.WriteLine($"{string.Join(",", list)} {k}");
IBox box = new Box();
box[1] += 4;
box[1]++;
Console.WriteLine(box[1]);

static T Saturate<T>(int x) where T : INumberBase<T> => T.CreateSaturating(x);

interface ICount { int N { get; set; } void Bump() => N++; }
class Counter : ICount { public int N { get; set; } }
interface IBox { int this[int i] { get; set; } }
class Box : IBox
{
    private readonly int[] _items = new int[3];
    public int this[int i] { get => _items[i]; set { Console.WriteLine($"set {i}"); _items[i] = value; } }
}
