// Struct fields of type double/float compare with double.Equals (NaN equals NaN, 0.0 equals -0.0),
// not bitwise, and hash consistently. Also the deterministic hash codes of primitives, boxed
// primitives and enums, which programs can print.
using System;
using System.Collections.Generic;

double negZero = -0.0, nan = double.NaN, otherNaN = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000001));
var z1 = new Point { X = 0.0, Y = 1 };
var z2 = new Point { X = negZero, Y = 1 };
Console.WriteLine($"{z1.Equals(z2)} {z1.GetHashCode() == z2.GetHashCode()}");
var n1 = new Point { X = nan, Y = 2 };
var n2 = new Point { X = otherNaN, Y = 2 };
Console.WriteLine($"{n1.Equals(n2)} {n1.GetHashCode() == n2.GetHashCode()} {n1.Equals(new Point { X = 1, Y = 2 })}");
var f1 = new PointF { X = 0.0f, Y = float.NaN };
var f2 = new PointF { X = -0.0f, Y = float.NaN };
Console.WriteLine($"{f1.Equals(f2)} {f1.GetHashCode() == f2.GetHashCode()}");

var points = new HashSet<Point> { z1, z2, n1, n2, new Point { X = 1.5, Y = 2 } };
Console.WriteLine(points.Count + " " + points.Contains(new Point { X = -0.0, Y = 1 }) + " " + points.Contains(new Point { X = nan, Y = 2 }));
var list = new List<Point> { new Point { X = 3, Y = 3 }, n1 };
Console.WriteLine(list.IndexOf(n2) + " " + list.Contains(new Point { X = 3, Y = -0 }));

// Generic structs: the same rules per instantiation.
Console.WriteLine($"{new Pair<double>(nan, 1).Equals(new Pair<double>(otherNaN, 1))} {new Pair<double>(0.0, 1).Equals(new Pair<double>(negZero, 1))} {new Pair<long>(7, 1).Equals(new Pair<long>(7, 1))} {new Pair<long>(7, 1).Equals(new Pair<long>(8, 1))}");
Console.WriteLine($"{new Pair<string>("a" + 1, 2).Equals(new Pair<string>("a1", 2))} {new Pair<Pair<double>>(new(negZero, 3), 4).Equals(new Pair<Pair<double>>(new(0.0, 3), 4))}");

// Boxed primitives: Equals and the exact .NET hash codes.
object bz = 0.0, bn = nan;
Console.WriteLine($"{bz.Equals(negZero)} {bn.Equals(otherNaN)} {((object)0.0f).Equals(-0.0f)} {bz.Equals(0.0f)} {((object)1).Equals(1L)}");
Console.WriteLine($"{bz.GetHashCode()} {((object)negZero).GetHashCode()} {bn.GetHashCode()} {((object)otherNaN).GetHashCode()} {((object)1.5).GetHashCode()} {((object)float.NaN).GetHashCode()} {((object)2.5f).GetHashCode()}");
Console.WriteLine($"{((object)(short)-1).GetHashCode()} {((object)(sbyte)-2).GetHashCode()} {((object)'a').GetHashCode()} {((object)(ushort)65535).GetHashCode()} {((object)-5L).GetHashCode()} {((object)true).GetHashCode()} {((object)0xFFFFFFFF00000001UL).GetHashCode()}");
Console.WriteLine($"{nan.GetHashCode()} {negZero.GetHashCode()} {double.PositiveInfinity.GetHashCode()} {(-2.5f).GetHashCode()} {'z'.GetHashCode()} {(-7).GetHashCode()} {(-7L).GetHashCode()} {((short)-3).GetHashCode()}");
Console.WriteLine($"{EqualityComparer<double>.Default.GetHashCode(negZero)} {EqualityComparer<double>.Default.GetHashCode(otherNaN)} {EqualityComparer<char>.Default.GetHashCode('A')} {EqualityComparer<sbyte>.Default.GetHashCode(-1)}");
Console.WriteLine($"{1.0.Equals(1.0)} {nan.Equals(otherNaN)} {0.0.Equals(negZero)} {5.Equals(5)} {5.Equals((object)5)} {5.Equals((object)5L)}");

// Enums hash like their underlying value.
Console.WriteLine($"{Color.Blue.GetHashCode()} {((object)Small.Minus).GetHashCode()} {Big.High.GetHashCode()} {((object)Color.Green).Equals(Color.Green)} {((object)Color.Green).Equals(1)}");

struct Point { public double X; public int Y; }
struct PointF { public float X; public float Y; }
readonly struct Pair<T>(T first, int second)
{
    public readonly T First = first;
    public readonly int Second = second;
}
enum Color { Red, Green, Blue }
enum Small : sbyte { Minus = -3 }
enum Big : long { High = 0x100000002 }
