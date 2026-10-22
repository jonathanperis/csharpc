bool?[] vs = { true, false, null };
foreach (var x in vs) foreach (var y in vs) Console.WriteLine($"{F(x)} & {F(y)} = {F(x & y)}   {F(x)} | {F(y)} = {F(x | y)}   ^ = {F(x ^ y)}");
static string F(bool? b) => b?.ToString() ?? "null";
