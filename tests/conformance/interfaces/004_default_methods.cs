ILogger l = new ConsoleLogger();
l.Log("x");
l.LogTwice("y");
interface ILogger { void Log(string m); void LogTwice(string m) { Log(m); Log(m); } }
class ConsoleLogger : ILogger { public void Log(string m) => Console.WriteLine("log " + m); }
