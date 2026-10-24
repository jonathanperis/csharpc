// System.Buffers.MemoryPool<T> (the shared ArrayMemoryPool: default and requested sizes from the shared
// ArrayPool's buckets, disposal, argument checks), IMemoryOwner<T>, and Memory<T> / ReadOnlyMemory<T>
// formatted in interpolations and boxed (ToString is the characters for char, Equals compares the view).
using System;
using System.Buffers;

Memory<char> memory = new char[8];
"12345".AsSpan().CopyTo(memory.Span);
memory.Span.Slice(5).Fill('.');
Console.WriteLine($"'{memory}' '{memory.Slice(0, 5)}'");
ReadOnlyMemory<char> ro = "hello world".AsMemory(6);
Console.WriteLine($"[{ro}] {ro.Length} {ro.IsEmpty}");
object boxed = ro;
Console.WriteLine(boxed);
Console.WriteLine(ro.ToString());
Console.WriteLine(boxed.GetType() + " " + boxed.Equals(ro) + " " + boxed.Equals(ro.Slice(1)) + " " + (boxed is ReadOnlyMemory<char> back && back.Length == 5));
Console.WriteLine(boxed is Memory<char>);
object boxedInts = new Memory<int>(new int[4], 1, 2);
Console.WriteLine(boxedInts + " " + boxedInts.GetType());
Memory<int> ints = new int[] { 1, 2, 3 };
Console.WriteLine(ints.ToString());
using (IMemoryOwner<char> owner = MemoryPool<char>.Shared.Rent())
{
    Console.WriteLine(owner.Memory.Length);
}
using (var owner = MemoryPool<byte>.Shared.Rent(100))
{
    Console.WriteLine(owner.Memory.Length + " " + MemoryPool<byte>.Shared.MaxBufferSize);
}
using (var owner = MemoryPool<int>.Shared.Rent())
{
    Console.WriteLine(owner.Memory.Length);
}
var o2 = MemoryPool<char>.Shared.Rent(10);
o2.Dispose();
try { _ = o2.Memory; } catch (ObjectDisposedException e) { Console.WriteLine(e.Message); }
try { MemoryPool<char>.Shared.Rent(-2); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
