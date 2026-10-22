static string D(object o) => o switch { List<int> l => $"ints {l.Count}", List<string> => "strings", IEnumerable<char> s => "chars " + string.Concat(s), int[] { Length: > 1 } arr => $"arr {arr[1]}", _ => "?" };
Console.WriteLine(D(new List<int> { 1 }) + " " + D(new List<string>()) + " " + D("hey") + " " + D(new[] { 1, 2 }));
