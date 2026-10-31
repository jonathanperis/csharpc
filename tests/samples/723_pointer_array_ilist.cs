#:property AllowUnsafeBlocks=true
// Arrays of pointers through the non-generic IList and Array.GetValue (Roslyn holds byte*[] values): .NET
// can't box their elements and throws NotSupportedException("Specified type is not supported"); so does csharpc.
unsafe
{
    byte*[] a = new byte*[2];
    byte b = 7;
    a[1] = &b;
    object o = a;
    var list = (System.Collections.IList)o;
    Console.WriteLine($"{o is System.Collections.IList} {list.Count} {*a[1]} {a.Length} {o.GetType()}");
    try { Console.WriteLine(list[0]); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    try { list[0] = null; } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    try { Console.WriteLine(list.Contains(null)); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    try { Console.WriteLine(a.GetValue(0)); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

    // A struct holding a pointer (System.Reflection.Metadata's MemoryBlock): ValueType.Equals/GetHashCode.
    var x = new Block(&b, 1);
    var y = new Block(&b, 1);
    var z = new Block(null, 1);
    Console.WriteLine($"{x.Equals(y)} {x.Equals(z)} {x.GetHashCode() == y.GetHashCode()} {new System.Collections.Generic.HashSet<Block> { x, y, z }.Count}");
}

unsafe struct Block
{
    public byte* Pointer;
    public int Length;
    public Block(byte* pointer, int length) { Pointer = pointer; Length = length; }
}
