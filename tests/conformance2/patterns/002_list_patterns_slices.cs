static string M(int[] a) => a switch { [] => "empty", [var x] => $"one {x}", [var f, .., var l] when f == l => $"same ends {f}", [0, ..] => "starts 0", [.., 9] => "ends 9", _ => $"len {a.Length}" };
foreach (var a in new[] { new int[0], new[] { 4 }, new[] { 3, 1, 3 }, new[] { 0, 1 }, new[] { 1, 9 }, new[] { 1, 2, 3 } }) Console.WriteLine(M(a));
