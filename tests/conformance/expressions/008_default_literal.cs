int i = default;
string? s = default;
Console.WriteLine(i + " " + (s == null));
Console.WriteLine(Get<long>());
static T Get<T>() => default!;
