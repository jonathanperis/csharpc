using IlLegacyIdioms;
using IlRoslynIdioms;

Special a = 3, b = 3, c = 7;
Console.WriteLine($"{Special.Size} {a} {a.Equals(b)} {a.Equals(c)} {a.Equals((object)b)}");
var byKind = new Dictionary<Special, string> { [a] = "three", [c] = "seven" };
Console.WriteLine(byKind[b]);

Console.WriteLine($"{Decimals.Zero} {Decimals.One} {Decimals.MinusOne} {Decimals.Max} {Decimals.Min}");
Console.WriteLine($"{Decimals.Sum([1.5m, 2.25m])} {Decimals.Sum([])} {Decimals.FromBits(-12.345m)}");
Console.WriteLine($"{InlineArrays.Join("a", "b", "c", "d", "e")} {InlineArrays.SumOf(1, 2, 3)} {InlineArrays.Many(100)} {InlineArrays.Describe("x", 4)}");
var wrapped = new ArrayWrapper<string>(["x", "y", "z"]);
var copy = new string[4];
wrapped.CopyTo(copy, 1);
System.Collections.IList untyped = wrapped;
Console.WriteLine($"{wrapped.Contains("y")} {wrapped.Contains("w")} {wrapped.IndexOf("z")} {string.Join(",", copy)} {untyped.Contains("x")} {untyped.IndexOf("y")} {untyped.IndexOf(3)}");
try { wrapped.Clear(); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
untyped.Clear();
Console.WriteLine(string.Join(",", wrapped.Select(s => s ?? "null")));
Console.WriteLine($"{Matrices.Closure(4)} {Matrices.Cells()} {Matrices.OutOfRange()}");
Console.WriteLine($"{Pointers.SumChars("abc")} {Pointers.SumChars("")} {Pointers.SumBytes([1, 2, 250])} {Pointers.SumBytes([])} {Pointers.FirstOf([42, 1])} {Pointers.Pinned([3, 4, 5])} {Pointers.Pinned([])} {Pointers.MinTicks()}");
Console.WriteLine(HashState.Run());
Console.WriteLine(Unions.Run());

// Assembly attributes of an IL assembly, as Roslyn reads its own version and commit hash.
var idioms = typeof(Special).Assembly;
Console.WriteLine(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(idioms)?.InformationalVersion);
Console.WriteLine(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyFileVersionAttribute>(idioms)?.Version);
Console.WriteLine(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyCompanyAttribute>(idioms)?.Company);
Console.WriteLine(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyProductAttribute>(idioms)?.Product);
var metadata = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyMetadataAttribute>(idioms);
Console.WriteLine($"{metadata?.Key}={metadata?.Value}");
Console.WriteLine(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyCopyrightAttribute>(idioms) is null);
var framework = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>(idioms);
Console.WriteLine($"{framework?.FrameworkName} [{framework?.FrameworkDisplayName}]");

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
