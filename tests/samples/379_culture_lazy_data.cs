// Culture data csharpc reads on first use instead of when a culture is created: NumberFormatInfo's
// NativeDigits and DigitSubstitution, CultureInfo.LCID, the specific culture of a neutral culture, and the
// current culture in the C locale (created without loading ICU). The values must be .NET's.
using System.Globalization;

var current = CultureInfo.CurrentCulture;
Console.WriteLine($"current '{current.Name}' readOnly={current.IsReadOnly} same={ReferenceEquals(current, CultureInfo.InvariantCulture)} equal={current.Equals(CultureInfo.InvariantCulture)}");
Console.WriteLine($"current calendar {current.Calendar.GetType().Name} lcid={current.LCID}");
Console.WriteLine(1234567.891.ToString("N2") + " " + (-5).ToString() + " " + DateTime.MinValue.AddDays(738950.5).ToString());
Console.WriteLine(string.Join(",", current.NumberFormat.NativeDigits) + " " + current.NumberFormat.DigitSubstitution);

foreach (var name in new[] { "en-US", "pt-BR", "ar-SA", "fa-IR", "hi-IN", "th-TH", "de", "pt", "zh-Hans", "sr-Cyrl" })
{
    var culture = new CultureInfo(name);
    var nfi = culture.NumberFormat;
    Console.WriteLine($"{name}: lcid={culture.LCID} digits={string.Join("", nfi.NativeDigits)} subst={nfi.DigitSubstitution} neutral={culture.IsNeutralCulture}");
    // A clone (taken before the lazy values are read) and a read-only copy agree with the original.
    var clone = (NumberFormatInfo)new CultureInfo(name).NumberFormat.Clone();
    var readOnly = NumberFormatInfo.ReadOnly(new CultureInfo(name).NumberFormat);
    Console.WriteLine($"  clone {string.Join("", clone.NativeDigits)} {clone.DigitSubstitution}, readOnly {string.Join("", readOnly.NativeDigits)} {readOnly.DigitSubstitution}");
    if (culture.IsNeutralCulture)
        Console.WriteLine($"  specific {CultureInfo.CreateSpecificCulture(name).Name}");
    Console.WriteLine($"  {1234.5.ToString("N1", culture)} {(-42).ToString(culture)}");
}

// Setting the lazily read values before they are read.
var custom = (NumberFormatInfo)new CultureInfo("ar-SA").NumberFormat.Clone();
custom.DigitSubstitution = DigitShapes.None;
Console.WriteLine($"custom {custom.DigitSubstitution} {string.Join("", custom.NativeDigits)}");
foreach (var bad in new[] { "xx-yy-zz-not-a-culture", "pt-XX-invalid", "nothing" })
{
    try { Console.WriteLine(CultureInfo.CreateSpecificCulture(bad).Name); }
    catch (CultureNotFoundException e) { Console.WriteLine($"{e.GetType().Name} {e.InvalidCultureName}"); }
}
Console.WriteLine(CultureInfo.GetCultureInfo("fr").Equals(new CultureInfo("fr")));
Console.WriteLine(new CultureInfo("en-US").GetHashCode() == CultureInfo.GetCultureInfo("en-US").GetHashCode());
