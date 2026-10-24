// UriFormatException names its constructor parameters textString and e: the message and inner
// exception were dropped (Message printed "Exception of type 'System.UriFormatException' was thrown.").
using System;

Console.WriteLine(new UriFormatException("custom").Message);
Console.WriteLine(new UriFormatException().Message);
var withInner = new UriFormatException("outer", new InvalidOperationException("inner"));
Console.WriteLine(withInner.Message + " / " + withInner.InnerException!.Message);
Console.WriteLine(new System.IO.EndOfStreamException("eos").Message);
Console.WriteLine(new TimeoutException("t").Message);
try { throw new UriFormatException("thrown"); }
catch (FormatException e) { Console.WriteLine(e.Message + " " + e.GetType().Name); }
