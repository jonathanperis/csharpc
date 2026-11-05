// Half formatted with a named culture: its decimal separator (de-DE, fr-FR). WebAssembly runs in
// globalization-invariant mode, where creating a named culture is a CultureNotFoundException, so the sample is
// recorded for the other targets only.
using System;
using System.Globalization;

Half a = (Half)1.5f, b = (Half)0.1f;
Console.WriteLine(a.ToString(CultureInfo.GetCultureInfo("de-DE")) + " " + b.ToString(CultureInfo.GetCultureInfo("de-DE")) + " " + b.ToString("G", CultureInfo.GetCultureInfo("fr-FR")));
Console.WriteLine(a.ToString("F2", CultureInfo.GetCultureInfo("de-DE")) + " " + b.ToString("E2", CultureInfo.GetCultureInfo("fr-FR")) + " " + a.ToString("N1", CultureInfo.GetCultureInfo("de-DE")));
