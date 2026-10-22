foreach (var c in "a1 Z!") Console.Write(c switch { >= 'a' and <= 'z' => 'l', >= '0' and <= '9' => 'd', ' ' => '_', >= 'A' and <= 'Z' => 'U', _ => '?' });
Console.WriteLine();
static int Days(Month m) => m switch { Month.Feb => 28, Month.Apr or Month.Jun => 30, _ => 31 };
Console.WriteLine($"{Days(Month.Feb)} {Days(Month.Apr)} {Days(Month.Jan)}");
enum Month { Jan, Feb, Mar, Apr, May, Jun }
