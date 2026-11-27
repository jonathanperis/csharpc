// Generic code over Nullable<T>, enums and Half asking interface questions of a boxed T and calling through the interface (issue 305):
// .NET boxes an int? as its int (or null), System.Enum implements ISpanFormattable, Half implements IUtf8SpanFormattable.
#nullable disable
using System;
enum Color { Red, Green }
class P {
  static string T1<T>(T v) => (v is IFormattable) + " " + ((object)v is ISpanFormattable) + " " + (v is IUtf8SpanFormattable) + " " + (v is IDisposable) + " " + (v is IComparable<T>);
  static string F<T>(T v) => v is IFormattable f ? f.ToString(null, null) : "nf";
  static string S<T>(T v) { if (v is ISpanFormattable s) { Span<char> b = stackalloc char[64]; return s.TryFormat(b, out var n, default, null) ? new string(b.Slice(0, n)) : "x"; } return "ns"; }
  static string U<T>(T v) { if (v is IUtf8SpanFormattable s) { Span<byte> b = stackalloc byte[64]; return s.TryFormat(b, out var n, default, null) ? System.Text.Encoding.UTF8.GetString(b.Slice(0, n)) : "x"; } return "nu"; }
  static void Main() {
    Console.WriteLine(T1((int?)5) + " | " + T1((int?)null) + " | " + F((int?)5) + " " + F((int?)null) + " " + S((int?)7) + " " + U((int?)8));
    Console.WriteLine(T1(Color.Green) + " | " + F(Color.Green) + " " + S(Color.Green) + " " + U(Color.Red));
    Console.WriteLine(T1((Half)1.5f) + " | " + F((Half)1.5f) + " " + S((Half)2.5f) + " " + U((Half)3.5f));
  }
}
