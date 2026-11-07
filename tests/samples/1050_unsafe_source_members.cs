#:property AllowUnsafeBlocks=true
// System.Runtime.CompilerServices.Unsafe called from source: the pointer/ref reinterpretation, arithmetic,
// comparison, block and read/write members, with ref locals and ref returns.
using System;
using System.Runtime.CompilerServices;

struct Pair { public int A; public int B; }
struct Wide { public long Value; }
struct Mixed { public byte Tag; public int Count; public short Extra; }

class Holder
{
    public int First = 11;
    public int Second = 22;
    public string Name = "holder";
}

unsafe class P
{
    static ref int Pick(int[] data, int index) => ref Unsafe.Add(ref data[0], index);

    static ref T At<T>(T[] data, nint index) => ref Unsafe.Add(ref data[0], index);

    static int Sum<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        int total = 0;
        ref byte start = ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in values[0]));
        for (int i = 0; i < values.Length * sizeof(T); i++) total += Unsafe.Add(ref start, i);
        return total;
    }

    static void Main()
    {
        // AsPointer / As<TFrom, TTo> / AsRef
        int x = 5;
        void* p = Unsafe.AsPointer(ref x);
        Console.WriteLine(*(int*)p);
        *(int*)p = 9;
        Console.WriteLine(x);
        uint u = Unsafe.As<int, uint>(ref x);
        Console.WriteLine(u);
        int neg = -1;
        Console.WriteLine(Unsafe.As<int, uint>(ref neg));
        Unsafe.As<int, uint>(ref neg) = 7;
        Console.WriteLine(neg);
        float f = 1.5f;
        Console.WriteLine($"{Unsafe.As<float, int>(ref f):X8}");
        Console.WriteLine(Unsafe.AsRef<int>(p));
        Unsafe.AsRef<int>(p) = 31;
        Console.WriteLine(x);
        Pair pair = new() { A = 1, B = 2 };
        ref Wide wide = ref Unsafe.As<Pair, Wide>(ref pair);
        Console.WriteLine($"{wide.Value:X16}");
        wide.Value = 0x0000000700000003L;
        Console.WriteLine($"{pair.A} {pair.B}");

        // As<T>(object)
        object boxed = "text";
        string text = Unsafe.As<string>(boxed);
        Console.WriteLine(text.Length);
        Console.WriteLine(ReferenceEquals(Unsafe.As<object>(text), text));

        // Add / Subtract (int, nint, nuint, void*)
        int[] arr = { 10, 20, 30, 40, 50 };
        ref int r = ref arr[0];
        Console.WriteLine(Unsafe.Add(ref r, 2));
        Console.WriteLine(Unsafe.Add(ref r, (nint)3));
        Console.WriteLine(Unsafe.Add(ref r, (nuint)4));
        ref int last = ref Unsafe.Add(ref r, 4);
        Console.WriteLine(Unsafe.Subtract(ref last, 1));
        Console.WriteLine(Unsafe.Subtract(ref last, (nint)2));
        Console.WriteLine(Unsafe.Subtract(ref last, (nuint)3));
        Unsafe.Add(ref r, 1) = 21;
        Console.WriteLine(arr[1]);
        Console.WriteLine(Pick(arr, 3));
        Pick(arr, 3)++;
        Console.WriteLine(arr[3]);
        At(arr, 2) += 100;
        Console.WriteLine(arr[2]);
        fixed (int* fp = arr)
        {
            int* third = (int*)Unsafe.Add<int>(fp, 2);
            Console.WriteLine(*third);
            int* back = (int*)Unsafe.Subtract<int>(third, 1);
            Console.WriteLine(*back);
        }
        long[] longs = { 1, 2, 3 };
        Console.WriteLine(Unsafe.Add(ref longs[0], 2));
        Pair[] pairs = { new Pair { A = 1, B = 2 }, new Pair { A = 3, B = 4 } };
        Console.WriteLine(Unsafe.Add(ref pairs[0], 1).B);
        Mixed[] mixed = { default, new Mixed { Tag = 1, Count = 2, Extra = 3 } };
        Console.WriteLine($"{Unsafe.SizeOf<Mixed>()} {Unsafe.Add(ref mixed[0], 1).Count}");

        // AddByteOffset / SubtractByteOffset / ByteOffset
        Console.WriteLine(Unsafe.AddByteOffset(ref r, (nint)8));
        Console.WriteLine(Unsafe.AddByteOffset(ref r, (nuint)12));
        Console.WriteLine(Unsafe.SubtractByteOffset(ref last, (nint)4));
        Console.WriteLine(Unsafe.SubtractByteOffset(ref last, (nuint)8));
        Console.WriteLine(Unsafe.ByteOffset(ref r, ref last));
        Console.WriteLine(Unsafe.ByteOffset(ref last, ref r));

        // AreSame / IsAddress*
        ref int again = ref arr[0];
        Console.WriteLine(Unsafe.AreSame(ref r, ref again));
        Console.WriteLine(Unsafe.AreSame(ref r, ref last));
        Console.WriteLine(Unsafe.IsAddressLessThan(ref r, ref last));
        Console.WriteLine(Unsafe.IsAddressLessThan(ref last, ref r));
        Console.WriteLine(Unsafe.IsAddressGreaterThan(ref last, ref r));
        Console.WriteLine(Unsafe.IsAddressGreaterThan(ref r, ref r));
        Console.WriteLine(Unsafe.IsAddressLessThanOrEqualTo(ref r, ref r));
        Console.WriteLine(Unsafe.IsAddressGreaterThanOrEqualTo(ref r, ref last));

        // NullRef / IsNullRef
        ref int none = ref Unsafe.NullRef<int>();
        Console.WriteLine(Unsafe.IsNullRef(ref none));
        Console.WriteLine(Unsafe.IsNullRef(ref r));
        Console.WriteLine(Unsafe.IsNullRef(ref Unsafe.NullRef<string>()));

        // SizeOf
        Console.WriteLine($"{Unsafe.SizeOf<byte>()} {Unsafe.SizeOf<short>()} {Unsafe.SizeOf<int>()} {Unsafe.SizeOf<long>()} {Unsafe.SizeOf<char>()} {Unsafe.SizeOf<bool>()} {Unsafe.SizeOf<double>()}");
        Console.WriteLine($"{Unsafe.SizeOf<Pair>()} {Unsafe.SizeOf<Wide>()} {Unsafe.SizeOf<Guid>()} {Unsafe.SizeOf<decimal>()}");

        // SkipInit
        Unsafe.SkipInit(out int skipped);
        skipped = 4;
        Console.WriteLine(skipped);

        // Read / Write (void*) and the byte-ref forms
        byte[] buffer = new byte[16];
        fixed (byte* b = buffer)
        {
            Unsafe.Write(b, 0x11223344);
            Unsafe.Write(b + 4, 2.5);
            Console.WriteLine($"{Unsafe.Read<int>(b):X8} {Unsafe.Read<double>(b + 4)}");
            Unsafe.WriteUnaligned(b + 1, 0x0A0B0C0Du);
            Console.WriteLine($"{Unsafe.ReadUnaligned<uint>(b + 1):X8} {Unsafe.ReadUnaligned<ushort>(b + 2):X4}");
            Unsafe.Write(b + 12, new Pair { A = 5, B = 6 });
        }
        Console.WriteLine(Unsafe.ReadUnaligned<Pair>(ref buffer[12]).B);
        Unsafe.WriteUnaligned(ref buffer[8], (short)0x7F7E);
        Console.WriteLine($"{Unsafe.ReadUnaligned<short>(ref buffer[8]):X4} {buffer[8]} {buffer[9]}");
        Unsafe.WriteUnaligned(ref buffer[0], true);
        Console.WriteLine(Unsafe.ReadUnaligned<bool>(ref buffer[0]));

        // Copy
        int source = 123, dest = 0;
        Unsafe.Copy(ref dest, &source);
        Console.WriteLine(dest);
        int target = 0;
        Unsafe.Copy(&target, ref source);
        Console.WriteLine(target);

        // CopyBlock / InitBlock (+ Unaligned), void* and ref byte forms
        byte[] from = { 1, 2, 3, 4, 5, 6, 7, 8 };
        byte[] to = new byte[8];
        Unsafe.CopyBlock(ref to[0], ref from[0], 5);
        Console.WriteLine(string.Join(",", to));
        Unsafe.CopyBlockUnaligned(ref to[5], ref from[1], 3);
        Console.WriteLine(string.Join(",", to));
        fixed (byte* sp = from, dp = to)
        {
            Unsafe.CopyBlock(dp, sp, 2);
            Unsafe.CopyBlockUnaligned(dp + 2, sp + 7, 1);
            Unsafe.InitBlock(dp + 4, 0xAB, 2);
            Unsafe.InitBlockUnaligned(dp + 6, 0x01, 2);
        }
        Console.WriteLine(string.Join(",", to));
        Unsafe.InitBlock(ref to[0], 0xFF, 3);
        Unsafe.InitBlockUnaligned(ref to[3], 0x10, 2);
        Console.WriteLine(string.Join(",", to));

        // BitCast
        Console.WriteLine($"{Unsafe.BitCast<float, int>(1.0f):X8} {Unsafe.BitCast<int, float>(0x40490FDB)}");
        Console.WriteLine($"{Unsafe.BitCast<double, long>(-2.0):X16} {Unsafe.BitCast<long, ulong>(-1L)}");
        Console.WriteLine(Unsafe.BitCast<Pair, Wide>(new Pair { A = 1, B = 2 }).Value);
        Console.WriteLine(Unsafe.BitCast<byte, sbyte>(200));
        Console.WriteLine(Unsafe.BitCast<byte, bool>(1));
        Console.WriteLine(Unsafe.BitCast<char, ushort>('A'));
        try { Console.WriteLine(Unsafe.BitCast<int, long>(1)); }
        catch (NotSupportedException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

        // Unbox
        object box = 41;
        ref int unboxed = ref Unsafe.Unbox<int>(box);
        unboxed++;
        Console.WriteLine(box);
        object boxedPair = new Pair { A = 1, B = 2 };
        Unsafe.Unbox<Pair>(boxedPair).B = 50;
        Console.WriteLine(((Pair)boxedPair).B);
        try { Unsafe.Unbox<int>("nope"); }
        catch (InvalidCastException) { Console.WriteLine("InvalidCastException"); }

        // Reinterpretation of fields of a class instance
        var holder = new Holder();
        ref int firstRef = ref holder.First;
        ref long both = ref Unsafe.As<int, long>(ref firstRef);
        Console.WriteLine($"{both:X16}");
        both = 0x0000001400000009L;
        Console.WriteLine($"{holder.First} {holder.Second} {holder.Name}");
        Console.WriteLine(Unsafe.ByteOffset(ref holder.First, ref holder.Second));

        // Generic helpers over refs
        Console.WriteLine(Sum<int>(new[] { 1, 2, 3 }));
        Console.WriteLine(Sum<ushort>(stackalloc ushort[] { 0x0101, 0x0202 }));

        // ref locals follow the reference
        int[] data = { 1, 2, 3 };
        ref int cursor = ref data[0];
        for (int i = 0; i < data.Length; i++)
        {
            cursor *= 10;
            cursor = ref Unsafe.Add(ref cursor, 1);
        }
        Console.WriteLine(string.Join(",", data));
    }
}
