// The CoreLib types served from the framework's IL, under load (#382). wasm32 runs the slice cut from the 32-bit CoreLib and every other
// target the 64-bit one; both must print what .NET prints. Large counts, every key in one bucket with removals and re-adds, StringBuilder
// chunk growth with inserts and removes across chunks, Queue wrap-around, the seeded generator on every typed path; results are folded
// into checksums.
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;

static ulong Mix(ulong h, ulong v) => (h ^ v) * 1099511628211UL;

// Dictionary / HashSet: 300 000 int keys (FastMod buckets on 64-bit), then colliding keys (one bucket), removals and re-adds.
var dict = new Dictionary<int, int>();
for (int i = 0; i < 300_000; i++) dict[i * 7919] = i;
for (int i = 0; i < 300_000; i += 3) dict.Remove(i * 7919);
for (int i = 0; i < 50_000; i++) dict[-i - 1] = i;
ulong h = 14695981039346656037UL;
foreach (var (k, v) in dict) h = Mix(Mix(h, (ulong)k), (ulong)v);
Console.WriteLine($"dict {dict.Count} {h:X16}");

var colliding = new Dictionary<Key, int>();
for (int i = 0; i < 4000; i++) colliding[new Key(i)] = i;
for (int i = 0; i < 4000; i += 2) colliding.Remove(new Key(i));
for (int i = 4000; i < 5000; i++) colliding.Add(new Key(i), i);
h = 14695981039346656037UL;
foreach (var (k, v) in colliding) h = Mix(Mix(h, (ulong)k.Value), (ulong)v);
Console.WriteLine($"colliding {colliding.Count} {colliding.ContainsKey(new Key(4001))} {colliding.ContainsKey(new Key(2))} {h:X16}");

var set = new HashSet<long>();
for (long i = 0; i < 200_000; i++) set.Add(i * 1_000_003L);
set.RemoveWhere(x => x % 3 == 0);
set.TrimExcess();
h = 14695981039346656037UL;
foreach (var x in set) h = Mix(h, (ulong)x);
Console.WriteLine($"set {set.Count} {set.Contains(1_000_003L)} {set.Contains(0)} {h:X16}");

var strings = new HashSet<string>(StringComparer.Ordinal);
for (int i = 0; i < 100_000; i++) strings.Add("key" + i);
Console.WriteLine($"strings {strings.Count} {strings.Contains("key99999")} {strings.Contains("key100000")}");

// List / CollectionsMarshal: growth past 2^20 elements, span access, SetCount.
var list = new List<long>();
for (int i = 0; i < 1_200_000; i++) list.Add(i * 3L);
list.RemoveRange(1000, 500_000);
list.InsertRange(10, Enumerable.Range(0, 1000).Select(x => (long)-x));
var span = CollectionsMarshal.AsSpan(list);
long sum = 0;
foreach (var x in span) sum += x;
CollectionsMarshal.SetCount(list, 100);
Console.WriteLine($"list {span.Length} {sum} {list.Count} {list[99]}");

// Queue: wrap-around while growing.
var queue = new Queue<int>();
long qsum = 0;
for (int round = 0; round < 200; round++)
{
    for (int i = 0; i < 1000 + round; i++) queue.Enqueue(round * 10_000 + i);
    for (int i = 0; i < 900; i++) qsum += queue.Dequeue();
}
queue.TrimExcess();
Console.WriteLine($"queue {queue.Count} {queue.Peek()} {qsum}");

// ObjectModel.
var collection = new Collection<int>(Enumerable.Range(0, 10_000).ToList());
collection.Insert(5000, -1);
collection.RemoveAt(0);
var readOnly = new ReadOnlyCollection<int>(collection);
var readOnlyDict = new ReadOnlyDictionary<int, int>(dict);
Console.WriteLine($"objectmodel {readOnly.Count} {readOnly[4999]} {readOnly.IndexOf(-1)} {readOnlyDict.Count} {readOnlyDict[7919 * 4]}");

// StringBuilder: chunk growth to megabytes, inserts and removes across chunk boundaries, Replace, chunk enumeration.
var sb = new StringBuilder();
for (int i = 0; i < 200_000; i++) sb.Append(i).Append(',');
for (int i = 0; i < 2000; i++) sb.Insert(i * 397 % sb.Length, "<" + i + ">");
for (int i = 0; i < 2000; i++) sb.Remove(i * 911 % (sb.Length - 10), 7);
sb.Replace("12", "twelve");
int chunks = 0; long chunkChars = 0;
foreach (var chunk in sb.GetChunks()) { chunks++; chunkChars += chunk.Length; }
var text = sb.ToString();
h = 14695981039346656037UL;
foreach (char c in text) h = Mix(h, c);
Console.WriteLine($"stringbuilder {sb.Length} {chunkChars == sb.Length} {chunks > 1} {h:X16}");

// MemoryExtensions on a large span.
var bytes = new byte[3_000_001];
for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 31 % 251);
bytes[2_999_990] = 255;
Console.WriteLine($"memory {bytes.AsSpan().IndexOf((byte)255)} {bytes.AsSpan().LastIndexOf((byte)0)} {bytes.AsSpan().Count((byte)7)} {bytes.AsSpan(1).SequenceEqual(bytes.AsSpan(1))}");

// Random: the seeded (legacy) generator on every typed path, nint included.
var random = new Random(12345);
h = 14695981039346656037UL;
for (int i = 0; i < 100_000; i++)
{
    h = Mix(h, (ulong)random.Next(1000));
    h = Mix(h, (ulong)random.NextInt64(-5_000_000_000L, 5_000_000_000L));
    h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(random.NextDouble()));
    h = Mix(h, (ulong)BitConverter.SingleToInt32Bits(random.NextSingle()));
}
var buffer = new byte[4096];
random.NextBytes(buffer);
foreach (var b in buffer) h = Mix(h, b);
var items = new int[1000];
for (int i = 0; i < items.Length; i++) items[i] = i;
random.Shuffle(items);
foreach (var x in random.GetItems<int>(items, 500)) h = Mix(h, (ulong)x);
Console.WriteLine($"random {h:X16} {items[0]} {items[999]}");
var unseeded = new Random();
bool inRange = true;
for (int i = 0; i < 100_000; i++)
{
    if ((uint)unseeded.Next(1000) >= 1000) inRange = false;
    long l = unseeded.NextInt64(-3, 3); if (l < -3 || l >= 3) inRange = false;
    double d = unseeded.NextDouble(); if (d < 0 || d >= 1) inRange = false;
}
Console.WriteLine($"unseeded in range {inRange}");

// Threading types, single-threaded paths.
var rwls = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
for (int i = 0; i < 1000; i++) { rwls.EnterReadLock(); rwls.EnterReadLock(); rwls.ExitReadLock(); rwls.ExitReadLock(); rwls.EnterWriteLock(); rwls.ExitWriteLock(); }
var spin = new SpinLock(false);
int taken = 0;
for (int i = 0; i < 1000; i++) { bool t = false; spin.Enter(ref t); if (t) { taken++; spin.Exit(); } }
using var local = new ThreadLocal<long>(() => 42, trackAllValues: true);
for (int i = 0; i < 1000; i++) local.Value += i;
Console.WriteLine($"threading {rwls.RecursiveReadCount} {rwls.IsWriteLockHeld} {taken} {local.Value} {local.Values.Count}");

readonly record struct Key(int Value)
{
    public override int GetHashCode() => 17; // every key in one bucket
}
