// A boxed BigInteger answers its interfaces (IFormattable, ISpanFormattable, IComparable, IEquatable<BigInteger>,
// IComparable<BigInteger>, IUtf8SpanFormattable) and dispatches through them, with the provider applied: framework code that
// formats or compares a boxed T through those interfaces (Convert.ToString(object), string.Join, comparers) relies on it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
odd.NumberFormat.NegativeSign = "~";
object o = BigInteger.Parse("-123456789012345678901234567890", CultureInfo.InvariantCulture);
object one = BigInteger.One;
Console.WriteLine($"{o is IFormattable} {o is ISpanFormattable} {o is IComparable} {o is IEquatable<BigInteger>} {o is IComparable<BigInteger>} {o is IUtf8SpanFormattable} {o is IConvertible}");
Console.WriteLine(((IFormattable)o).ToString(null, CultureInfo.InvariantCulture) + " " + ((IFormattable)o).ToString("N0", odd) + " " + ((IFormattable)one).ToString("X4", null));
Span<char> buffer = stackalloc char[64];
Console.WriteLine(((ISpanFormattable)o).TryFormat(buffer, out int written, "D", odd) + " " + buffer.Slice(0, written).ToString());
Console.WriteLine(((IComparable)o).CompareTo(one) + " " + ((IComparable)one).CompareTo(null) + " " + ((IComparable<BigInteger>)one).CompareTo(BigInteger.One)
    + " " + ((IEquatable<BigInteger>)one).Equals(BigInteger.One));
try { ((IComparable)one).CompareTo(1); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
var list = new List<object> { BigInteger.Parse("5"), BigInteger.MinusOne, BigInteger.Pow(10, 30) };
list.Sort((a, b) => ((IComparable)a).CompareTo(b));
Console.WriteLine(string.Join(",", list) + " " + Convert.ToString(o, odd));
