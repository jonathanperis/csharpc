// Array.Copy argument validation: exception types, messages and ParamName must match .NET.
using System;

int[] src = { 1, 2, 3 }, dst = new int[3];
int[] nul = null!;

Try("negative length", () => Array.Copy(src, dst, -1));
Try("negative length (5 args)", () => Array.Copy(src, 0, dst, 0, -1));
Try("long negative length", () => Array.Copy(src, dst, -5L));
Try("negative source index", () => Array.Copy(src, -1, dst, 0, 1));
Try("negative destination index", () => Array.Copy(src, 0, dst, -2, 1));
Try("source too short", () => Array.Copy(src, 2, dst, 0, 2));
Try("destination too short", () => Array.Copy(src, 0, dst, 2, 2));
Try("length too long", () => Array.Copy(src, dst, 4));
Try("null source", () => Array.Copy(nul, dst, 1));
Try("null destination", () => Array.Copy(src, nul, 1));
Try("null source (5 args)", () => Array.Copy(nul, 0, dst, 0, 1));
Try("null destination (5 args)", () => Array.Copy(src, 0, nul, 0, 1));
Try("negative length and index", () => Array.Copy(src, -1, dst, -1, -1));
Try("zero length at end", () => Array.Copy(src, 3, dst, 3, 0));
Try("huge long length", () => Array.Copy(src, dst, 3_000_000_000L));
Try("string[] to object[]", () => { object[] o = new object[2]; Array.Copy(new[] { "x", "y" }, o, 2); Console.WriteLine(string.Join(",", o)); });
Try("ok overlapping", () => { Array.Copy(src, 0, src, 1, 2); Console.WriteLine(string.Join(",", src)); });

static void Try(string label, Action action)
{
    try
    {
        action();
        Console.WriteLine($"{label}: no exception");
    }
    catch (ArgumentException e)
    {
        Console.WriteLine($"{label}: {e.GetType().Name}");
        Console.WriteLine($"  Message: {e.Message}");
        Console.WriteLine($"  ParamName: {e.ParamName ?? "(null)"}");
    }
}
