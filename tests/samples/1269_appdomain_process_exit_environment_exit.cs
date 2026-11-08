// AppDomain.ProcessExit is raised by Environment.Exit too, before the process ends with the code given (and what the
// handlers print is flushed).
AppDomain.CurrentDomain.ProcessExit += (sender, e) => Console.WriteLine("exit handler 1");
AppDomain.CurrentDomain.ProcessExit += (sender, e) => Console.WriteLine("exit handler 2, code " + Environment.ExitCode);
Console.WriteLine("exiting with 3");
Environment.Exit(3);
Console.WriteLine("not reached");
