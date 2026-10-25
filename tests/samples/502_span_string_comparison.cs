// MemoryExtensions' StringComparison overloads over spans and string.GetHashCode(span, comparison),
// as Microsoft.Extensions.Primitives' StringSegment and ASP.NET Core's header parsing call them.
ReadOnlySpan<char> name = "Content-Type";
Console.WriteLine($"{name.EndsWith("TYPE", StringComparison.OrdinalIgnoreCase)} {name.EndsWith("TYPE", StringComparison.Ordinal)} {name.EndsWith("-Type", StringComparison.Ordinal)} {name.EndsWith("", StringComparison.Ordinal)} {"a".AsSpan().EndsWith("ba", StringComparison.OrdinalIgnoreCase)}");
Console.WriteLine($"{name.Contains("t-t", StringComparison.OrdinalIgnoreCase)} {name.Contains("t-t", StringComparison.Ordinal)} {name.LastIndexOf("t", StringComparison.Ordinal)} {name.LastIndexOf("T", StringComparison.OrdinalIgnoreCase)}");
Console.WriteLine($"{Math.Sign(name.CompareTo("content-type", StringComparison.Ordinal))} {name.CompareTo("content-type", StringComparison.OrdinalIgnoreCase)} {Math.Sign("b".AsSpan().CompareTo("a", StringComparison.Ordinal))} {Math.Sign("a".AsSpan().CompareTo("ab", StringComparison.OrdinalIgnoreCase))}");
Console.WriteLine($"{"abc".AsSpan().CompareTo("abd", StringComparison.Ordinal)} {"a".AsSpan().CompareTo("z", StringComparison.Ordinal)} {"abc".AsSpan().CompareTo("ab", StringComparison.Ordinal)} {"".AsSpan().CompareTo("", StringComparison.Ordinal)}");
Console.WriteLine(string.GetHashCode("Accept", StringComparison.OrdinalIgnoreCase) == string.GetHashCode("ACCEPT", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.GetHashCode("Accept".AsSpan(), StringComparison.OrdinalIgnoreCase) == "accept".GetHashCode(StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.GetHashCode("Accept".AsSpan(), StringComparison.Ordinal) == "Accept".GetHashCode());
Console.WriteLine(char.Parse("x") + " " + (int)char.Parse("é"));
try { char.Parse("xy"); } catch (FormatException e) { Console.WriteLine(e.Message); }
try { char.Parse(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
Console.WriteLine($"{char.TryParse("q", out var q)} {q} {char.TryParse("", out var empty)} {(int)empty}");
