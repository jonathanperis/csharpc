// decimal.GetBits (Roslyn serializes decimal constants with it) and Array.MaxLength.
foreach (var d in new[] { 0m, 1m, -1m, 1.5m, -123.4567m, decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m, 79228162514264337593543950335m / 3 })
{
    var bits = decimal.GetBits(d);
    Console.WriteLine($"{d}: {string.Join(" ", bits.Select(b => b.ToString("X8")))} -> {new decimal(bits)}");
}
Span<int> buffer = stackalloc int[5];
Console.WriteLine($"{decimal.GetBits(2.5m, buffer)} {buffer[0]} {buffer[3]:X8} {buffer[4]}");
Console.WriteLine($"{decimal.TryGetBits(-7m, buffer, out var written)} {written} {buffer[0]} {buffer[3]:X8}");
Console.WriteLine($"{decimal.TryGetBits(1m, buffer[..3], out written)} {written}");
try { decimal.GetBits(1m, buffer[..2]); }
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { new decimal(new[] { 1, 0, 0, 29 << 16 }); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { new decimal(new[] { 1, 0 }); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
Console.WriteLine(new decimal((ReadOnlySpan<int>)[5, 0, 0, 1 << 16]));
Console.WriteLine(Array.MaxLength);
