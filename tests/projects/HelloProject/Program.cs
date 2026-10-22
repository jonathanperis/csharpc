using Hello.Model;
using MathLib;

#if FANCY_GREETING
Console.WriteLine("*** Hello from a multi-project app, compiled by csharpc ***");
#else
Console.WriteLine("Hello");
#endif

var primes = Primes.FirstN(10);
Console.WriteLine("primes: " + string.Join(", ", ToStrings(primes)));

var v = new Vector(3, 4).Add(new Vector(1, 1)).Scale(0.5);   // Vector comes from a <Using> item
Console.WriteLine($"vector {v} has length {v.Length}");

var inventory = new Inventory("warehouse");
inventory.Add("apples", 12);
inventory.Add("pears", 7);
inventory.Add("apples", 3);
Console.WriteLine(inventory.Report());

var sb = new Builder();                                       // global using alias
sb.Append("built with ").Append(nameof(Builder));
Console.WriteLine(sb.ToString());

static string[] ToStrings(int[] values)
{
    var result = new string[values.Length];
    for (int i = 0; i < values.Length; i++) result[i] = values[i].ToString();
    return result;
}
