// An exception escaping an async void method crashes the process (unhandled), like .NET.
Console.WriteLine("start");
FireAndForget();
await Task.Delay(2000);
Console.WriteLine("never printed");

async void FireAndForget()
{
    await Task.Delay(1);
    try { throw new InvalidOperationException("inner handled"); }
    catch (InvalidOperationException e) { Console.WriteLine("handled " + e.Message); }
    finally { Console.WriteLine("finally runs first"); }
    throw new InvalidOperationException("async void boom");
}
