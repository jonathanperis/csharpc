// Type.GetTypeCode(typeof(T)) in generic code, which the IL front-end folds to a constant for each instantiation (#399:
// ConcurrentDictionary's IsWriteAtomic switches on it; as a run-time call it pulled in the reflection tables). Each code must be
// .NET's: an enum's is its underlying type's, Nullable<T> and other structs are Object, DBNull and DateTime their own.
using System;

static class Program
{
    enum Small : byte { A }
    enum Big : long { A }
    struct Pair { public int X; }

    static TypeCode Code<T>() => Type.GetTypeCode(typeof(T));

    // The shape ConcurrentDictionary uses: a switch on the code of a type parameter.
    static bool AtomicWrite<T>()
    {
        if (!typeof(T).IsValueType) return true;
        switch (Type.GetTypeCode(typeof(T)))
        {
            case TypeCode.Boolean: case TypeCode.Byte: case TypeCode.Char: case TypeCode.Int16: case TypeCode.Int32:
            case TypeCode.SByte: case TypeCode.Single: case TypeCode.UInt16: case TypeCode.UInt32:
                return true;
            case TypeCode.Double: case TypeCode.Int64: case TypeCode.UInt64:
                return true; // IntPtr.Size == 8 in .NET; printed as a constant so one recording holds on 32-bit targets
            default:
                return false;
        }
    }

    static void Show<T>(string name) => Console.WriteLine($"{name}: {Code<T>()} {AtomicWrite<T>()}");

    static void Main()
    {
        Show<bool>("bool");
        Show<char>("char");
        Show<sbyte>("sbyte");
        Show<byte>("byte");
        Show<short>("short");
        Show<ushort>("ushort");
        Show<int>("int");
        Show<uint>("uint");
        Show<long>("long");
        Show<ulong>("ulong");
        Show<float>("float");
        Show<double>("double");
        Show<decimal>("decimal");
        Show<DateTime>("DateTime");
        Show<string>("string");
        Show<DBNull>("DBNull");
        Show<object>("object");
        Show<Small>("Small");
        Show<Big>("Big");
        Show<DayOfWeek>("DayOfWeek");
        Show<Pair>("Pair");
        Show<int?>("int?");
        Show<Guid>("Guid");
        Show<nint>("nint");
        Show<int[]>("int[]");
        Console.WriteLine(Type.GetTypeCode(null));
        Type runtime = typeof(Big);
        Console.WriteLine(Type.GetTypeCode(runtime));
    }
}
