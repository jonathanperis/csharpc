// Objects reachable only from locals, temporaries and arguments while other allocations (and
// collections) happen. Meant to run under CSRT_GC_STRESS=1 too: on WebAssembly those values live in
// wasm locals, which the collector only sees through the compiler's stack spill slots.
using System.Text;

static int[] Fill(int n, int seed)
{
    var a = new int[n];
    for (int i = 0; i < n; i++) a[i] = seed * 31 + i;
    return a;
}

static long Sum(int[] a)
{
    long s = 0;
    foreach (var x in a) s += x;
    return s;
}

static string Describe(Node n) => n.Next is null ? $"[{n.Value}]" : $"[{n.Value}]" + Describe(n.Next);

static Node Chain(int length)
{
    Node head = null!;
    for (int i = length; i > 0; i--) head = new Node(i, head);
    return head;
}

long total = 0;
for (int round = 0; round < 200; round++)
{
    // A temporary array whose only reference is the evaluation stack while Fill allocates again.
    total += Sum(Fill(16, round)) + Sum(Fill(8, round + 1));

    // Locals live across allocations.
    var keep = Fill(5, round);
    var text = new StringBuilder();
    for (int i = 0; i < 10; i++) text.Append(i).Append(',');
    var list = new List<string>();
    for (int i = 0; i < 10; i++) list.Add(text.ToString() + i);
    total += Sum(keep) + list.Count + list[9].Length;

    // A linked structure reachable only from a local.
    var chain = Chain(20);
    var junk = new object[50];
    for (int i = 0; i < junk.Length; i++) junk[i] = new int[4];
    total += Describe(chain).Length;

    // Values captured by a closure and passed through a delegate.
    var captured = Fill(3, round);
    Func<int, long> f = k => captured[k % 3] + Fill(2, k)[1];
    total += f(round);

    // Exceptions carry references too.
    try { throw new InvalidOperationException("round " + round); }
    catch (InvalidOperationException e) { total += e.Message.Length + Fill(2, round)[0]; }
}
Console.WriteLine(total);

var dict = new Dictionary<string, int[]>();
for (int i = 0; i < 300; i++) dict["k" + i] = Fill(4, i);
long check = 0;
foreach (var (key, value) in dict) check += key.Length + Sum(value);
Console.WriteLine(check);

record Node(int Value, Node? Next);
