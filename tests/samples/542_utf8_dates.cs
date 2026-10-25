// Utf8Parser/Utf8Formatter for DateTime and DateTimeOffset in the 'G', 'R', 'l' and 'O' standard formats
// (docs-standard-datetime-system-text-json-support-datetime-converter-examples-example2).
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;

string[] inputs =
{
    "Thu, 25 Jul 2019 13:36:07 GMT", "thu, 25 jul 2019 13:36:07 gmt", "Fri, 25 Jul 2019 13:36:07 GMT",
    "Thu, 25 Jul 2019 13:36:07 GMTxyz", "Mon, 29 Feb 2016 00:00:00 GMT", "Mon, 29 Feb 2015 00:00:00 GMT",
    "2017-06-12T05:30:45.7680000", "2017-06-12T05:30:45.7680000Z", "2017-06-12T05:30:45.7680000-07:00",
    "2017-06-12T24:00:00.0000000Z", "05/25/2017 10:30:15", "05/25/2017 10:30:15 -08:00", "13/25/2017 10:30:15", "garbage",
};
foreach (var input in inputs)
{
    var bytes = Encoding.ASCII.GetBytes(input);
    foreach (char format in new[] { 'R', 'l', 'O', 'G', default(char) })
    {
        bool ok = Utf8Parser.TryParse(bytes, out DateTime value, out int consumed, format);
        bool okOffset = Utf8Parser.TryParse(bytes, out DateTimeOffset offset, out int consumedOffset, format);
        string dt = ok && value.Kind != DateTimeKind.Local ? $"{value.Ticks} {value.Kind} ({consumed})" : ok ? $"local ({consumed})" : "-";
        string dto = okOffset && format is 'R' or 'l' or 'O' ? $"{offset.UtcTicks} {offset.Offset} ({consumedOffset})" : okOffset ? $"ok ({consumedOffset})" : "-";
        Console.WriteLine($"{input} [{(format == default ? "default" : format.ToString())}]: {dt} | {dto}");
    }
}

try { Utf8Parser.TryParse("x"u8, out DateTime _, out _, 'Q'); }
catch (FormatException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

var date = new DateTime(2019, 7, 25, 13, 36, 7, 123, DateTimeKind.Utc).AddTicks(4567);
var unspecified = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Unspecified);
var withOffset = new DateTimeOffset(2017, 5, 25, 10, 30, 15, TimeSpan.FromHours(-8));
Span<byte> buffer = stackalloc byte[64];
foreach (var format in new[] { default, new StandardFormat('R'), new StandardFormat('l'), new StandardFormat('O'), new StandardFormat('G') })
{
    string name = format.IsDefault ? "default" : format.ToString();
    foreach (var d in new[] { date, unspecified })
    {
        bool ok = Utf8Formatter.TryFormat(d, buffer, out int written, format);
        Console.WriteLine($"DateTime {name}: {ok} {Encoding.ASCII.GetString(buffer.Slice(0, written))}");
    }
    bool okOffset = Utf8Formatter.TryFormat(withOffset, buffer, out int writtenOffset, format);
    Console.WriteLine($"DateTimeOffset {name}: {okOffset} {Encoding.ASCII.GetString(buffer.Slice(0, writtenOffset))}");
}
Console.WriteLine($"too small: {Utf8Formatter.TryFormat(date, buffer.Slice(0, 10), out int w, new StandardFormat('R'))} {w}");
try { Utf8Formatter.TryFormat(date, buffer, out _, new StandardFormat('Q')); }
catch (FormatException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
