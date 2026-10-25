// Library members System.Xml needs, as programs use them: non-generic Array access and binary search,
// Array.CreateInstanceFromArrayType, Hashtable.Synchronized, Comparer, Convert.ToBase64CharArray,
// TryFormat through ISpanFormattable, MemoryExtensions over sets of values, bool.TrueString,
// TimeZoneInfo.Utc, file and relative URIs, and an exception setting HResult.
using System;
using System.Collections;
using System.Globalization;

Array numbers = new[] { 1, 3, 5, 7, 9 };
Console.WriteLine($"GetValue {numbers.GetValue(2)}, BinarySearch {Array.BinarySearch(numbers, 7)} {Array.BinarySearch(numbers, 4)} {Array.BinarySearch(numbers, 1, 3, 9, null)}");
numbers.SetValue(4, 1);
Console.WriteLine($"after SetValue: {string.Join(",", (int[])numbers)}");
try { numbers.GetValue(9); }
catch (IndexOutOfRangeException e) { Console.WriteLine(e.Message); }
Array words = new[] { "apple", "Banana", "cherry" };
Console.WriteLine($"strings: {Array.BinarySearch(words, "Banana", StringComparer.Ordinal)} {Array.BinarySearch(words, "cherry", Comparer.DefaultInvariant)}");
try { Array.BinarySearch(new object[] { new object(), new object() }, new object(), null); }
catch (InvalidOperationException e) { Console.WriteLine($"{e.Message} / {e.InnerException?.Message}"); }

Array created = Array.CreateInstanceFromArrayType(typeof(string[]), 3);
created.SetValue("x", 0);
Console.WriteLine($"created {created.GetType()} length {created.Length} [{string.Join(",", (string?[])created)}]");
Array doubles = Array.CreateInstanceFromArrayType(typeof(double[]), 2);
Console.WriteLine($"doubles {doubles.GetType()} {doubles.GetValue(1)}");
try { Array.CreateInstanceFromArrayType(typeof(string), 1); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }

var table = new Hashtable { ["a"] = 1 };
var synced = Hashtable.Synchronized(table);
synced["b"] = 2;
synced.Add("c", 3);
Console.WriteLine($"synchronized {synced.IsSynchronized} {table.IsSynchronized} count {table.Count} contains {synced.ContainsKey("b")} {synced.ContainsValue(3)}");
Console.WriteLine($"comparer: {Comparer.Default.Compare(1, 2)} {Comparer.Default.Compare("b", "a")} {Comparer.Default.Compare(null, 1)} {Comparer.DefaultInvariant.Compare("a", "A")}");
try { Comparer.Default.Compare(new object(), new object()); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }

var chars = new char[12];
int written = Convert.ToBase64CharArray([1, 2, 3, 4, 5], 0, 5, chars, 2);
Console.WriteLine($"base64 {written}: [{new string(chars, 2, written)}]");
try { Convert.ToBase64CharArray([1, 2, 3], 0, 3, new char[3], 0); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }

Console.WriteLine($"TryFormat: {Format(42)} {Format(-7L)} {Format(255u)} {Format(1.5)} {Format((short)-3)} {Format((byte)200)}");
Span<char> small = stackalloc char[2];
Console.WriteLine($"too small: {12345.TryFormat(small, out int n)} {n}; hex: {(255.TryFormat(stackalloc char[8], out int h, "X4") ? h : -1)}");

ReadOnlySpan<char> padded = "--==value==--";
Console.WriteLine($"Trim: [{padded.Trim("-=")}] [{padded.TrimStart("-")}] [{"  spaced  ".AsSpan().Trim("")}] [{padded.Trim(ReadOnlySpan<char>.Empty).ToString()}]");
Console.WriteLine($"ContainsAny: {"hello".AsSpan().ContainsAny("xyz")} {"hello".AsSpan().ContainsAny("lo")} {"aaa".AsSpan().ContainsAnyExcept("a")} {"abc".AsSpan().ContainsAnyExcept("cab")} {"a.b".AsSpan().ContainsAny('x', 'y', '.')}");
Span<char> target = stackalloc char[5];
"a.b.c".AsSpan().Replace(target, '.', '/');
Console.WriteLine($"Replace: {target.ToString()}");
try { "abcdef".AsSpan().Replace(new char[2], 'a', 'b'); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }

Console.WriteLine($"{bool.TrueString} {bool.FalseString}");
var utc = TimeZoneInfo.Utc;
Console.WriteLine($"utc offset {utc.GetUtcOffset(DateTime.UtcNow)} base {utc.BaseUtcOffset} dst {utc.IsDaylightSavingTime(new DateTime(2024, 7, 1))} same {ReferenceEquals(TimeZoneInfo.Utc, utc)} {ReferenceEquals(TimeZoneInfo.Local, TimeZoneInfo.Local)}");

var file = new Uri("/tmp/data/report.xml");
Console.WriteLine($"file: {file} {file.AbsoluteUri} {file.LocalPath} {file.IsFile} {file.Scheme} [{file.Host}] {file.Port} {file.IsAbsoluteUri} {file.AbsolutePath} {file.Segments.Length}");
var explicitFile = new Uri("file:///var/log/app.log");
Console.WriteLine($"explicit: {explicitFile.LocalPath} {explicitFile == new Uri("/var/log/app.log")}");
var relative = new Uri("docs/readme.txt", UriKind.RelativeOrAbsolute);
Console.WriteLine($"relative: {relative} {relative.IsAbsoluteUri} {relative.OriginalString} {Uri.TryCreate("x/y", UriKind.Relative, out var r2) && !r2!.IsAbsoluteUri} {Uri.TryCreate("http://h/", UriKind.Relative, out _)}");
try { _ = relative.Scheme; }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { new Uri("readme.txt"); }
catch (UriFormatException e) { Console.WriteLine(e.Message); }

try { throw new CodedException("failed"); }
catch (CodedException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

static string Format<T>(T value) where T : ISpanFormattable
{
    Span<char> buffer = stackalloc char[32];
    return value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture) ? buffer[..written].ToString() : "?";
}

sealed class CodedException : Exception
{
    public CodedException(string message) : base(message) { HResult = unchecked((int)0x80131600); }
}
