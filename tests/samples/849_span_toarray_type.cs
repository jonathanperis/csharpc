// Span<T>.ToArray() makes a T[]: its GetType(), casts from object and array covariance are a T[]'s
// (`new ReadOnlySequence<byte>("ok"u8.ToArray())` stores it as object and casts it back to byte[]).
object bytes = "ok"u8.ToArray();
Console.WriteLine(bytes.GetType() + " " + ((byte[])bytes).Length + " " + (bytes is byte[]) + " " + (bytes is int[]));
ReadOnlySpan<int> ints = stackalloc int[] { 1, 2, 3 };
object copy = ints.ToArray();
Console.WriteLine(copy.GetType() + " " + string.Join(",", (int[])copy));
Span<string> words = new[] { "a", "b" };
object[] covariant = words.ToArray();
Console.WriteLine(covariant.GetType() + " " + covariant.Length);
try { covariant[0] = 42; } catch (ArrayTypeMismatchException e) { Console.WriteLine(e.GetType().Name); }
Console.WriteLine(ReadOnlySpan<char>.Empty.ToArray().GetType() + " " + new Memory<double>(new[] { 1.5 }).ToArray().GetType());
