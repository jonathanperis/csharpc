// Exceptions through runtime frames (ToString), throwing from handlers, finally in catch, static init.
var b = new Bad();
try { Console.WriteLine("value: " + b); } catch (InvalidOperationException e) { Console.WriteLine("through runtime: " + e.Message); }
try
{
    try { throw new FormatException("first"); }
    catch (FormatException)
    {
        try { Console.WriteLine("handler try"); }
        finally { Console.WriteLine("finally inside catch"); }
        throw new ArgumentException("second");
    }
    finally { Console.WriteLine("outer finally"); }
}
catch (ArgumentException e) { Console.WriteLine("got " + e.Message); }
Console.WriteLine(Holder.Value);
Console.WriteLine(Nested(3));
static int Nested(int n)
{
    try { if (n == 0) throw new Exception("bottom"); return Nested(n - 1) + 1; }
    catch (Exception e) when (n == 2) { Console.WriteLine("n=2 caught " + e.Message); return 100; }
    finally { Console.WriteLine("unwind " + n); }
}
class Bad { public override string ToString() => throw new InvalidOperationException("ToString failed"); }
static class Holder
{
    public static int Value = Compute();
    static int Compute() { try { throw new Exception("x"); } catch { return 7; } }
}
