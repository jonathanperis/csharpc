Console.WriteLine(Sum(new[] { 1, 2, 3 }));
Console.WriteLine(Sum(new[] { 1.5, 2.5 }));
static T Sum<T>(T[] xs) where T : System.Numerics.INumber<T>
{
    T total = T.Zero;
    foreach (var x in xs) total += x;
    return total;
}
