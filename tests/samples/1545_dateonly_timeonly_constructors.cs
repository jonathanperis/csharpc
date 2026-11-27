// DateOnly(year, month, day, Calendar) and TimeOnly(hour, minute, second, millisecond, microsecond) (#197).
using System;
using System.Globalization;

static class Program
{
    static void Try(string label, Func<object> make)
    {
        try { Console.WriteLine($"{label}: {make()}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var inv = CultureInfo.InvariantCulture;
        Try("gregorian", () => new DateOnly(2024, 2, 29, new GregorianCalendar()).ToString("O", inv));
        Try("julian", () => new DateOnly(2024, 2, 29, new JulianCalendar()).ToString("O", inv));
        Try("thai", () => new DateOnly(2567, 1, 15, new ThaiBuddhistCalendar()).ToString("O", inv));
        Try("hebrew", () => new DateOnly(5784, 7, 1, new HebrewCalendar()).ToString("O", inv));
        Try("julian day number", () => new DateOnly(1, 1, 1, new JulianCalendar()).DayNumber);
        Try("invalid", () => new DateOnly(2023, 2, 29, new GregorianCalendar()));
        Try("null calendar", () => new DateOnly(2024, 1, 1, null!));

        Try("micro", () => new TimeOnly(13, 45, 30, 250, 999).ToString("HH:mm:ss.ffffff", inv));
        Try("micro ticks", () => new TimeOnly(0, 0, 0, 0, 7).Ticks);
        Try("micro equals", () => new TimeOnly(1, 2, 3, 4, 5) == new TimeOnly(1, 2, 3, 4).Add(TimeSpan.FromTicks(50)));
        Try("micro out of range", () => new TimeOnly(1, 2, 3, 4, 1000));
        Try("negative micro", () => new TimeOnly(1, 2, 3, 4, -1));
        Try("bad millisecond first", () => new TimeOnly(1, 2, 3, 1000, 1000));
        Try("bad hour first", () => new TimeOnly(24, 2, 3, 4, 1000));
    }
}
