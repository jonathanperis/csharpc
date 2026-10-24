using IlLegacyIdioms;
using IlRoslynIdioms;

Special a = 3, b = 3, c = 7;
Console.WriteLine($"{Special.Size} {a} {a.Equals(b)} {a.Equals(c)} {a.Equals((object)b)}");
var byKind = new Dictionary<Special, string> { [a] = "three", [c] = "seven" };
Console.WriteLine(byKind[b]);

Console.WriteLine($"{Decimals.Zero} {Decimals.One} {Decimals.MinusOne} {Decimals.Max} {Decimals.Min}");
Console.WriteLine($"{Decimals.Sum([1.5m, 2.25m])} {Decimals.Sum([])} {Decimals.FromBits(-12.345m)}");
Console.WriteLine($"{Pointers.SumChars("abc")} {Pointers.SumChars("")} {Pointers.SumBytes([1, 2, 250])} {Pointers.SumBytes([])} {Pointers.FirstOf([42, 1])}");

// IntPtr/UIntPtr conversion operators as C# 9 IL calls them: the narrowing ones are checked.
Console.WriteLine($"{NativeInts.FromInt(-5)} {NativeInts.FromLong(1L << 40)} {NativeInts.ToLong(new IntPtr(-7))}");
Console.WriteLine($"{NativeInts.ToInt(new IntPtr(123456))} {NativeInts.ToULong(new UIntPtr(99))} {NativeInts.RoundTrip(5)}");
unsafe
{
    int local = 3;
    Console.WriteLine(NativeInts.ToPointer(NativeInts.FromPointer(&local)) == &local);
    Console.WriteLine((nuint)NativeInts.FromUPointer(&local) == (nuint)(&local));
}
try { NativeInts.ToInt(new IntPtr(1L << 40)); Console.WriteLine("no overflow"); }
catch (OverflowException e) { Console.WriteLine(e.Message); }
try { NativeInts.ToUInt(new UIntPtr(1UL << 33)); Console.WriteLine("no overflow"); }
catch (OverflowException e) { Console.WriteLine(e.Message); }
Console.WriteLine(NativeInts.ToUInt(NativeInts.FromULong(4000000000)));
