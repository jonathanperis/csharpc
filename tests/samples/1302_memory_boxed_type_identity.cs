// A boxed Memory<T> / ReadOnlyMemory<T> is an instance of typeof(Memory<T>): GetType(), Type equality and IsInstanceOfType agree (reflection-based
// serializers box constructor arguments of these types and check them against the parameter type).
using System;

static class Program
{
    static void Main()
    {
        object m = new Memory<byte>(new byte[2]);
        object r = new ReadOnlyMemory<float>(new float[3]);
        Console.WriteLine(m.GetType() == typeof(Memory<byte>));
        Console.WriteLine(r.GetType() == typeof(ReadOnlyMemory<float>));
        Console.WriteLine(m.GetType() == typeof(ReadOnlyMemory<byte>));
        Console.WriteLine(m.GetType().IsValueType + " " + typeof(Memory<byte>).IsGenericType + " " + typeof(ReadOnlyMemory<float>).GetGenericTypeDefinition().Name);
        Console.WriteLine(typeof(ReadOnlyMemory<float>).IsInstanceOfType(r));
        Console.WriteLine(typeof(Memory<byte>).IsInstanceOfType(r));
        Console.WriteLine(typeof(Memory<byte>).IsInstanceOfType(m));
        Console.WriteLine(((Memory<byte>)m).Length + " " + ((ReadOnlyMemory<float>)r).Length);
    }
}
