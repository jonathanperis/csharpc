// ValueTask<T> results held inline: default results (which csharpc represents without a task), other
// results, async ValueTask methods that finish without suspending, and results larger than 64 bytes.
using System.Globalization;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

Show("int 0", new ValueTask<int>(0));
Show("int 7", new ValueTask<int>(7));
Show("FromResult 0", ValueTask.FromResult(0));
Show("FromResult 42", ValueTask.FromResult(42));
Show("bool false", new ValueTask<bool>(false));
Show("bool true", new ValueTask<bool>(true));
Show("double 0", new ValueTask<double>(0.0));
Show("double -0", new ValueTask<double>(-0.0));
Show("float -0", new ValueTask<float>(-0.0f));
Show("decimal 0.00", new ValueTask<decimal>(0.00m));
Show("decimal 0", new ValueTask<decimal>(0m));
Show("string null", new ValueTask<string?>((string?)null));
Show("string empty", new ValueTask<string>(""));
Show("int? null", new ValueTask<int?>((int?)null));
Show("int? 0", new ValueTask<int?>(0));
Show("pair default", new ValueTask<Pair>(default(Pair)));
Show("pair (0, 1)", new ValueTask<Pair>(new Pair(0, 1)));
Show("pair (1, 0)", new ValueTask<Pair>(new Pair(1, 0)));
Show("tuple default", new ValueTask<(byte, long)>((0, 0L)));
Show("tuple (1, 0)", new ValueTask<(byte, long)>((1, 0L)));
Show("big default", new ValueTask<Big>(default(Big)));
Show("big set", new ValueTask<Big>(Big.Make(3)));
Show("default(ValueTask<Big>)", default(ValueTask<Big>));
Show("default(ValueTask<long>)", default(ValueTask<long>));

for (int i = 0; i < 3; i++)
{
    Show($"sync {i}", Compute(i, suspend: false));
    Console.WriteLine($"async {i}: [{await Compute(i, suspend: true)}]");
}
Show("sync big", MakeBig(0, suspend: false));
Show("sync big 5", MakeBig(5, suspend: false));
Console.WriteLine($"async big 5: [{await MakeBig(5, suspend: true)}]");

ValueTask<int> zero = new ValueTask<int>(0);
Task<int> asTask = zero.AsTask();
Console.WriteLine($"AsTask: {asTask.IsCompletedSuccessfully} {asTask.Result} {asTask.Status}");
Console.WriteLine($"awaited: {await zero.ConfigureAwait(false)} {await new ValueTask<long>(0L)} {await Compute(0, suspend: false)}");
Console.WriteLine($"preserved: {zero.Preserve().Result}");
try
{
    await Fail(suspend: false);
}
catch (InvalidOperationException e)
{
    Console.WriteLine($"caught {e.Message}");
}

static void Show<T>(string label, ValueTask<T> task)
{
    var completed = task.IsCompleted;
    var ok = task.IsCompletedSuccessfully;
    var result = task.IsCompleted ? task.Result : task.AsTask().GetAwaiter().GetResult();
    Console.WriteLine($"{label}: {completed} {ok} [{result?.ToString() ?? "null"}]");
}

static async ValueTask<int> Compute(int value, bool suspend)
{
    if (suspend) await Task.Yield();
    return value;
}

static async ValueTask<Big> MakeBig(int seed, bool suspend)
{
    if (suspend) await Task.Yield();
    return seed == 0 ? default : Big.Make(seed);
}

static async ValueTask<int> Fail(bool suspend)
{
    if (suspend) await Task.Yield();
    throw new InvalidOperationException("sync failure");
}

record struct Pair(byte A, long B);

struct Big
{
    public long A, B, C, D, E, F, G, H, I, J, K, L;
    public static Big Make(long seed) => new Big { A = seed, B = seed + 1, F = seed * 3, L = seed * 100 };
    public override string ToString() => $"{A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L}";
}
