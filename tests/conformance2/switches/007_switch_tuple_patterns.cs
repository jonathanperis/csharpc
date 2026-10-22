static string Fizz(int i) => (i % 3, i % 5) switch { (0, 0) => "FizzBuzz", (0, _) => "Fizz", (_, 0) => "Buzz", _ => i.ToString() };
for (int i = 1; i <= 15; i++) Console.Write(Fizz(i) + " ");
Console.WriteLine();
