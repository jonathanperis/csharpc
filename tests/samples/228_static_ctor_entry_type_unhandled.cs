// The entry point's type initializes before Main runs (Main is one of its static methods), a
// nested type's static constructor doesn't run with its outer type's, and an unhandled
// TypeInitializationException terminates the process like any unhandled exception.
using System;

class Program
{
    static readonly string Greeting = Log("Program field init");

    static Program() => Console.WriteLine("Program cctor");

    static void Main()
    {
        Console.WriteLine("Main: " + Greeting);
        Console.WriteLine(Outer.Inner.Value);
        Console.WriteLine(Outer.Value);
        Console.WriteLine(Broken.Value);
        Console.WriteLine("not reached");
    }

    internal static string Log(string text) { Console.WriteLine(text); return text; }
}

class Outer
{
    public static int Value = 1;
    static Outer() => Console.WriteLine("Outer cctor");

    public class Inner
    {
        public static int Value = 2;
        static Inner() => Console.WriteLine("Inner cctor");
    }
}

class Broken
{
    public static int Value = int.Parse(Program.Log("x"));
}
