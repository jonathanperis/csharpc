// Task.FromResult hands out .NET's shared tasks (TaskCache): one each for true and false, for the Int32 values -1 to 8, and for the
// default value of a reference type or of a value type without references of 1, 2, 4, 8 or 16 bytes (compared bit for bit: -0.0 is
// not default). Every other result gets a new task. Code that compares tasks by reference sees the same answers as under .NET.
using System;
using System.Threading.Tasks;

static class Program
{
    static string Same<T>(T a, T b) => ReferenceEquals(Task.FromResult(a), Task.FromResult(b)) ? "shared" : "new";

    struct Pair { public int A, B; }
    struct WithRef { public string S; }
    struct Three { public byte A, B, C; }

    static void Main()
    {
        Console.WriteLine($"bool: true {Same(true, true)}, false {Same(false, false)}, true/false {Same(true, false)}");
        Console.WriteLine($"int: -2 {Same(-2, -2)}, -1 {Same(-1, -1)}, 0 {Same(0, 0)}, 8 {Same(8, 8)}, 9 {Same(9, 9)}, 1000 {Same(1000, 1000)}");
        Console.WriteLine($"long: 0 {Same(0L, 0L)}, 1 {Same(1L, 1L)}; byte 0 {Same((byte)0, (byte)0)}; char 0 {Same('\0', '\0')}; short 0 {Same((short)0, (short)0)}");
        Console.WriteLine($"double: 0.0 {Same(0.0, 0.0)}, -0.0 {Same(-0.0, -0.0)}, 1.5 {Same(1.5, 1.5)}; decimal 0 {Same(0m, 0m)}");
        Console.WriteLine($"Int128 0 {Same(Int128.Zero, Int128.Zero)}, Guid.Empty {Same(Guid.Empty, Guid.Empty)}, DateTime default {Same(default(DateTime), default(DateTime))}");
        Console.WriteLine($"string: null {Same<string>(null, null)}, \"\" {Same("", "")}; object null {Same<object>(null, null)}");
        Console.WriteLine($"int?: null {Same<int?>(null, null)}, 0 {Same<int?>(0, 0)}; long?: null {Same<long?>(null, null)}");
        Console.WriteLine($"struct Pair default {Same(default(Pair), default(Pair))}, Pair(1,0) {Same(new Pair { A = 1 }, new Pair { A = 1 })}");
        Console.WriteLine($"struct WithRef default {Same(default(WithRef), default(WithRef))}, three bytes default {Same(default(Three), default(Three))}");
        var t = Task.FromResult(true);
        Console.WriteLine($"shared task: {t.Status}, {t.Result}, id>0 {t.Id > 0}, options {t.CreationOptions}, state {t.AsyncState ?? "null"}");
    }
}
