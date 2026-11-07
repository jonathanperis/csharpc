#:property LangVersion=7.3
#:property Nullable=disable
#:property ImplicitUsings=disable
using System;

// A project on C# 7.3 (or 6) must compile: csharpc's built-in [GeneratedRegex] generator emitted a file with
// "#nullable enable annotations" even for a program without a [GeneratedRegex] member, which is an error
// before C# 8 (CS8370 / CS8059). The compat corpus' ref-readonly-struct and csharp6-* samples hit this.
// The generator now emits nothing without members, and no #nullable directive below C# 8.
namespace LangVersionSeven
{
    internal readonly struct Point
    {
        public Point(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
        public override string ToString() => $"({X}, {Y})";
    }

    internal static class Program
    {
        private static int Sum(in Point p) => p.X + p.Y;

        private static void Main()
        {
            var p = new Point(3, 4);
            Console.WriteLine(p);
            Console.WriteLine(Sum(in p));
            int number = 0b0001_0000;
            Console.WriteLine(number);
        }
    }
}
