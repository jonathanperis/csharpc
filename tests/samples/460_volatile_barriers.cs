// Volatile.ReadBarrier / WriteBarrier (.NET 10): fences without an operand (Roslyn's
// RoslynImmutableInterlocked.VolatileRead uses them).
int[] data = new int[4];
bool ready = false;
for (var i = 0; i < data.Length; i++) data[i] = i * i;
Volatile.WriteBarrier();
Volatile.Write(ref ready, true);
if (Volatile.Read(ref ready))
{
    Volatile.ReadBarrier();
    Console.WriteLine(string.Join(",", data));
}
var published = await Task.Run(() =>
{
    var local = new[] { 1, 2, 3 };
    Volatile.WriteBarrier();
    return local;
});
Volatile.ReadBarrier();
Console.WriteLine(published.Sum());
