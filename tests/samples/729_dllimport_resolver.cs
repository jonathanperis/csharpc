// NativeLibrary.SetDllImportResolver (how csharpc itself finds libLLVM): the declaring assembly's resolver
// gets the library name first, a handle it returns is searched for the entry point, IntPtr.Zero falls back
// to the default probing, its exceptions reach the P/Invoke's caller, and an assembly gets one resolver.
using System;
using System.Reflection;
using System.Runtime.InteropServices;

var calls = new System.Collections.Generic.List<string>();
NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, assembly, path) =>
{
    calls.Add($"{name} {assembly == typeof(Native).Assembly} {path?.ToString() ?? "null"}");
    return name switch
    {
        "alias-of-libc" => NativeLibrary.Load(OperatingSystem.IsWindows() ? "msvcrt.dll" : OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6"),
        "failing-library" => throw new InvalidOperationException("the resolver refused " + name),
        _ => IntPtr.Zero,
    };
});

Console.WriteLine($"abs(-5) = {Native.abs(-5)}, labs = {Native.abs(-7)}");
try { Native.Missing(); } catch (EntryPointNotFoundException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { Native.Refused(); } catch (InvalidOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { Native.Unknown(); } catch (DllNotFoundException e) { Console.WriteLine(e.GetType().Name); }
try { NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (_, _, _) => IntPtr.Zero); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { NativeLibrary.SetDllImportResolver(null!, (_, _, _) => IntPtr.Zero); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
try { NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
Console.WriteLine(string.Join(" | ", calls));

static class Native
{
    [DllImport("alias-of-libc")] public static extern int abs(int value);
    [DllImport("alias-of-libc", EntryPoint = "csharpc_no_such_function")] public static extern void Missing();
    [DllImport("failing-library")] public static extern void Refused();
    [DllImport("csharpc-no-such-library")] public static extern void Unknown();
}
