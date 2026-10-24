// Exception messages of the globalization code with formatted arguments (filled in without composite
// formatting in csharpc): culture ids in decimal and hex, calendar ranges with dates, numeric ranges.
using System.Globalization;

void Show(Action action)
{
    try { action(); Console.WriteLine("no exception"); }
    catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}

Show(() => new RegionInfo(9));
Show(() => new RegionInfo(0x7F));
Show(() => new CultureInfo(0x12345));
Show(() => new CultureInfo(-5));
Show(() => new GregorianCalendar().AddYears(DateTime.MaxValue, 1));
Show(() => new GregorianCalendar().GetDaysInMonth(2024, 13));
Show(() => new GregorianCalendar().ToDateTime(2024, 2, 30, 0, 0, 0, 0));
Show(() => new HijriCalendar().GetYear(DateTime.MinValue));
Show(() => new HebrewCalendar().GetYear(DateTime.MinValue));
Show(() => new UmAlQuraCalendar().GetYear(DateTime.MaxValue));
Show(() => new JapaneseCalendar().ToDateTime(100, 1, 1, 0, 0, 0, 0, 5));
Show(() => CultureInfo.GetCultureInfo("not a culture!"));
