foreach (var cmd in new[] { "start", "stop", "other" })
    switch (cmd)
    {
        case "start": Console.WriteLine("starting"); break;
        case "stop": Console.WriteLine("stopping"); break;
        default: Console.WriteLine("unknown " + cmd); break;
    }
