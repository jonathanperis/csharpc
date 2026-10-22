// A `double.NaN` / `float.NaN` constant pattern matches NaN in C# (patterns compare with
// double.Equals semantics, not ==). csharpc lowers it to an ordered floating-point equality, which
// is never true for NaN: silently wrong output.
// .NET: NaN NaN True      csharpc: other other False
// Found in dotnet/docs docs/csharp/language-reference/statements/snippets/{jump,selection}-statements.
using System;

foreach (double m in new[] { double.NaN })
{
    switch (m)
    {
        case < 0.0: Console.WriteLine("negative"); break;
        case double.NaN: Console.WriteLine("NaN"); break;
        default: Console.WriteLine("other"); break;
    }
}
float f = float.NaN;
Console.WriteLine(f switch { float.NaN => "NaN", _ => "other" });
double d = double.NaN;
Console.WriteLine(d is double.NaN);
