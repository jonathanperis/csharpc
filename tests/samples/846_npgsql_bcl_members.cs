// BCL members Npgsql reaches: string.Normalize (SCRAM-SHA-256 passwords), HMACSHA256.HashData, Version(string),
// Math.Abs of the small integers, decimal.Scale and CreateChecked, CompareInfo.GetStringComparer,
// Array.GetValue/SetValue with an index array, EncodingExtensions.GetString of a multi-segment sequence,
// Utf8.FromUtf16 / ToUtf16, Dns.GetHostAddressesAsync of an address literal and the EventCounters.
using System.Buffers;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Unicode;

static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

foreach (var form in new[] { NormalizationForm.FormC, NormalizationForm.FormD, NormalizationForm.FormKC, NormalizationForm.FormKD })
{
    var s = "ﬁ café Å ①";
    string n = s.Normalize(form);
    Console.WriteLine($"{form}: {n.Length} {Hex(Encoding.UTF8.GetBytes(n))} {s.IsNormalized(form)} {n.IsNormalized(form)}");
}
Console.WriteLine(ReferenceEquals("ascii", "ascii".Normalize()) + " " + "plain".Normalize(NormalizationForm.FormKC));
try { "a\ud800b".Normalize(); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { "x".Normalize((NormalizationForm)3); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

Console.WriteLine(Hex(HMACSHA256.HashData(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("Client Key"))));
Console.WriteLine(Hex(HMACSHA256.HashData(Encoding.UTF8.GetBytes(new string('k', 100)), Encoding.UTF8.GetBytes("Server Key"))));
Console.WriteLine(Hex(HMACSHA1.HashData("k"u8, "data"u8)) + " " + HMACSHA512.HashData("k"u8, "data"u8).Length);
try { HMACSHA256.HashData(null!, new byte[1]); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

Console.WriteLine(new Version("14.2") + " " + new Version("1.2.3.4").Revision);
try { new Version("1.x"); } catch (FormatException e) { Console.WriteLine(e.GetType().Name); }
try { new Version(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

short sh = -12345;
sbyte sb = -7;
nint ni = -42;
Console.WriteLine(Math.Abs(sh) + " " + Math.Abs(sb) + " " + Math.Abs(ni));
try { Math.Abs(short.MinValue); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { Math.Abs(sbyte.MinValue); } catch (OverflowException e) { Console.WriteLine(e.Message); }

Console.WriteLine(1.2345m.Scale + " " + 100m.Scale + " " + 0.000m.Scale + " " + decimal.CreateChecked(12.5m) + " " + Create<decimal, decimal>(-3.25m) + " " + Create<int, decimal>(42));
static TTo Create<TFrom, TTo>(TFrom value) where TFrom : INumberBase<TFrom> where TTo : INumberBase<TTo> => TTo.CreateChecked(value);

var comparer = CultureInfo.InvariantCulture.CompareInfo.GetStringComparer(CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth);
var fields = new Dictionary<string, int>(comparer) { ["RandomNumber"] = 1 };
Console.WriteLine(fields.ContainsKey("randomnumber") + " " + comparer.Equals("ＡＢＣ", "abc") + " " + ReferenceEquals(CompareInfo.GetCompareInfo("").GetStringComparer(CompareOptions.Ordinal), StringComparer.Ordinal));
try { CultureInfo.InvariantCulture.CompareInfo.GetStringComparer((CompareOptions)0x100); } catch (ArgumentException e) { Console.WriteLine(e.Message); }

Array numbers = new int[] { 10, 20, 30 };
numbers.SetValue(99, new[] { 1 });
Console.WriteLine(numbers.GetValue(new[] { 1 }) + " " + numbers.GetValue(new[] { 2 }));
try { numbers.GetValue(new[] { 1, 2 }); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { numbers.GetValue((int[])null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

var first = new Segment(new byte[] { 0x68, 0xC3 });
var last = first.Append(new byte[] { 0xA9, 0x21 });
var sequence = new ReadOnlySequence<byte>(first, 0, last, 2);
Console.WriteLine(sequence.IsSingleSegment + " " + Encoding.UTF8.GetString(sequence) + " " + Encoding.UTF8.GetString(new ReadOnlySequence<byte>("ok"u8.ToArray())));

Span<byte> utf8 = stackalloc byte[16];
Console.WriteLine(Utf8.FromUtf16("añ😀", utf8, out int charsRead, out int bytesWritten) + $" {charsRead} {bytesWritten} {Hex(utf8[..bytesWritten])}");
Console.WriteLine(Utf8.FromUtf16("ab\ud800", utf8, out charsRead, out bytesWritten, replaceInvalidSequences: false) + $" {charsRead} {bytesWritten}");
Console.WriteLine(Utf8.FromUtf16("ab\ud800", utf8, out charsRead, out bytesWritten, isFinalBlock: false) + $" {charsRead} {bytesWritten}");
Console.WriteLine(Utf8.FromUtf16("abcd", utf8[..2], out charsRead, out bytesWritten) + $" {charsRead} {bytesWritten}");
Span<char> utf16 = stackalloc char[8];
Console.WriteLine(Utf8.ToUtf16(new byte[] { 0x61, 0xFF, 0x62 }, utf16, out int bytesRead, out int charsWritten) + $" {bytesRead} {charsWritten} {(int)utf16[1]:X}");

var addresses = await Dns.GetHostAddressesAsync("127.0.0.1", CancellationToken.None);
Console.WriteLine(addresses.Length + " " + addresses[0]);
try { await Dns.GetHostAddressesAsync("localhost", new CancellationToken(true)); } catch (OperationCanceledException e) { Console.WriteLine(e.GetType().Name); }

using var source = new Counted();
var counter = new PollingCounter("busy-connections", source, () => 3) { DisplayName = "Busy", DisplayUnits = "connections" };
counter.AddMetadata("pool", "main");
Console.WriteLine($"{counter.Name} {counter.DisplayName} {counter.DisplayUnits} {counter.EventSource.Name}");
var rate = new IncrementingPollingCounter("bytes-read", source, () => 1) { DisplayRateTimeScale = TimeSpan.FromSeconds(1) };
Console.WriteLine(rate.Name + " " + rate.DisplayRateTimeScale);
counter.Dispose();
rate.Dispose();
try { new PollingCounter("x", source, null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

sealed class Segment : ReadOnlySequenceSegment<byte>
{
    public Segment(byte[] data) => Memory = data;
    public Segment Append(byte[] data)
    {
        var next = new Segment(data) { RunningIndex = RunningIndex + Memory.Length };
        Next = next;
        return next;
    }
}

[EventSource(Name = "Sample-Counters")]
sealed class Counted : EventSource { }
