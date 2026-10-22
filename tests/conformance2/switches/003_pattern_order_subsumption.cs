static string M(object o) => o switch
{
    int i when i > 100 => "big int",
    int i => $"int {i}",
    string { Length: 0 } => "empty string",
    string s => $"string {s}",
    null => "null",
    _ => o.GetType().Name,
};
foreach (var o in new object?[] { 5, 500, "", "hi", null, 2.5, 'c' }) Console.WriteLine(M(o!));
