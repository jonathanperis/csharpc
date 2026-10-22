foreach (object o in new object[] { 5, "str", 2.5, new int[] { 1 } })
    Console.WriteLine(o switch
    {
        int i => $"int {i}",
        string s => $"string of {s.Length}",
        double => "a double",
        _ => "something else",
    });
