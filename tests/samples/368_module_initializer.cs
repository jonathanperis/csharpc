// [ModuleInitializer] methods run before Main, in source order (csharpc used to ignore them silently).
using System;
using System.Runtime.CompilerServices;

Console.WriteLine("main: " + Init.Log);

static class Init
{
    public static string Log = "";

    [ModuleInitializer]
    internal static void First() { Log += "first;"; Console.WriteLine("first"); }

    [ModuleInitializer]
    internal static void Second() { Log += "second;"; Console.WriteLine("second"); }
}

static class Other
{
    [ModuleInitializer]
    internal static void Third() { Init.Log += "third;"; Console.WriteLine("third"); }
}
