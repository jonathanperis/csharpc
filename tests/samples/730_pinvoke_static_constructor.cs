#:property AllowUnsafeBlocks=true
// A P/Invoke is a static method of its type: a precise static constructor runs before the call, so a
// DllImport resolver registered there (as csharpc's own Llvm.Native does) is used. csharpc used to skip
// the static constructor for calls to extern methods; [LibraryImport] wrappers go through it too.
using System;
using System.Runtime.InteropServices;

Console.WriteLine("main");
Console.WriteLine(Native.abs(-3));
Console.WriteLine(Wrapped.Abs(-4));
Console.WriteLine(Generated.abs3(-5));
Console.WriteLine(Native.abs(-6));

static class Resolver
{
    public static int Calls;

    public static IntPtr Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? path)
    {
        Calls++;
        Console.WriteLine($"resolve {name} (call {Calls})");
        return name == "alias-of-libc"
            ? NativeLibrary.Load(OperatingSystem.IsWindows() ? "msvcrt.dll" : OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6")
            : IntPtr.Zero;
    }
}

static class Native
{
    static Native()
    {
        Console.WriteLine("Native cctor");
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolver.Resolve);
    }

    [DllImport("alias-of-libc")] public static extern int abs(int value);
}

static class Wrapped
{
    static Wrapped() => Console.WriteLine("Wrapped cctor");
    public static int Abs(int value) => abs2(value);
    [DllImport("alias-of-libc", EntryPoint = "abs")] private static extern int abs2(int value);
}

static partial class Generated
{
    static Generated() => Console.WriteLine("Generated cctor");
    [LibraryImport("alias-of-libc", EntryPoint = "abs")] public static partial int abs3(int value);
}
