#:property AllowUnsafeBlocks=true
using System.Runtime.InteropServices;

// Marshal.GetFunctionPointerForDelegate hands out a native-callable code pointer for a delegate of a
// blittable signature (csharpc generates reverse P/Invoke thunks per delegate type, there is no run-time
// code generation), and GetDelegateForFunctionPointer wraps a native pointer in a delegate. Both are
// exercised through `delegate* unmanaged` calls, which every platform has: argument kinds (int, small
// ints, double, pointers, by-ref, native-int, 4-byte bool), closures, pointer identity, the pointers of
// [UnmanagedCallersOnly] methods, delegates that go away (the thunks are reused), and the exceptions.
unsafe
{
    var add = new BinaryOp((a, b) => a + b);
    var addPointer = Marshal.GetFunctionPointerForDelegate(add);
    var call = (delegate* unmanaged<int, int, int>)addPointer;
    Console.WriteLine($"add {call(3, 4)} {call(-10, 3)}");
    Console.WriteLine($"same delegate, same pointer: {Marshal.GetFunctionPointerForDelegate(add) == addPointer}");
    var other = new BinaryOp((a, b) => a * b);
    var otherPointer = Marshal.GetFunctionPointerForDelegate(other);
    Console.WriteLine($"other delegate, other pointer: {otherPointer != addPointer} {((delegate* unmanaged<int, int, int>)otherPointer)(6, 7)}");

    // A closure keeps its state between native calls.
    int calls = 0;
    var counter = new BinaryOp((a, b) => { calls++; return a + b + calls; });
    var counterCall = (delegate* unmanaged<int, int, int>)Marshal.GetFunctionPointerForDelegate(counter);
    Console.WriteLine($"closure {counterCall(1, 1)} {counterCall(1, 1)} {counterCall(1, 1)} calls={calls}");

    // Pointers and by-ref arguments.
    var visit = new Visit((values, count) =>
    {
        int sum = 0;
        for (int i = 0; i < count; i++) sum += values[i];
        Console.WriteLine($"visit {count} values, sum {sum}");
    });
    var visitCall = (delegate* unmanaged<int*, int, void>)Marshal.GetFunctionPointerForDelegate(visit);
    int[] data = [1, 2, 3, 4, 5];
    fixed (int* p = data) visitCall(p, data.Length);
    var bump = new Bump((ref int value) => value += 10);
    var bumpCall = (delegate* unmanaged<int*, void>)Marshal.GetFunctionPointerForDelegate(bump);
    int bumped = 5;
    bumpCall(&bumped);
    Console.WriteLine($"bump {bumped}");

    // Small integers, floating point, native ints, mixed widths.
    var scale = new Scale((x, factor, offset) => x * factor + offset);
    var scaleCall = (delegate* unmanaged<double, byte, short, double>)Marshal.GetFunctionPointerForDelegate(scale);
    Console.WriteLine($"scale {scaleCall(2.5, 3, -4)} {scaleCall(0.5, 255, 1000)}");
    var wide = new Wide((a, b, c, d) => a + (long)b + c + d);
    var wideCall = (delegate* unmanaged<long, ulong, sbyte, ushort, long>)Marshal.GetFunctionPointerForDelegate(wide);
    Console.WriteLine($"wide {wideCall(-5, 10, -3, 65535)}");
    var small = new Small(x => (byte)(x + 1));
    var smallCall = (delegate* unmanaged<byte, byte>)Marshal.GetFunctionPointerForDelegate(small);
    Console.WriteLine($"small {smallCall(41)} {smallCall(255)}");
    var identity = new Identity(value => value);
    var identityCall = (delegate* unmanaged<nint, nint>)Marshal.GetFunctionPointerForDelegate(identity);
    Console.WriteLine($"native int {identityCall(-1)} {identityCall(1234567)}");

    // bool crosses as a 4-byte BOOL by default.
    var even = new Check(value => value % 2 == 0);
    var evenCall = (delegate* unmanaged<int, int>)Marshal.GetFunctionPointerForDelegate(even);
    Console.WriteLine($"bool {evenCall(4)} {evenCall(7)}");
    var negate = new Negate(value => !value);
    var negateCall = (delegate* unmanaged<int, int>)Marshal.GetFunctionPointerForDelegate(negate);
    Console.WriteLine($"bool param {negateCall(0)} {negateCall(1)} {negateCall(2)}");

    // The pointer of an [UnmanagedCallersOnly] method, as a delegate and called through the delegate.
    var twice = Marshal.GetDelegateForFunctionPointer<Unary>((IntPtr)(delegate* unmanaged<int, int>)&Twice);
    Console.WriteLine($"native pointer as delegate {twice(21)} {twice(-4)}");
    // A pointer from GetFunctionPointerForDelegate, wrapped again: calls reach the original delegate.
    var wrapped = Marshal.GetDelegateForFunctionPointer<BinaryOp>(addPointer);
    Console.WriteLine($"wrapped {wrapped(100, 23)}");
    try { Marshal.GetDelegateForFunctionPointer<Unary>(IntPtr.Zero); }
    catch (ArgumentNullException e) { Console.WriteLine($"zero pointer: {e.ParamName}"); }

    // Delegates that go away give their thunks back: far more than the pool holds, one after the other.
    long total = 0;
    for (int i = 0; i < 200; i++)
    {
        int captured = i;
        var temporary = new Unary(x => x + captured);
        var pointer = (delegate* unmanaged<int, int>)Marshal.GetFunctionPointerForDelegate(temporary);
        total += pointer(1);
        GC.KeepAlive(temporary);
    }
    Console.WriteLine($"200 short-lived delegates: {total}");

    try { Marshal.GetFunctionPointerForDelegate<BinaryOp>(null!); }
    catch (ArgumentNullException e) { Console.WriteLine($"null: {e.ParamName}"); }
    try { Marshal.GetFunctionPointerForDelegate<Func<int, int>>(x => x); }
    catch (ArgumentException e) { Console.WriteLine($"generic delegate: {e.GetType().Name}"); }
}

[UnmanagedCallersOnly]
static int Twice(int value) => value * 2;

delegate int BinaryOp(int a, int b);
delegate int Unary(int a);
unsafe delegate void Visit(int* values, int count);
delegate void Bump(ref int value);
delegate double Scale(double x, byte factor, short offset);
delegate long Wide(long a, ulong b, sbyte c, ushort d);
delegate byte Small(byte x);
delegate nint Identity(nint value);
delegate bool Check(int value);
delegate bool Negate(bool value);
