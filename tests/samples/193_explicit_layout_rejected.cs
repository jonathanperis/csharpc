// Explicit layouts of classes and Pack change where fields live; csharpc doesn't lay those out yet
// (structs with explicit offsets are supported: 536_struct_explicit_layout), so it must refuse these
// types instead of silently printing another union member's value.
using System.Runtime.InteropServices;

var u = new U();
u.I = 0x41424344;
Console.WriteLine(u.B0);
var p = new P { A = 1, B = 2 };
Console.WriteLine(p.A + p.B);

[StructLayout(LayoutKind.Explicit)]
class U { [FieldOffset(0)] public int I; [FieldOffset(0)] public byte B0; }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct P { public byte A; public long B; }
