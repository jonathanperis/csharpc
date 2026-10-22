foreach (var a in new[] { new int[0], new[] { 1 }, new[] { 1, 2 }, new[] { 1, 2, 3, 4 } })
    Console.WriteLine(a switch
    {
        [] => "empty",
        [var x] => $"one: {x}",
        [1, 2] => "one-two",
        [var first, .., var last] => $"{first}..{last}",
    });
