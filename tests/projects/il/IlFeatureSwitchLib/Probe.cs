using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace IlFeatureSwitchLib;

public static class Probe
{
    public static string Answer()
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            var method = new DynamicMethod("Answer", typeof(int), Type.EmptyTypes);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, 42);
            il.Emit(OpCodes.Ret);
            return "emitted " + method.CreateDelegate<Func<int>>()();
        }
        return "interpreted " + (40 + 2);
    }

    public static string Engine() => RuntimeFeature.IsDynamicCodeCompiled ? Emit() : "reflection engine";

    private static string Emit()
    {
        var method = new DynamicMethod("Engine", typeof(string), Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "dynamic engine");
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<string>>()();
    }

    public static int Loop(int n)
    {
        var total = 0;
        for (var i = 0; i < n; i++)
            if (!RuntimeFeature.IsDynamicCodeSupported) total += i;
        return total;
    }
}

// Span<T>.op_Implicit(ArraySegment<T>) as a call in IL (csharpc's IL front-end crashed on it).
public static class Segments
{
    public static string Describe(ArraySegment<byte> segment)
    {
        ReadOnlySpan<byte> read = segment;
        Span<byte> write = segment;
        if (write.Length > 0) write[0] = 99;
        var sum = 0;
        foreach (var b in read) sum += b;
        return $"{read.Length} {sum}";
    }
}
