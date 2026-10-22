Console.WriteLine(D<int>() + "|" + (D<string>() ?? "null") + "|" + D<int?>() + "|" + D<bool>());
static T D<T>() => default!;
static bool IsNull<T>(T v) => v == null;
Console.WriteLine($"{IsNull(0)} {IsNull<string?>(null)} {IsNull<int?>(null)}");
