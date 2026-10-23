// [StructLayout(LayoutKind.Explicit)] / [FieldOffset] unions and Pack/Size change where fields live;
// csharpc lays fields out sequentially, so it must refuse these types instead of silently printing
// another union member's value.
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
