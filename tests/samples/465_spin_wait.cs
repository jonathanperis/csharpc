// SpinWait (Roslyn's SingleInitNullable and lock-free caches spin with it): the count, the yield
// threshold, Reset and SpinUntil.
var spinner = new SpinWait();
var yields = new List<bool>();
for (var i = 0; i < 12; i++)
{
    yields.Add(spinner.NextSpinWillYield || Environment.ProcessorCount == 1);
    spinner.SpinOnce();
}
Console.WriteLine($"{spinner.Count} {string.Join("", yields.Select(y => y ? 'Y' : '.'))}");
spinner.Reset();
Console.WriteLine(spinner.Count);
spinner.SpinOnce(-1);
spinner.SpinOnce(5);
Console.WriteLine(spinner.Count);

var calls = 0;
SpinWait.SpinUntil(() => ++calls == 5);
Console.WriteLine(calls);
Console.WriteLine(SpinWait.SpinUntil(() => false, 0));
Console.WriteLine(SpinWait.SpinUntil(() => true, TimeSpan.FromMilliseconds(10)));
try { spinner.SpinOnce(-2); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { SpinWait.SpinUntil(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { SpinWait.SpinUntil(() => true, -5); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
