// Index and Range static members (what compiled C# calls for `[..]`, and what source can name):
// Range.All/StartAt/EndAt, Index.Start/End/FromStart/FromEnd, with .NET's argument checks.
using System;

int[] data = [10, 20, 30, 40, 50];
Console.WriteLine(string.Join(",", data[Range.All]) + " | " + string.Join(",", data[Range.StartAt(3)]) + " | " + string.Join(",", data[Range.EndAt(^3)]));
Console.WriteLine($"{Index.Start} {Index.End} {Index.FromStart(2)} {Index.FromEnd(1)} {data[Index.FromEnd(1)]} {data[Index.FromStart(1)]}");
Console.WriteLine($"{Range.All} {Range.StartAt(^1)} {Range.EndAt(2)} {Range.All.Equals(..)} {Index.End.Equals(^0)}");
var span = data.AsSpan()[Range.StartAt(1)];
Console.WriteLine(span.Length + " " + span[0]);
try { Index.FromEnd(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { Index.FromStart(-2); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
