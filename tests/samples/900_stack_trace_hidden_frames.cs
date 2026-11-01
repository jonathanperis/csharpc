// Frames .NET leaves out of a stack trace (System.Diagnostics.StackTrace.ShowInStackTrace): methods
// marked [StackTraceHidden], every method of a type marked so (not of its nested or compiler-generated
// types: lambdas and async state machines stay), and [MethodImpl(AggressiveInlining)] methods. The
// trace's last frame, the one that caught the exception, is listed even when it is hidden.
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        Show("hidden method", () => Guard.Check(-1));
        Show("hidden type", () => Validation.Require(false, "flag"));
        Show("aggressive inlining", () => Inlined(3));
        Show("lambda in a hidden type", () => Validation.ViaLambda());
        Show("local function in a hidden type", () => Validation.ViaLocal());
        Show("nested type of a hidden type", () => Validation.Nested.Fail());
        Show("hidden catching frame", Validation.CatchHere);
        try { await Validation.HiddenAsync(); } catch (Exception e) { Report("hidden async method", e); }
    }

    static void Thrower(string what) => throw new InvalidOperationException(what);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Inlined(int x) => x > 2 ? Twice(x) : x;

    static int Twice(int x)
    {
        Thrower("inlined " + x);
        return 2 * x;
    }

    static class Guard
    {
        [StackTraceHidden]
        public static void Check(int value)
        {
            if (value < 0) Thrower("negative " + value);
        }
    }

    [StackTraceHidden]
    static class Validation
    {
        public static void Require(bool condition, string name)
        {
            if (!condition) Fail(name);
        }

        static void Fail(string name) => Thrower("required " + name);

        public static void ViaLambda()
        {
            Action a = () => Thrower("lambda");
            a();
        }

        public static void ViaLocal()
        {
            Local(1);
            static void Local(int n) => Thrower("local " + n);
        }

        public static void CatchHere()
        {
            try { Require(false, "caught"); }
            catch (Exception e) { Report("caught in a hidden method", e); }
        }

        public static async Task HiddenAsync()
        {
            await Task.Yield();
            Thrower("async");
        }

        public static class Nested
        {
            public static void Fail() => Thrower("nested");
        }
    }

    static void Show(string name, Action action)
    {
        try { action(); }
        catch (Exception e) { Report(name, e); }
    }

    static void Report(string name, Exception e)
    {
        Console.WriteLine("== " + name + ": " + e.Message);
        Console.Write(Clean(e.StackTrace));
    }

    /// <summary>"   at M() in /some/dir/file.cs:line 3" -> "   at M() in file.cs:line 3", one line each.</summary>
    static string Clean(string? text)
    {
        if (text == null) return "<null>" + Environment.NewLine;
        var sb = new StringBuilder();
        foreach (var line in text.Split(Environment.NewLine))
        {
            var at = line.IndexOf(" in ", StringComparison.Ordinal);
            var cut = Math.Max(line.LastIndexOf('/'), line.LastIndexOf('\\'));
            sb.AppendLine(at >= 0 && cut > at ? line.Substring(0, at + 4) + line.Substring(cut + 1) : line);
        }
        return sb.ToString();
    }
}
