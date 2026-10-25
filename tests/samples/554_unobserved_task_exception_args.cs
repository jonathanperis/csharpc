// TaskScheduler.UnobservedTaskException handlers and their arguments, and GC.WaitForPendingFinalizers in a
// program without finalizers (nothing is pending, so it returns at once).
using System;
using System.Threading.Tasks;

int raised = 0;
EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
{
    raised++;
    Console.WriteLine($"handler: {args.Exception.InnerExceptions.Count} exception(s), observed {args.Observed}");
    args.SetObserved();
};
TaskScheduler.UnobservedTaskException += handler;

var manual = new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("lost")));
Console.WriteLine($"{manual.Exception.InnerException!.Message} observed {manual.Observed}");
manual.SetObserved();
Console.WriteLine($"after SetObserved: {manual.Observed}");
handler(null, new UnobservedTaskExceptionEventArgs(new AggregateException(new Exception("a"), new Exception("b"))));

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
TaskScheduler.UnobservedTaskException -= handler;
Console.WriteLine($"raised {raised}");
