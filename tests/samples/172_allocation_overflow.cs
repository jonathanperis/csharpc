// Size computations must never wrap into an undersized allocation: .NET throws instead.
int minus = -1;
long huge = 1L << 40;
int max = int.MaxValue;

Try("new int[-1]", () => new int[minus].Length);
Try("new int[1L<<40]", () => new int[huge].Length);
Try("new long[int.MaxValue]", () => new long[max].Length);
Try("new byte[int.MaxValue]", () => new byte[max].Length);
Try("new int[-1, 2]", () => new int[minus, 2].Length);
Try("new int[65536, 65536]", () => new int[65536, 65536].Length);
Try("new string('x', -1)", () => new string('x', minus).Length);
Try("new string('x', int.MaxValue)", () => new string('x', max).Length);
Try("PadLeft(int.MaxValue)", () => "ab".PadLeft(max).Length);
Try("Array.Resize(-1)", () => { var r = new int[1]; Array.Resize(ref r, minus); return r.Length; });
Try("new List<int>(-1)", () => new List<int>(minus).Count);
Try("StringBuilder(-1)", () => new System.Text.StringBuilder(minus).Length);
Try("Append('x', -1)", () => new System.Text.StringBuilder().Append('x', minus).Length);
Console.WriteLine("survived");

static void Try(string what, Func<int> f)
{
    try { Console.WriteLine($"{what} = {f()}"); }
    catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message}"); }
}
