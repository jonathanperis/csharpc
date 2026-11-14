#:property AllowUnsafeBlocks=true
// Marshal.SizeOf and OffsetOf of structs with fixed-size buffers: the compiler's <name>e__FixedBuffer struct is as large as
// its elements (StructLayout.Size) and aligned as one element marshalled with the container's CharSet.
using System;
using System.Runtime.InteropServices;

unsafe struct Checkpoint
{
    public ulong ItemId;
    public short AccountIdLength;
    public fixed char AccountIdBuffer[40];
    public short HandlerIdLength;
    public fixed char HandlerIdBuffer[8];
}

unsafe struct Ints { public byte A; public fixed int B[3]; public byte C; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
unsafe struct Wide { public byte A; public fixed char B[5]; public byte C; }

unsafe struct Narrow { public byte A; public fixed char B[5]; public byte C; }

unsafe struct Longs { public byte A; public fixed long B[2]; public byte C; }

// bool marshals as a 4-byte BOOL: the buffer is the larger of one marshalled element and its managed size.
unsafe struct Bools { public byte A; public fixed bool B[3]; public int C; }

struct Outer { public byte Tag; public Ints Inner; }

static unsafe class Program
{
    static void Main()
    {
        Console.WriteLine($"Checkpoint {Marshal.SizeOf<Checkpoint>()} {sizeof(Checkpoint)} {Marshal.OffsetOf<Checkpoint>(nameof(Checkpoint.AccountIdBuffer))} {Marshal.OffsetOf<Checkpoint>(nameof(Checkpoint.HandlerIdLength))} {Marshal.OffsetOf<Checkpoint>(nameof(Checkpoint.HandlerIdBuffer))}");
        Console.WriteLine($"Ints {Marshal.SizeOf<Ints>()} {sizeof(Ints)} {Marshal.OffsetOf<Ints>("B")} {Marshal.OffsetOf<Ints>("C")}");
        Console.WriteLine($"Wide {Marshal.SizeOf<Wide>()} {sizeof(Wide)} {Marshal.OffsetOf<Wide>("B")} {Marshal.OffsetOf<Wide>("C")}");
        Console.WriteLine($"Narrow {Marshal.SizeOf<Narrow>()} {sizeof(Narrow)} {Marshal.OffsetOf<Narrow>("B")} {Marshal.OffsetOf<Narrow>("C")}");
        Console.WriteLine($"Longs {Marshal.SizeOf<Longs>()} {sizeof(Longs)} {Marshal.OffsetOf<Longs>("B")} {Marshal.OffsetOf<Longs>("C")}");
        Console.WriteLine($"Bools {Marshal.SizeOf<Bools>()} {sizeof(Bools)} {Marshal.OffsetOf<Bools>("B")} {Marshal.OffsetOf<Bools>("C")}");
        Console.WriteLine($"Outer {Marshal.SizeOf<Outer>()} {Marshal.OffsetOf<Outer>("Inner")}");
        Console.WriteLine($"typeof {Marshal.SizeOf(typeof(Checkpoint))}");
    }
}
