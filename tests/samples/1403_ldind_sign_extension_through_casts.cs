#:property AllowUnsafeBlocks=true
// A small integer read through a pointer of the other signedness (`*(sbyte*)p` with p a byte*, as Ascii.GetIndexOfFirstNonAsciiByte
// tests the last byte): the read's type decides sign or zero extension, not the pointer's declared type (#423). Matters for the
// IL front-end, where the cast leaves no conversion behind (ldarg; ldind.i1).
using System;

static unsafe class Program
{
    static bool IsAsciiByte(byte* p) => *(sbyte*)p >= 0;
    static int AsSigned16(ushort* p) => *(short*)p;
    static int AsUnsigned16(short* p) => *(ushort*)p;
    static int AsUnsigned8(sbyte* p) => *(byte*)p;
    static int CharAsSigned(char* p) => *(short*)p;

    static void Main()
    {
        byte[] bytes = { 0x41, 0xE2, 0x7F, 0x80, 0xFF };
        fixed (byte* b = bytes)
        {
            for (int i = 0; i < bytes.Length; i++) Console.Write(IsAsciiByte(b + i) ? "A" : "n");
            Console.WriteLine();
            for (int i = 0; i < bytes.Length; i++) Console.Write(AsUnsigned8((sbyte*)(b + i)) + " ");
            Console.WriteLine();
        }
        ushort[] words = { 0x0001, 0x8000, 0xFFFF, 0x7FFF };
        fixed (ushort* w = words)
            for (int i = 0; i < words.Length; i++) Console.Write(AsSigned16(w + i) + " " + AsUnsigned16((short*)(w + i)) + " | ");
        Console.WriteLine();
        string text = "a￾老";
        fixed (char* c = text)
            for (int i = 0; i < text.Length; i++) Console.Write(CharAsSigned(c + i) + " ");
        Console.WriteLine();
    }
}
