// NOTE: csharpc runs static initializers eagerly before Main (.NET: lazily, beforefieldinit).
// This program only prints values that are identical under both models.
Console.WriteLine(A.X + " " + B.Y);
class A { public static int X = 10; }
class B { public static int Y = A.X * 2; }
