using System.Runtime.InteropServices;

// Every import names the library "nativeadd": under `dotnet run` it is loaded from libnativeadd.so/.dylib next to the
// program; under csharpc with DirectPInvoke it is the static archive linked into the executable.
unsafe class Program
{
    [DllImport("nativeadd")] static extern int native_add(int a, int b);
    [DllImport("nativeadd", EntryPoint = "native_scale")] static extern double Scale(double x, int times);
    [DllImport("nativeadd")] static extern int native_counter_next();
    [DllImport("nativeadd")] static extern int native_length([MarshalAs(UnmanagedType.LPStr)] string text);
    [DllImport("nativeadd")] static extern void native_fill(byte* buffer, int length);
    [DllImport("nativeadd", SetLastError = true)] static extern int native_fail();
    [DllImport("nativeadd")] static extern Point native_swap(Point p);
    [DllImport("nativeadd")] static extern int native_sum_with_callback(delegate* unmanaged<int, int> callback, int count);

    struct Point { public int X, Y; }

    [UnmanagedCallersOnly]
    static int Square(int n) => n * n;

    static void Main()
    {
        Console.WriteLine(native_add(40, 2));
        Console.WriteLine(native_add(int.MaxValue, 1));
        Console.WriteLine(Scale(1.5, 4));
        Console.WriteLine($"{native_counter_next()} {native_counter_next()} {native_counter_next()}");
        Console.WriteLine(native_length("héllo, wörld"));
        var buffer = new byte[8];
        fixed (byte* p = buffer) native_fill(p, buffer.Length);
        Console.WriteLine(string.Join(",", buffer));
        Console.WriteLine($"{native_fail()} errno={Marshal.GetLastPInvokeError()}");
        var swapped = native_swap(new Point { X = 1, Y = 2 });
        Console.WriteLine($"{swapped.X} {swapped.Y}");
        Console.WriteLine(native_sum_with_callback(&Square, 4));
        Console.WriteLine(native_add(1, 2) == 3 ? "done" : "wrong");
    }
}
