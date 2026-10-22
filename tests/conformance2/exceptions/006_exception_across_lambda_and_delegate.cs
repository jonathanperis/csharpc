Func<int, int> f = x => x == 0 ? throw new DivideByZeroException() : 10 / x;
foreach (var v in new[] { 2, 0 }) {
    try { Console.WriteLine(f(v)); } catch (DivideByZeroException e) { Console.WriteLine(e.Message); }
}
Action a = () => throw new NotImplementedException();
try { a(); } catch (NotImplementedException e) { Console.WriteLine(e.Message); }
