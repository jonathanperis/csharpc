// Type tests and casts against ManualResetEvent and AutoResetEvent (System.Transactions casts its lock's handle to
// ManualResetEvent), including the handles the library makes (CancellationToken.WaitHandle, Task.AsyncWaitHandle,
// SemaphoreSlim.AvailableWaitHandle are ManualResetEvents) and the failing casts.
using System.Threading;

object manual = new ManualResetEvent(false);
object auto = new AutoResetEvent(false);
object text = "text";
object? none = null;

Console.WriteLine($"{manual is ManualResetEvent} {manual is AutoResetEvent} {auto is ManualResetEvent} {auto is AutoResetEvent} {text is ManualResetEvent} {none is ManualResetEvent}");
Console.WriteLine($"{(manual as ManualResetEvent) != null} {(auto as ManualResetEvent) != null} {(none as ManualResetEvent) != null} {(text as AutoResetEvent) != null}");

ManualResetEvent cast = (ManualResetEvent)manual;
cast.Set();
Console.WriteLine("cast: " + cast.WaitOne(0) + " " + ((ManualResetEvent)manual).WaitOne(0));
Console.WriteLine("null cast: " + ((ManualResetEvent?)none is null));

try { _ = (ManualResetEvent)auto; } catch (InvalidCastException e) { Console.WriteLine(e.Message); }
try { _ = (AutoResetEvent)manual; } catch (InvalidCastException e) { Console.WriteLine(e.Message); }
try { _ = (ManualResetEvent)text; } catch (InvalidCastException e) { Console.WriteLine(e.Message); }

using var source = new CancellationTokenSource();
object tokenHandle = source.Token.WaitHandle;
Console.WriteLine("token handle: " + (tokenHandle is ManualResetEvent) + " " + (tokenHandle is AutoResetEvent));
object taskHandle = ((IAsyncResult)Task.CompletedTask).AsyncWaitHandle;
Console.WriteLine("task handle: " + (taskHandle is ManualResetEvent) + " " + (taskHandle is AutoResetEvent));
using var semaphore = new SemaphoreSlim(1);
object semaphoreHandle = semaphore.AvailableWaitHandle;
Console.WriteLine("semaphore handle: " + (semaphoreHandle is ManualResetEvent) + " " + (semaphoreHandle is AutoResetEvent));

object[] mixed = { new ManualResetEvent(true), new AutoResetEvent(true), 5, "x", new ManualResetEventSlim(true) };
int manuals = 0, autos = 0;
foreach (object o in mixed) { if (o is ManualResetEvent) manuals++; else if (o is AutoResetEvent) autos++; }
Console.WriteLine($"mixed: {manuals} {autos}");

switch (manual)
{
    case AutoResetEvent: Console.WriteLine("switch: auto"); break;
    case ManualResetEvent m: Console.WriteLine("switch: manual " + m.WaitOne(0)); break;
}
