// AppDomain.ProcessExit: handlers run in subscription order when Main has returned and its foreground threads are done,
// with the domain as sender and EventArgs.Empty; a removed handler does not run; a handler can set the exit code.
var domain = AppDomain.CurrentDomain;
domain.ProcessExit += (sender, e) => Console.WriteLine($"first: sender={sender == domain} args={e == EventArgs.Empty} code={Environment.ExitCode}");
EventHandler removed = (sender, e) => Console.WriteLine("removed handler ran");
domain.ProcessExit += removed;
domain.ProcessExit -= removed;
domain.ProcessExit += (sender, e) => { Console.WriteLine("second: setting the exit code to 7"); Environment.ExitCode = 7; };

var worker = new Thread(() => { Thread.Sleep(100); Console.WriteLine("foreground thread done"); });
worker.Start();
Console.WriteLine("main done");
