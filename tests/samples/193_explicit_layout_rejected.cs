// Explicit layouts of classes change where fields live; csharpc doesn't lay those out yet (structs with
// explicit offsets, overlapping fields and Pack are supported: 536_struct_explicit_layout,
// 720_struct_overlapping_packed_layout), so it must refuse this type instead of silently printing
// another union member's value.
using System.Runtime.InteropServices;

var u = new U();
u.I = 0x41424344;
Console.WriteLine(u.B0);

[StructLayout(LayoutKind.Explicit)]
class U { [FieldOffset(0)] public int I; [FieldOffset(0)] public byte B0; }
