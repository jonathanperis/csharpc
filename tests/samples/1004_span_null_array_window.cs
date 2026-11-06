// A null array with an empty window is the empty span (new Span<T>(null, 0, 0), AsSpan(null, 0), the same for
// ReadOnlySpan, Memory and ArraySegment-free callers such as System.Net.Http's header store); any other window over
// a null array is an ArgumentOutOfRangeException, and the array-less constructors keep their meaning.
byte[]? none = null;

var span = new Span<byte>(none, 0, 0);
Console.WriteLine("Span(null, 0, 0): length " + span.Length + ", empty " + span.IsEmpty);
var readOnly = new ReadOnlySpan<byte>(none, 0, 0);
Console.WriteLine("ReadOnlySpan(null, 0, 0): length " + readOnly.Length);
Console.WriteLine("Span(null): length " + new Span<byte>(none).Length);
Console.WriteLine("AsSpan(null, 0): " + none.AsSpan(0).Length);
Console.WriteLine("AsSpan(null, 0, 0): " + none.AsSpan(0, 0).Length);
Console.WriteLine("AsMemory(null, 0, 0): " + none.AsMemory(0, 0).Length);
Console.WriteLine("Memory(null, 0, 0): " + new Memory<byte>(none, 0, 0).Length);

void Report(string label, Action action)
{
    try { action(); Console.WriteLine(label + ": no exception"); }
    catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message); }
}

Report("Span(null, 1, 0)", () => _ = new Span<byte>(none, 1, 0).Length);
Report("Span(null, 0, 1)", () => _ = new Span<byte>(none, 0, 1).Length);
Report("ReadOnlySpan(null, 0, 2)", () => _ = new ReadOnlySpan<byte>(none, 0, 2).Length);
Report("AsSpan(null, 1)", () => _ = none.AsSpan(1).Length);
Report("AsSpan(null, 0, 3)", () => _ = none.AsSpan(0, 3).Length);
Report("Memory(null, 2, 0)", () => _ = new Memory<byte>(none, 2, 0).Length);

// A real array keeps its window checks.
var data = new byte[] { 1, 2, 3, 4 };
Console.WriteLine(new Span<byte>(data, 1, 2).Length + " " + data.AsSpan(2).Length + " " + data.AsSpan(1, 3).Length);
Report("Span(data, 3, 2)", () => _ = new Span<byte>(data, 3, 2).Length);
Report("AsSpan(data, 5)", () => _ = data.AsSpan(5).Length);
