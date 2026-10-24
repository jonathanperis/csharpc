#:property AllowUnsafeBlocks=true
// Fixed-size buffers in C# source: indexing, fixed statements, pointers, copies of the containing struct.
using System;

unsafe struct Buffer
{
    public fixed char fixedBuffer[128];
    public int Length;
    public fixed byte Bytes[4];
}

unsafe class Example
{
    public Buffer buffer;
}

unsafe class Program
{
    static int Add(int a, int b) => a + b;
    static string Join(string a, string b) => a + b;
    static double Mul(double a, double b) => a * b;
    // A function pointer whose signature uses the method's type parameter.
    static T Combine<T>(delegate*<T, T, T> combinator, T left, T right) => combinator(left, right);

    static void Main()
    {
        var example = new Example();
        fixed (char* charPtr = example.buffer.fixedBuffer)
        {
            *charPtr = 'A';
            charPtr[1] = 'Z';
        }
        char c = example.buffer.fixedBuffer[0];
        Console.WriteLine(c);
        example.buffer.fixedBuffer[0] = 'B';
        Console.WriteLine(example.buffer.fixedBuffer[0]);
        Console.WriteLine(example.buffer.fixedBuffer[1]);
        Console.WriteLine(sizeof(Buffer));

        Buffer local = default;
        for (int i = 0; i < 4; i++) local.Bytes[i] = (byte)(i * 3);
        local.Length = 7;
        Buffer copy = local;
        copy.Bytes[0] = 99;
        Console.WriteLine($"{local.Bytes[0]} {copy.Bytes[0]} {copy.Bytes[3]} {copy.Length}");
        byte* p = local.Bytes;
        Console.WriteLine(p[2] + p[3]);
        Span<char> span;
        fixed (char* start = example.buffer.fixedBuffer) span = new Span<char>(start, 3);
        Console.WriteLine(new string(span.Slice(0, 2)));
        Console.WriteLine(Combine(&Add, 20, 22) + " " + Combine(&Join, "a", "b") + " " + Combine<double>(&Mul, 1.5, 4));
    }
}
