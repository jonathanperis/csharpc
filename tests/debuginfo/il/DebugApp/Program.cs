using DebugLib;

var counter = new Counter(7);
var label = Describe(counter);
Console.WriteLine(label);

static string Describe(Counter counter)
{
    var result = Counter.Describe(counter, "sum");
    return result;
}
