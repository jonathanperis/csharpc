using System.Numerics;
Console.WriteLine(Sum(new[] { 1, 2, 3 }) + " " + Sum(new[] { 1.5, 2.5 }) + " " + Sum(new[] { 1m, 2.25m }));
Console.WriteLine(Mean(new[] { 2.0, 4.0, 9.0 }));
static T Sum<T>(T[] xs) where T : INumber<T> { T s = T.Zero; foreach (var x in xs) s += x; return s; }
static double Mean<T>(T[] xs) where T : INumber<T> => double.CreateChecked(Sum(xs)) / xs.Length;
