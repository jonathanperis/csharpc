using System.Runtime.InteropServices;

Console.WriteLine(RidPkg.Info.Describe());
Console.WriteLine(RidPkg.Info.Twice(21));
Console.WriteLine(Native.rid_native_answer());
Console.WriteLine(Native.rid_native_add(40, 2));
Console.WriteLine(NativeLibrary.TryLoad("no_such_native_library", out _));

static unsafe class Native
{
    [DllImport("ridnative")] public static extern int rid_native_answer();
    [DllImport("ridnative")] public static extern int rid_native_add(int a, int b);
}
