static void T(Action a) { try { a(); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); } }
int zero = 0; int[] arr = new int[2]; string? s = null; object o = "x";
T(() => Console.WriteLine(1 / zero));
T(() => Console.WriteLine(arr[5]));
T(() => Console.WriteLine(s!.Length));
T(() => Console.WriteLine((int)o));
T(() => Console.WriteLine(int.Parse("abc")));
T(() => Console.WriteLine(new List<int>()[0]));
T(() => Console.WriteLine(new Dictionary<string, int>()["k"]));
