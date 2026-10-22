// ref locals, ref returns, ref reassignment, ref conditionals, in/ref readonly parameters.
int[] scores = [10, 20, 30];
ref int first = ref scores[0];
first = 11;
first++;
Console.WriteLine(scores[0]);

ref int largest = ref Largest(scores);
largest = 100;
Console.WriteLine(string.Join(",", scores));

Largest(scores) += 5;
Console.WriteLine(scores[2]);

int a = 1, b = 2;
bool pickA = false;
ref int chosen = ref (pickA ? ref a : ref b);
chosen = 42;
Console.WriteLine($"{a} {b}");

chosen = ref a;
chosen = 7;
Console.WriteLine($"{a} {b}");

var holder = new Holder();
ref int slot = ref holder.Slot();
slot = 9;
Console.WriteLine(holder.Value);
holder.Counter.Increment();
holder.Counter.Increment();
Console.WriteLine(holder.Counter.Count);

var big = new Big { A = 1, B = 2, C = 3, D = 4 };
Console.WriteLine(SumIn(in big) + SumRefReadonly(ref big));
Bump(ref big.A);
Console.WriteLine(big.A);

ref readonly int ro = ref scores[1];
Console.WriteLine(ro);

static ref int Largest(int[] values)
{
    int index = 0;
    for (int i = 1; i < values.Length; i++)
        if (values[i] > values[index]) index = i;
    return ref values[index];
}

static long SumIn(in Big big) => big.A + big.B + big.C + big.D;
static long SumRefReadonly(ref readonly Big big) => big.A * big.D;
static void Bump(ref long value) => value += 1000;

class Holder
{
    public int Value;
    public CounterStruct Counter;
    public ref int Slot() => ref Value;
}

struct CounterStruct
{
    public int Count;
    public void Increment() => Count++;
}

struct Big { public long A, B, C, D; }
