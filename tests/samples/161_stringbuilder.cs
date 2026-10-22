// StringBuilder (C# corelib): API surface, growth, and .NET's exact exception messages.
using System.Text;
var sb = new StringBuilder("Hello");
T(() => sb.Insert(99, "x")); T(() => sb.Insert(-1, "x")); T(() => sb.Remove(-1, 1)); T(() => sb.Remove(1, -1)); T(() => sb.Remove(3, 99));
T(() => sb.Append('x', -1)); T(() => sb[99]); T(() => sb[-1]); T(() => sb.ToString(2, 99)); T(() => sb.ToString(-1, 1));
T(() => { sb.Length = -1; return sb; }); T(() => new StringBuilder(-1)); T(() => { sb[99] = 'x'; return sb; });
T(() => sb.Replace("", "x")); T(() => sb.Replace(null!, "x")); T(() => sb.Insert(1, 'c'));
var b2 = new StringBuilder(); b2.Append(12).Append(-7L).Append(3u).Append(true).Append(2.5).Append('z').Append((object)null!).Append((string)null!).Append(1.5m).Append(new char[]{'a','b'}).AppendLine().Append("x");
b2.Length = 3; Console.WriteLine($"[{b2}] {b2.Length} {b2.Capacity >= 3}");
b2.Length = 6; Console.WriteLine($"[{b2.ToString().Replace('\0','0')}]");
static void T(Func<object> f) { try { Console.WriteLine(f()); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message.Replace("\n","\\n")}"); } }
var big = new StringBuilder();
for (int i = 0; i < 1000; i++) big.Append(i % 10).Append(',');
big.Insert(0, "start:").Remove(6, 4).Replace(",", ";").Replace('9', 'n');
Console.WriteLine(big.Length + " " + big.ToString(0, 20) + " " + big[big.Length - 2]);
var j = new StringBuilder().AppendJoin(", ", new[] { 1, 2, 3 }).Append(" | ").AppendJoin('-', "a", "b", "c").Append(" | ").AppendJoin("+", (object)1, "x", 2.5);
Console.WriteLine(j);
Console.WriteLine(new StringBuilder("abc", 100).Append(long.MinValue).Append(ulong.MaxValue).Append((short)-5).Append((byte)200));
Console.WriteLine(new StringBuilder().Append("hello world", 6, 5).Append(new[] { 'x', 'y', 'z' }, 1, 2).Insert(0, true).Insert(4, 'Q'));
