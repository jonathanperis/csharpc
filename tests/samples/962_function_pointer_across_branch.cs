#:property AllowUnsafeBlocks=true
// A function pointer argument followed by a method-group delegate argument: csc pushes the ldftn result,
// then emits the cached-delegate check (ldsfld; dup; brtrue) before the call, so the function pointer is
// live on the IL stack across a branch. Also static lambdas and several pointers in one call.
using System;

unsafe
{
    Register(&Native, Callback);
    Register(&Native, Callback);
    Register2(&Native, &Other, Callback, x => x - 1);
    Register(&Native, static x => x + 100);
}

static unsafe void Register(delegate*<int, int> fn, Func<int, int> callback) =>
    Console.WriteLine($"{fn(1)} {callback(2)}");

static unsafe void Register2(delegate*<int, int> a, delegate*<int, int> b, Func<int, int> c, Func<int, int> d) =>
    Console.WriteLine($"{a(1)} {b(1)} {c(2)} {d(2)}");

static int Native(int x) => x + 10;
static int Other(int x) => x * 3;
static int Callback(int x) => x * 10;
