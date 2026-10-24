// StringWriter with a format provider: numbers, IFormattable objects and composite formats written
// through it use the provider (Roslyn writes diagnostics and syntax with StringWriter(CultureInfo.InvariantCulture)).
using System.Globalization;
using System.Text;

var german = new CultureInfo("de-DE");
using var writer = new StringWriter(german);
writer.Write(1.5);
writer.Write(' ');
writer.Write(1234.5m);
writer.Write(' ');
writer.Write((object)2.25);
writer.Write(' ');
writer.Write("{0:N1}", 9876.5);
writer.WriteLine(" {0} {1}", 0.5f, 7);
Console.Write(writer.ToString());
Console.WriteLine(ReferenceEquals(writer.FormatProvider, german));

var sb = new StringBuilder("> ");
using (var invariant = new StringWriter(sb, CultureInfo.InvariantCulture))
{
    invariant.Write(3.75);
    invariant.Write(" {0:F2}", 1.0 / 3);
}
Console.WriteLine(sb);
Console.WriteLine(new StringWriter().FormatProvider == CultureInfo.CurrentCulture);
