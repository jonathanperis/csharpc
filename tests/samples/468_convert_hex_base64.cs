// Convert's hex and Base64 conversions, round trips and argument errors.
byte[][] inputs = [[], [0], [0xFF, 0x10], [1, 2, 3], [77, 97, 110, 32, 105], [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x7F]];
foreach (var bytes in inputs)
{
    var hex = Convert.ToHexString(bytes);
    var b64 = Convert.ToBase64String(bytes);
    Console.WriteLine($"[{hex}] [{Convert.ToHexStringLower(bytes)}] [{b64}] {Convert.FromHexString(hex).SequenceEqual(bytes)} {Convert.FromBase64String(b64).SequenceEqual(bytes)}");
}
Console.WriteLine(Convert.ToHexString([1, 2, 3, 4], 1, 2));
Console.WriteLine(Convert.ToBase64String([1, 2, 3, 4], 1, 3));
Console.WriteLine(Convert.ToBase64String((ReadOnlySpan<byte>)[9, 8]));
Console.WriteLine(Convert.ToBase64String(Enumerable.Range(0, 120).Select(i => (byte)i).ToArray(), Base64FormattingOptions.InsertLineBreaks).Replace("\r\n", "|"));
Console.WriteLine(string.Join(",", Convert.FromHexString("0aFf")));
Console.WriteLine(string.Join(",", Convert.FromBase64String(" TW Fu\r\nIGk= ")));
foreach (var bad in new[] { "abc", "zz" })
{
    try { Convert.FromHexString(bad); } catch (FormatException e) { Console.WriteLine(e.Message); }
}
foreach (var bad in new[] { "abc", "ab=c", "a===", "!!!!" })
{
    try { Convert.FromBase64String(bad); } catch (FormatException e) { Console.WriteLine(e.Message); }
}
try { Convert.ToHexString(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { Convert.ToBase64String([1], 1, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { Convert.ToHexString([1], -1, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
