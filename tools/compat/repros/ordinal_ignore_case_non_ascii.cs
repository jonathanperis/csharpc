// StringComparison.OrdinalIgnoreCase must fold non-ASCII letters (simple Unicode upper-casing, as
// ToUpperInvariant does). csharpc only folds ASCII, so Cyrillic/Greek/Latin-1 strings that differ
// only in case compare unequal: silently wrong output.
// .NET: 0 True True 0 True      csharpc: 32 False True -32 False
// Found in dotnet/docs docs/standard/base-types/snippets/best-practices-strings/csharp/comparison2.
using System;

string a = "Владимир", b = "ВЛАДИМИР";
Console.WriteLine(string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Equals(a, b, StringComparison.OrdinalIgnoreCase));
Console.WriteLine("straße".Equals("STRAßE", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("ÉCOLE", "école", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Equals("Ωμέγα", "ΩΜΈΓΑ", StringComparison.OrdinalIgnoreCase));
