// DefaultInterpolatedStringHandler (string.Create with a provider, and every `$"..."` compiled to IL) formats
// BCL structs csharpc lowers itself — BigInteger with its format strings, Index, Range, Int128 — as their
// own ToString(format, provider) would; their boxes aren't IFormattable, so the format used to be dropped.
using System;
using System.Globalization;
using System.Numerics;

var big = BigInteger.Parse("-98765432109876543210");
var small = new BigInteger(255);
Index fromEnd = ^3;
Range middle = 1..^2;
Int128 wide = Int128.MaxValue;
UInt128 uwide = UInt128.MaxValue;
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{big:D25} {big:N0} {small:X} {small,8:X4}|{big,-30:N2}|"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{fromEnd} {middle} [{middle,10}] {Index.Start} {..}"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{wide} {uwide} [{wide,-40}]"));
