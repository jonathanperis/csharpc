#:property AllowUnsafeBlocks=true
using System.Runtime.InteropServices;
unsafe
{
    var r = Native.div(17, 5);
    Console.WriteLine($"div: {r.quot} rem {r.rem}");
    int[] data = [5, 3, 9, 1, 7];
    fixed (int* p = data) Native.qsort(p, (nuint)data.Length, sizeof(int), &Compare);
    Console.WriteLine(string.Join(",", data));
    Console.WriteLine(Native.open("/nonexistent/file", 0) + " errno=" + Marshal.GetLastPInvokeError());
    Console.WriteLine(Native.cos(0.0));
    Console.WriteLine(Native.strtol("ff", out var end, 16) + " " + (end != IntPtr.Zero));
    IntPtr mem = Marshal.AllocHGlobal(16);
    Marshal.WriteInt32(mem, 4, 1234);
    Console.WriteLine(Marshal.ReadInt32(mem, 4));
    Marshal.FreeHGlobal(mem);
    var handle = NativeLibrary.Load("libm.so.6");
    var sqrt = (delegate* unmanaged<double, double>)NativeLibrary.GetExport(handle, "sqrt");
    Console.WriteLine(sqrt(2.0));
    Console.WriteLine(Native.toupper('a') == 'A');
    try { Native.missing(); } catch (DllNotFoundException) { Console.WriteLine("DllNotFoundException"); }
    try { Native.nosuch(); } catch (EntryPointNotFoundException e) { Console.WriteLine(e.Message); }
}

[UnmanagedCallersOnly]
static unsafe int Compare(void* a, void* b) => *(int*)a - *(int*)b;

struct DivT { public int quot; public int rem; }

static unsafe class Native
{
    [DllImport("libc")] public static extern DivT div(int numerator, int denominator);
    [DllImport("libc")] public static extern void qsort(void* b, nuint n, nuint size, delegate* unmanaged<void*, void*, int> cmp);
    [DllImport("libc", SetLastError = true)] public static extern int open(string path, int flags);
    [DllImport("libm.so.6")] public static extern double cos(double x);
    [DllImport("libc")] public static extern long strtol(string s, out IntPtr end, int radix);
    [DllImport("libc")] public static extern int toupper(int c);
    [DllImport("libdoesnotexist")] public static extern void missing();
    [DllImport("libc", EntryPoint = "no_such_function_xyz")] public static extern void nosuch();
}
