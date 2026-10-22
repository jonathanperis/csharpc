// Local functions that capture enclosing variables (and each other).
int total = 0;
void Add(int x) => total += x;
Add(5); Add(7);
Console.WriteLine(total);

Console.WriteLine(SumTo(10));
Console.WriteLine(new Greeter("Ada").Greet(3));

int limit = 4;
int CountDown(int n) => n <= limit ? n : CountDown(n - 1);   // recursive + capturing
Console.WriteLine(CountDown(9));

int depth = 0;
void Outer() { depth++; Inner(); }
void Inner() { depth += 10; }
Outer();
Console.WriteLine(depth);

int seed = 40;
Func<int> asDelegate = Next;
int Next() => ++seed;
Console.WriteLine(asDelegate());
Console.WriteLine(asDelegate());

static int SumTo(int n)
{
    int sum = 0;
    for (int i = 1; i <= n; i++) Accumulate(i);
    return sum;
    void Accumulate(int x) => sum += x;
}

class Greeter(string name)
{
    public string Greet(int times)
    {
        string result = "";
        for (int i = 0; i < times; i++) Append();
        return result;
        void Append() => result += "Hi " + name + "; ";
    }
}
