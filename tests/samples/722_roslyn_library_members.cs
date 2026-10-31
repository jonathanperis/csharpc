// Library members a program hosting Roslyn reaches (M6): Parallel.For with ParallelOptions and its
// ParallelLoopResult, CollectionsMarshal's dictionary value refs, MemoryStream.TryGetBuffer,
// BinaryReader.Read, span ReverseEndianness, Math.BigMul, BigInteger.ToByteArray/TryParse with styles,
// StringBuilder.AppendFormat with a provider and a span, Range/Index keys, TypeInfo.GenericTypeParameters,
// an implicit constructor calling a base constructor with optional parameters.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Parallel.For with options: every iteration once, a canceled token, MaxDegreeOfParallelism checks.
var hits = new int[100];
var result = Parallel.For(0, 100, new ParallelOptions { MaxDegreeOfParallelism = 3 }, i => Interlocked.Increment(ref hits[i]));
Console.WriteLine($"for: {result.IsCompleted} {result.LowestBreakIteration?.ToString() ?? "null"} {Array.TrueForAll(hits, h => h == 1)}");
long sum = 0;
Parallel.For(10L, 20L, new ParallelOptions(), i => Interlocked.Add(ref sum, i));
Console.WriteLine($"for long: {sum}");
Console.WriteLine($"empty: {Parallel.For(5, 5, new ParallelOptions(), _ => throw new Exception()).IsCompleted}");
var plain = Parallel.For(0, 4, _ => { });
Console.WriteLine($"no options: {plain.IsCompleted}");
using (var cts = new CancellationTokenSource())
{
    cts.Cancel();
    try { Parallel.For(0, 10, new ParallelOptions { CancellationToken = cts.Token }, _ => { }); }
    catch (OperationCanceledException e) { Console.WriteLine($"canceled: {e.GetType().Name} {e.Message} {e.CancellationToken == cts.Token}"); }
}
try { Parallel.For(0, 10, new ParallelOptions { MaxDegreeOfParallelism = 1 }, i => { if (i == 3) throw new InvalidOperationException("three"); }); }
catch (AggregateException e) { Console.WriteLine($"aggregate: {e.InnerExceptions.Count} {e.InnerException!.Message}"); }
try { new ParallelOptions { MaxDegreeOfParallelism = 0 }; }
catch (ArgumentOutOfRangeException e) { Console.WriteLine($"degree: {e.ParamName}"); }
try { Parallel.For(0, 1, null!, _ => { }); }
catch (ArgumentNullException e) { Console.WriteLine($"null options: {e.ParamName}"); }
Console.WriteLine($"defaults: {new ParallelOptions().MaxDegreeOfParallelism} {new ParallelOptions().TaskScheduler == null} {new ParallelOptions().CancellationToken.CanBeCanceled}");

// CollectionsMarshal over Dictionary.
var counts = new Dictionary<string, int>();
foreach (var word in "a b a c b a".Split(' ')) CollectionsMarshal.GetValueRefOrAddDefault(counts, word, out _)++;
ref int present = ref CollectionsMarshal.GetValueRefOrNullRef(counts, "c");
present += 10;
CollectionsMarshal.GetValueRefOrAddDefault(counts, "d", out bool existed);
Console.WriteLine($"counts: {string.Join(",", counts)} existed {existed}");

// MemoryStream.TryGetBuffer and BinaryReader.Read.
var grown = new MemoryStream();
grown.Write(new byte[] { 1, 2, 3, 4 });
Console.WriteLine($"grown: {grown.TryGetBuffer(out var segment)} {segment.Offset} {segment.Count}");
Console.WriteLine($"wrapped: {new MemoryStream(new byte[8]).TryGetBuffer(out var none)} {none.Array == null}");
Console.WriteLine($"visible: {new MemoryStream(new byte[8], 2, 4, false, true).TryGetBuffer(out var part)} {part.Offset} {part.Count}");
var reader = new BinaryReader(new MemoryStream(new byte[] { 9, 8, 7, 6, 5 }));
var into = new byte[6];
Console.WriteLine($"read: {reader.Read(into, 1, 3)} {string.Join(" ", into)} {reader.Read(into.AsSpan())} {into[0]}");
try { reader.Read(into, 4, 3); } catch (ArgumentException e) { Console.WriteLine($"read bounds: {e.GetType().Name}"); }
try { reader.Read(into, -1, 3); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"read negative: {e.Message}"); }

// BinaryPrimitives over spans, Math.BigMul.
short[] shorts = { 0x0102, -2, 0x7F00 };
var reversed = new short[3];
BinaryPrimitives.ReverseEndianness(shorts, reversed);
BinaryPrimitives.ReverseEndianness(shorts, shorts);
Console.WriteLine($"reverse: {string.Join(" ", reversed)} {string.Join(" ", shorts)}");
uint[] words = { 0x11223344 };
BinaryPrimitives.ReverseEndianness(words, words);
Console.WriteLine($"reverse uint: 0x{words[0]:X8}");
try { BinaryPrimitives.ReverseEndianness(new long[2], new long[1]); } catch (ArgumentException e) { Console.WriteLine($"short destination: {e.Message}"); }
ulong high = Math.BigMul(0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFE, out ulong low);
long shigh = Math.BigMul(-5L, long.MaxValue, out long slow);
Console.WriteLine($"bigmul: {high:X16} {low:X16} {shigh} {slow}");

// BigInteger bytes and styled parsing.
foreach (var n in new BigInteger[] { 0, 1, -1, 127, 128, 255, 256, -128, -129, -256, BigInteger.Pow(2, 64), -BigInteger.Pow(2, 32), BigInteger.Parse("-123456789012345678901234567890") })
    Console.WriteLine($"bytes {n}: {Convert.ToHexString(n.ToByteArray())}");
foreach (var (text, style) in new[] { ("123", NumberStyles.None), (" 45 ", NumberStyles.None), (" -45 ", NumberStyles.Integer), ("+7", NumberStyles.AllowLeadingSign), ("-7", NumberStyles.None), ("", NumberStyles.Integer), ("99999999999999999999", NumberStyles.None) })
    Console.WriteLine($"parse '{text}' {style}: {BigInteger.TryParse(text, style, CultureInfo.InvariantCulture, out var parsed)} {parsed}");

// StringBuilder.AppendFormat(provider, format, params ReadOnlySpan<object>).
var builder = new StringBuilder();
builder.AppendFormat(CultureInfo.InvariantCulture, "{0}-{1:F2}-{2}-{3}", 1, 2.5, "x", 'y');
Console.WriteLine(builder);
// GetChunks of a builder within its first chunk (one chunk in .NET too); the content of a longer one.
int chunks = 0;
foreach (var chunk in new StringBuilder("hi there").GetChunks()) { chunks++; Console.WriteLine($"chunk '{chunk}' {chunk.Length}"); }
var longer = new StringBuilder();
for (int i = 0; i < 50; i++) longer.Append(i).Append(',');
var joined = new StringBuilder();
foreach (var chunk in longer.GetChunks()) joined.Append(chunk.Span);
Console.WriteLine($"chunks {chunks}, content {joined.ToString() == longer.ToString()} {joined.Length}");

// Encoding providers come before the built-in table.
Encoding.RegisterProvider(new AliasProvider());
Encoding.RegisterProvider(new AliasProvider());
Console.WriteLine($"provider: {Encoding.GetEncoding("my-utf8").WebName} {Encoding.GetEncoding(1234).WebName} {Encoding.GetEncoding(65001).WebName} {Encoding.GetEncoding("my-utf8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).DecoderFallback.GetType().Name}");
try { Encoding.GetEncoding(1235); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
try { Encoding.RegisterProvider(null!); } catch (ArgumentNullException e) { Console.WriteLine($"null provider: {e.ParamName}"); }

// Range and Index as keys.
var ranges = new Dictionary<Range, string> { [1..3] = "a", [^2..] = "b" };
var indexes = new HashSet<Index> { 1, ^1, 1 };
Console.WriteLine($"ranges: {ranges[1..3]} {ranges[^2..]} {ranges.ContainsKey(0..3)} indexes {indexes.Count} {indexes.Contains(^1)}");

// TypeInfo.GenericTypeParameters.
Console.WriteLine($"generic parameters: {string.Join(",", (IEnumerable<Type>)typeof(Dictionary<,>).GetTypeInfo().GenericTypeParameters)} [{string.Join(",", (IEnumerable<Type>)typeof(List<int>).GetTypeInfo().GenericTypeParameters)}]");

// An implicit constructor calls the base constructor with its default arguments.
Console.WriteLine(new Derived().Describe());

class Base
{
    private readonly string _text;
    protected Base(int count = 3, string? name = null, bool flag = true, Color color = Color.Blue, double scale = 1.5) =>
        _text = $"{count} {name ?? "null"} {flag} {color} {scale}";
    public string Describe() => _text;
}

class Derived : Base { }

enum Color { Red, Blue }

sealed class AliasProvider : EncodingProvider
{
    public override Encoding? GetEncoding(string name) => name == "my-utf8" ? Encoding.UTF8 : null;
    public override Encoding? GetEncoding(int codepage) => codepage == 1234 ? Encoding.Unicode : null;
}
