#:property AllowUnsafeBlocks=true
// More of what a Roslyn-hosting program reaches (M6): TypeInfo's Declared* reflection through
// typeof(X).GetTypeInfo(), Encoding.WindowsCodePage, Marshal's COM members as .NET has them off Windows,
// GetHRForLastWin32Error, and an explicit layout with Pack (System.Text.Encoding.CodePages' CodePageIndex).
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

Console.WriteLine($"declared Spin: {string.Join(", ", typeof(Widget).GetTypeInfo().GetDeclaredMethods("Spin").Select(m => m.ToString()).OrderBy(s => s, StringComparer.Ordinal))}");
Console.WriteLine($"declared Hidden: {typeof(Widget).GetTypeInfo().GetDeclaredMethod("Hidden")} none: {typeof(Widget).GetTypeInfo().GetDeclaredMethod("Nope")?.ToString() ?? "null"} inherited: {typeof(Widget).GetTypeInfo().GetDeclaredMethods("ToString").Count()}");
Console.WriteLine($"declared constructors: {string.Join(", ", typeof(Widget).GetTypeInfo().DeclaredConstructors.Select(c => c.ToString()).OrderBy(s => s, StringComparer.Ordinal))}");

foreach (var e in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.ASCII, Encoding.Latin1 })
    Console.WriteLine($"{e.WebName} {e.WindowsCodePage}");

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine($"clsid type: {Marshal.GetTypeFromCLSID(Guid.Empty)?.ToString() ?? "null"}");
    try { Marshal.GetNativeVariantForObject(1, IntPtr.Zero); }
    catch (PlatformNotSupportedException e) { Console.WriteLine(e.Message); }
}
else Console.WriteLine("clsid type: null" + Environment.NewLine + "COM Interop is not supported on this platform.");
foreach (var error in new[] { 0, 2, 5, unchecked((int)0x80004005), 0x12345678 })
{
    Marshal.SetLastPInvokeError(error);
    Console.WriteLine($"hr for {error}: 0x{Marshal.GetHRForLastWin32Error():X8}");
}

unsafe
{
    var index = new CodePageIndex { CodePage = 1252, ByteCount = 1, Offset = 0x01020304 };
    byte* p = (byte*)&index;
    Console.WriteLine($"CodePageIndex: size {sizeof(CodePageIndex)}, CodePage at {(byte*)&index.CodePage - p}, Offset at {(byte*)&index.Offset - p}, {index.CodePage} {index.ByteCount} 0x{index.Offset:X8}");
    var pair = new PackedPair { A = 1, B = 2 };
    Console.WriteLine($"PackedPair: size {sizeof(PackedPair)}, {pair.A + pair.B}");
    var holder = new Holder { Before = 9, Index = index };
    Console.WriteLine($"Holder: size {sizeof(Holder)}, Index at {(byte*)&holder.Index - (byte*)&holder}, {holder.Index.CodePage}");
}

class Widget
{
    public Widget() { }
    private Widget(int size) { }
    static Widget() { }
    public void Spin() { }
    public static int Spin(int times) => times;
    private void Hidden() { }
}

[StructLayout(LayoutKind.Explicit, Pack = 2)]
struct CodePageIndex
{
    [FieldOffset(0)] public char CodePageName;
    [FieldOffset(32)] public short CodePage;
    [FieldOffset(34)] public short ByteCount;
    [FieldOffset(36)] public int Offset;
}

[StructLayout(LayoutKind.Explicit, Pack = 8)]
struct PackedPair { [FieldOffset(0)] public int A; [FieldOffset(4)] public int B; }

struct Holder { public byte Before; public CodePageIndex Index; }
