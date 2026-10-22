// Span/ReadOnlySpan/Memory views must stay inside their backing storage.
int[] data = [10, 20, 30, 40];
int minus = -1, five = 5, two = 2, four = 4;

Try("span[4]", () => new Span<int>(data)[four]);
Try("span[-1]", () => new Span<int>(data)[minus]);
Try("new Span(data, 2, 5)", () => new Span<int>(data, two, five).Length);
Try("new Span(data, 5, 0)", () => new Span<int>(data, five, 0).Length);
Try("Slice(3, 2)", () => data.AsSpan().Slice(3, two).Length);
Try("Slice(-1)", () => data.AsSpan().Slice(minus).Length);
Try("AsSpan(5)", () => data.AsSpan(five).Length);
Try("stackalloc span[3]", () => { Span<byte> b = stackalloc byte[3]; return b[3 + minus + 1]; });
Try("ROS<char>[3]", () => "abc".AsSpan()[3]);
Try("ROS<char> Slice(2,2)", () => "abc".AsSpan().Slice(2, 2).Length);
Try("CopyTo shorter", () => { data.AsSpan().CopyTo(new int[2]); return 0; });
Try("Memory.Slice(1,9)", () => data.AsMemory().Slice(1, 9).Length);
Try("span[^0]", () => data.AsSpan()[^0]);
Try("span[1..9]", () => data.AsSpan()[1..9].Length);
Console.WriteLine(string.Join(",", data));

static void Try(string what, Func<int> f)
{
    try { Console.WriteLine($"{what} = {f()}"); }
    catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message}"); }
}
