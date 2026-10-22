namespace Demo;

class Program
{
    static void Main()
    {
        System.Console.WriteLine("Demo.Program.Main");
        Other.Main();
    }
}

class Other
{
    public static void Main() => System.Console.WriteLine("Demo.Other.Main (called, not the entry point)");
}

static class Third
{
    static int Main(string[] args) => args.Length;
}
