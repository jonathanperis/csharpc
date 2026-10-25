// Library members ASP.NET Core reaches at start-up: span encoding and number formatting/parsing,
// MemoryMarshal, Lazy<T>(), OS checks, Enum.ToObject, uninitialized objects, attribute lookups over
// reflection metadata, ConstructorInvoker, boxed native integers, task type tests, special folders,
// SemaphoreSlim.WaitAsync, Wait(TimeSpan) and the tracked console colors of redirected output.
#nullable disable
#pragma warning disable IL2026, IL2067, IL2072, IL2075, IL2080, IL3050
using System;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

enum Level : byte { Low = 1, High = 200 }

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method, AllowMultiple = true)]
sealed class NoteAttribute : Attribute { public NoteAttribute(string text) { Text = text; } public string Text { get; } }

struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } public override string ToString() => $"({X},{Y})"; }

class Widget
{
    public string Name = "unset";
    public Widget() { Name = "made"; }
    public Widget(string name) { Name = name; }
    [Note("m")] public void Run([Note("a")] [Note("b")] int value) { }
}

class Program
{
    static void KeepMembers([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] Type t) { }

    static async Task Main()
    {
        Span<char> chars = stackalloc char[8];
        Console.WriteLine($"{Encoding.UTF8.GetChars("héllo"u8, chars)} {new string(chars.Slice(0, 5))} {Encoding.UTF8.GetCharCount("héllo"u8)} {Encoding.UTF8.GetByteCount("héllo".AsSpan())}");
        try { Encoding.UTF8.GetChars("hello world"u8, chars); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + " " + e.ParamName); }
        Console.WriteLine($"{Encoding.UTF8.TryGetChars("hi"u8, chars, out int written)} {written}");

        Span<char> digits = stackalloc char[16];
        Console.WriteLine($"{12345.TryFormat(digits, out int n, "N0", CultureInfo.InvariantCulture)} {new string(digits.Slice(0, n))}");
        Console.WriteLine($"{(-7L).TryFormat(digits.Slice(0, 1), out n, default, null)} {n} {3.5.TryFormat(digits, out n, "F2", CultureInfo.InvariantCulture)} {new string(digits.Slice(0, n))}");
        Span<byte> utf8 = stackalloc byte[8];
        Console.WriteLine($"{255.TryFormat(utf8, out n, "X", null)} {Encoding.ASCII.GetString(utf8.Slice(0, n))}");

        Console.WriteLine($"{Utf8Parser.TryParse("12345 rest"u8, out long l, out int consumed)} {l} {consumed}");
        Console.WriteLine($"{Utf8Parser.TryParse("-42"u8, out int i, out consumed)} {i} {consumed} {Utf8Parser.TryParse("x1"u8, out i, out consumed)} {consumed}");
        Console.WriteLine($"{Utf8Parser.TryParse("99999999999999999999"u8, out l, out consumed)} {consumed} {Utf8Parser.TryParse("ff"u8, out uint u, out consumed, 'x')} {u} {Utf8Parser.TryParse("+5"u8, out uint u2, out consumed)}");

        ReadOnlySpan<int> ints = new[] { 1, 258 };
        Console.WriteLine(string.Join(",", MemoryMarshal.AsBytes(ints).ToArray()));
        Span<short> shorts = MemoryMarshal.Cast<int, short>(new[] { 0x00020001 }.AsSpan());
        Console.WriteLine($"{shorts.Length} {shorts[0]} {shorts[1]} {MemoryMarshal.Read<int>(new byte[] { 7, 0, 0, 0, 9 })}");
        int local = 5;
        var single = MemoryMarshal.CreateSpan(ref local, 1);
        single[0] = 6;
        Console.WriteLine(local);

        var lazy = new Lazy<Widget>();
        Console.WriteLine($"{lazy.IsValueCreated} {lazy.Value.Name} {lazy.IsValueCreated}");

        Console.WriteLine($"{RuntimeInformation.IsOSPlatform(OSPlatform.Linux) == OperatingSystem.IsLinux()} {OSPlatform.Windows} {OSPlatform.Create("linux") == OSPlatform.Linux}");
        Console.WriteLine(typeof(Program).Assembly.IsCollectible);

        var loadError = new ReflectionTypeLoadException(new Type[] { null }, new Exception[] { new InvalidOperationException("one"), null });
        Console.WriteLine(loadError.Message.Replace(Environment.NewLine, "|") + " " + loadError.LoaderExceptions.Length);

        Console.WriteLine($"{Enum.ToObject(typeof(Level), 200)} {Enum.ToObject(typeof(Level), (object)1L)} {Enum.ToObject(typeof(Level), 456)} {Enum.ToObject(typeof(Level), 1).GetType()}");
        try { Enum.ToObject(typeof(Level), "x"); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        Console.WriteLine($"{RuntimeHelpers.GetUninitializedObject(typeof(Point))} {((Widget)RuntimeHelpers.GetUninitializedObject(typeof(Widget))).Name ?? "null"}");

        KeepMembers(typeof(Widget));
        var run = typeof(Widget).GetMethod("Run");
        var notes = (NoteAttribute[])Attribute.GetCustomAttributes(run.GetParameters()[0], typeof(NoteAttribute));
        Console.WriteLine($"{notes.Length} {notes[0].Text}{notes[1].Text} {run.GetCustomAttribute<NoteAttribute>().Text} {run.GetParameters()[0].IsDefined(typeof(NoteAttribute))}");
        try { run.GetParameters()[0].GetCustomAttribute<NoteAttribute>(); } catch (AmbiguousMatchException e) { Console.WriteLine(e.Message); }
        var invoker = ConstructorInvoker.Create(typeof(Widget).GetConstructor(new[] { typeof(string) }));
        Console.WriteLine(((Widget)invoker.Invoke("fast")).Name);

        object boxed = (nint)12345, other = (nuint)7;
        Console.WriteLine($"{boxed} {boxed is nint} {boxed.Equals((nint)12345)} {other} {boxed.GetType()}");

        object task = Task.CompletedTask, notTask = "x";
        Console.WriteLine($"{task is Task} {notTask is Task}");

        Console.WriteLine($"{"key=value".AsSpan().ContainsAny('=', ';', ',')} {"plain".AsSpan().ContainsAny('=', ';', ',')}");
        // (Unix folders and console behaviour: Windows has its own.)
        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            Console.WriteLine($"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) == home} {Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}");
        }

        var semaphore = new SemaphoreSlim(1);
        await semaphore.WaitAsync();
        var second = semaphore.WaitAsync();
        Console.WriteLine($"{semaphore.CurrentCount} {second.IsCompleted}");
        semaphore.Release();
        await second;
        Console.WriteLine($"{semaphore.CurrentCount} {await semaphore.WaitAsync(0)}");
        var gate = new ManualResetEventSlim(false);
        Console.WriteLine($"{gate.Wait(TimeSpan.FromMilliseconds(1))} {new ManualResetEventSlim(true).Wait(TimeSpan.Zero)}");
        try { gate.Wait(TimeSpan.FromMilliseconds(-2)); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"{(int)Console.ForegroundColor} {(int)Console.BackgroundColor}");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(Console.ForegroundColor);
            Console.ResetColor();
            Console.WriteLine((int)Console.ForegroundColor);
            try { Console.BackgroundColor = (ConsoleColor)99; } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        }
    }
}
