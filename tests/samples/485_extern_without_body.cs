// A user extern method with no implementation (no [DllImport]): .NET compiles the program (CS0626) and
// throws BadImageFormatException only when the method is called; csharpc used to emit invalid IR.
using System;
class P {
    static extern int Log(string m);
    static void Main() {
        Console.WriteLine("start");
        try { Console.WriteLine(Log("x")); } catch (Exception e) { Console.WriteLine(e.GetType().FullName + ": " + e.Message); }
    }
}
