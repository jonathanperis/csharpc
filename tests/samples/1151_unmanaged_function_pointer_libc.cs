#:property AllowUnsafeBlocks=true
using System;
using System.Runtime.InteropServices;

// `delegate* unmanaged` calls follow the C ABI like a P/Invoke of the same function (sample 176): small structs
// come back in registers, complex numbers are float/double structs, a bool is a 4-byte BOOL, small integers are
// extended by the caller. The pointers are dlsym'd from libc and libm, so only a C-ABI-correct call prints these.
unsafe
{
    var libc = NativeLibrary.Load("libc.so.6");
    var libm = NativeLibrary.Load("libm.so.6");

    var d = ((delegate* unmanaged<int, int, DivT>)NativeLibrary.GetExport(libc, "div"))(17, 5);
    Console.WriteLine($"div {d.Quot} {d.Rem}");
    var l = ((delegate* unmanaged[Cdecl]<long, long, LDivT>)NativeLibrary.GetExport(libc, "ldiv"))(-100, 7);
    Console.WriteLine($"ldiv {l.Quot} {l.Rem}");
    var ll = ((delegate* unmanaged<long, long, LDivT>)NativeLibrary.GetExport(libc, "lldiv"))(1L << 40, 1000);
    Console.WriteLine($"lldiv {ll.Quot} {ll.Rem}");

    Console.WriteLine($"cabs {((delegate* unmanaged<Complex, double>)NativeLibrary.GetExport(libm, "cabs"))(new Complex { Re = 3, Im = 4 })}");
    Console.WriteLine($"cabsf {((delegate* unmanaged<ComplexF, float>)NativeLibrary.GetExport(libm, "cabsf"))(new ComplexF { Re = 5, Im = 12 })}");
    var c = ((delegate* unmanaged<Complex, Complex>)NativeLibrary.GetExport(libm, "conj"))(new Complex { Re = 1.5, Im = -2.25 });
    Console.WriteLine($"conj {c.Re} {c.Im}");
    var cf = ((delegate* unmanaged<ComplexF, ComplexF>)NativeLibrary.GetExport(libm, "conjf"))(new ComplexF { Re = -0.5f, Im = 8f });
    Console.WriteLine($"conjf {cf.Re} {cf.Im}");
    var root = ((delegate* unmanaged<Complex, Complex>)NativeLibrary.GetExport(libm, "csqrt"))(new Complex { Re = -4, Im = 0 });
    Console.WriteLine($"csqrt {root.Re} {root.Im}");

    // bool results are 4-byte BOOLs: isalpha returns 1024 (low byte zero) for a letter in glibc
    var isalpha = NativeLibrary.GetExport(libc, "isalpha");
    Console.WriteLine($"isalpha {((delegate* unmanaged<int, bool>)isalpha)('x')} {((delegate* unmanaged<int, bool>)isalpha)('7')}");

    // sub-int arguments are extended to 32 bits by the caller; the native function reads all of them
    var abs = NativeLibrary.GetExport(libc, "abs");
    Console.WriteLine($"abs {((delegate* unmanaged<sbyte, int>)abs)(-3)} {((delegate* unmanaged<byte, int>)abs)(200)} {((delegate* unmanaged<short, int>)abs)(-300)} {((delegate* unmanaged<ushort, int>)abs)(60000)} {((delegate* unmanaged<bool, int>)abs)(true)}");
    var toUpper = NativeLibrary.GetExport(libc, "toupper");
    Console.WriteLine($"toupper {(char)((delegate* unmanaged<int, int>)toUpper)('q')}");
}

struct DivT { public int Quot; public int Rem; }
struct LDivT { public long Quot; public long Rem; }
struct Complex { public double Re; public double Im; }
struct ComplexF { public float Re; public float Im; }
