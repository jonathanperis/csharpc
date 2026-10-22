// Lambdas converted to Func/Action delegates, with and without captures.
Func<int, int> square = x => x * x;
Func<int, int, int> add = (a, b) => a + b;
Func<double> pi = () => Math.PI;
Action<string> shout = s => Console.WriteLine(s + "!");
Console.WriteLine(square(7));
Console.WriteLine(add(2, 40));
Console.WriteLine(pi());
shout("hello");

int factor = 3;
Func<int, int> scale = x => x * factor;
Console.WriteLine(scale(14));
factor = 10;                                  // captured by reference, not by value
Console.WriteLine(scale(14));

Predicate<int> isEven = n => n % 2 == 0;
Console.WriteLine(isEven(4) + " " + isEven(7));

Func<int, bool> between = n => n > 1 && n < 10;
Console.WriteLine(between(5));
Func<string, int> len = s => s.Length;
Console.WriteLine(len("lambda"));

Action noop = () => { };
noop();
Func<int, Func<int, int>> adder = a => b => a + b;   // currying: nested lambdas
Console.WriteLine(adder(5)(6));

Func<int, string>? maybe = null;
Console.WriteLine(maybe?.Invoke(1) ?? "none");
maybe = n => "#" + n;
Console.WriteLine(maybe?.Invoke(9) ?? "none");
Console.WriteLine(maybe.Invoke(3));
