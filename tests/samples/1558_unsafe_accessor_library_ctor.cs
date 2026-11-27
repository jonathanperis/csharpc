// [UnsafeAccessor(UnsafeAccessorKind.Constructor)] of library types (#99): created as `new T(...)` is; DateTime's private
// constructor from its raw data too. (Not generic types: .NET's runtime rejects a constructor accessor of List<int>.)
using System;
using System.Runtime.CompilerServices;
using System.Text;

static class Program
{
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern DateTime NewDateTime(long ticks);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern DateTime NewDateTimeKind(long ticks, DateTimeKind kind);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern TimeSpan NewTimeSpan(int hours, int minutes, int seconds);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern StringBuilder NewBuilder(string value);

    // DateTime's private constructor from its raw data (ticks | kind << 62).
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern DateTime NewDateTimeData(ulong dateData);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern Version NewVersion(int major, int minor);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern Guid NewGuid(string text);

    static void Main()
    {
        Console.WriteLine(NewDateTime(TimeSpan.TicksPerDay).Day);
        var utc = NewDateTimeKind(638000000000000000, DateTimeKind.Utc);
        Console.WriteLine($"{utc.Ticks} {utc.Kind} {utc.Year}");
        Console.WriteLine(NewTimeSpan(1, 2, 3));
        Console.WriteLine(NewBuilder("ab").Append('c'));
        var local = NewDateTimeData(630822816000000000UL | (2UL << 62));
        Console.WriteLine($"{local.Ticks} {local.Kind} {NewDateTimeData(5).Ticks} {NewDateTimeData(5).Kind}");
        Console.WriteLine(NewVersion(4, 2));
        Console.WriteLine(NewGuid("00112233-4455-6677-8899-aabbccddeeff"));
        try { NewDateTime(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
    }
}
