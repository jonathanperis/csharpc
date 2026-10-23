// Marshal string helpers round-trip through the platform's allocators and encodings (on Windows:
// LocalAlloc/CoTaskMemAlloc, the ANSI code page and UTF-16 for "Auto"; on Unix: malloc and UTF-8).
using System.Runtime.InteropServices;

foreach (var text in new[] { "hello", "héllo wörld", "" })
{
    IntPtr ansi = Marshal.StringToHGlobalAnsi(text);
    IntPtr auto = Marshal.StringToHGlobalAuto(text);
    IntPtr uni = Marshal.StringToCoTaskMemUni(text);
    IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(text);
    Console.WriteLine($"[{Marshal.PtrToStringAnsi(ansi)}] [{Marshal.PtrToStringAuto(auto)}] [{Marshal.PtrToStringUni(uni)}] [{Marshal.PtrToStringUTF8(utf8)}]");
    Console.WriteLine($"{Marshal.PtrToStringAnsi(ansi, Math.Min(2, text.Length))} {Marshal.PtrToStringUni(uni, Math.Min(1, text.Length))}".Length);
    Marshal.FreeHGlobal(ansi);
    Marshal.FreeHGlobal(auto);
    Marshal.FreeCoTaskMem(uni);
    Marshal.FreeCoTaskMem(utf8);
}
Console.WriteLine(Marshal.StringToHGlobalAnsi(null) == IntPtr.Zero);
IntPtr block = Marshal.AllocHGlobal(16);
Marshal.WriteInt32(block, 42);
block = Marshal.ReAllocHGlobal(block, 64);
Console.WriteLine(Marshal.ReadInt32(block));
Marshal.FreeHGlobal(block);
Console.WriteLine(Marshal.GetPInvokeErrorMessage(2));
