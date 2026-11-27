// Interop members from #99: nint/nuint.Add/Subtract, Marshal.GetHRForException, Marshal.WriteInt16's char overloads,
// NativeLibrary.Load(name, assembly, searchPath) and OperatingSystem.IsMacOSVersionAtLeast.
using System;
using System.Runtime.InteropServices;

static class Program
{
    static void Main()
    {
        Console.WriteLine($"{nint.Add(5, 3)} {nint.Subtract(5, 3)} {nint.Add(5, -7)} {nuint.Add(5, 3)} {nuint.Subtract(5, 3)}");
        Console.WriteLine($"{Marshal.GetHRForException(new InvalidOperationException()):X} {Marshal.GetHRForException(new FormatException()):X} {Marshal.GetHRForException(null!)}");

        IntPtr p = Marshal.AllocHGlobal(8);
        Marshal.WriteInt16(p, 'A');
        Marshal.WriteInt16(p, 2, '€');
        Console.WriteLine($"{Marshal.ReadInt16(p)} {(char)Marshal.ReadInt16(p, 2)}");
        Marshal.FreeHGlobal(p);

        string library = OperatingSystem.IsWindows() ? "kernel32.dll" : OperatingSystem.IsMacOS() ? "libSystem.dylib" : "libc.so.6";
        IntPtr handle = NativeLibrary.Load(library, typeof(Program).Assembly, null);
        Console.WriteLine($"loaded: {handle != IntPtr.Zero}");
        try { NativeLibrary.Load("csharpc-no-such-library", typeof(Program).Assembly, DllImportSearchPath.SafeDirectories); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }
        try { NativeLibrary.Load(library, null!, null); }
        catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

        bool mac = OperatingSystem.IsMacOS();
        Console.WriteLine($"mac 10.0: {OperatingSystem.IsMacOSVersionAtLeast(10, 0) == mac} mac 999: {OperatingSystem.IsMacOSVersionAtLeast(999)}");
    }
}
