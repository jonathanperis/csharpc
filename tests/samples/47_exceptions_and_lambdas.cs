// Exceptions thrown inside lambdas and delegates, with finally inside the lambda.
Func<int, int> risky = x => x == 0 ? throw new ArgumentException("zero!") : 100 / x;
foreach (var v in new[] { 5, 0, 20 })
{
    try { Console.WriteLine(risky(v)); }
    catch (ArgumentException e) { Console.WriteLine("caught " + e.Message); }
}
int counter = 0;
Action bump = () => { try { counter++; throw new InvalidOperationException("in lambda"); } finally { counter += 10; } };
try { bump(); } catch (Exception e) { Console.WriteLine(e.Message + " " + counter); }
