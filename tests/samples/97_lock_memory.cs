// lock (object and System.Threading.Lock), Memory<T>, EventArgs.Empty, IntPtr.Size.
var gate = new object();
var modern = new System.Threading.Lock();
int counter = 0;
for (int i = 0; i < 3; i++)
{
    lock (gate) { counter++; }
    lock (modern) { counter += 10; }
}
Console.WriteLine(counter);

int[] numbers = [1, 2, 3, 4, 5, 6];
Memory<int> memory = numbers;
Memory<int> tail = memory.Slice(2);
tail.Span[0] = 30;
Console.WriteLine($"{numbers[2]} {tail.Length} {memory.Slice(1, 2).Span[1]}");
ReadOnlyMemory<char> text = "memory".AsMemory(1, 3);
Console.WriteLine(text.Span.ToString() + " " + text.Length);
Console.WriteLine(Total(memory));

var source = new Source();
source.Fired += (sender, e) => Console.WriteLine($"fired by {sender}, empty args: {e == EventArgs.Empty}");
source.Fire();
Console.WriteLine(IntPtr.Size == 8 || IntPtr.Size == 4);

static int Total(ReadOnlyMemory<int> values)
{
    int sum = 0;
    foreach (var v in values.Span) sum += v;
    return sum;
}

class Source
{
    public event EventHandler? Fired;
    public void Fire() => Fired?.Invoke(this, EventArgs.Empty);
    public override string ToString() => "Source";
}
