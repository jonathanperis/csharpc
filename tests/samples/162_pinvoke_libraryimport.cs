#:property AllowUnsafeBlocks=true
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

Console.WriteLine(N.wcslen("wide chars"));
Console.WriteLine(N.isalpha('x') + " " + N.isalpha('1'));
int[] values = [9, 2, 7];
Console.WriteLine(N.sum_first(values, 2));
Console.WriteLine(N.access("/definitely/missing", 0) + " " + Marshal.GetLastPInvokeError());
N.Ldiv(100, 7, out var q);
Console.WriteLine(q);
Span<byte> buf = stackalloc byte[32];
N.strncpy(buf, "v=42", (nuint)buf.Length); int n = (int)N.strlen(buf);
Console.WriteLine(n + " " + (char)buf[0] + (char)buf[1] + (char)buf[2] + (char)buf[3]);
Console.WriteLine(N.getenv_ptr("CSHARPC_DEFINITELY_UNSET") == IntPtr.Zero);

static unsafe partial class N
{
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf16)] public static partial nuint wcslen_bad(string s); // (UTF-16 != wchar_t, unused)
    public static nuint wcslen(string s) => (nuint)s.Length;
    [LibraryImport("libc", EntryPoint = "isalpha")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool isalpha_raw(int c);
    public static bool isalpha(char c) => isalpha_raw(c);
    [LibraryImport("libc", EntryPoint = "access", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int access(string path, int mode);
    public static int sum_first(int[] a, int count) { int s = 0; fixed (int* p = a) for (int i = 0; i < count; i++) s += p[i]; return s; }
    [LibraryImport("libc", EntryPoint = "lldiv")] private static partial LlDiv lldiv(long a, long b);
    public static void Ldiv(long a, long b, out long quot) { quot = lldiv(a, b).quot; }
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)] public static partial IntPtr strncpy(Span<byte> dest, string src, nuint n);
    [LibraryImport("libc")] public static partial nuint strlen(ReadOnlySpan<byte> s);
    [LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr getenv_ptr(string name);
}
struct LlDiv { public long quot; public long rem; }
