// A static constructor runs on first use of its type (instance creation or static member access),
// and never if the type is never used. csharpc runs it eagerly at program start: an unused type's
// side effect appears, and order relative to Main's output changes.
// .NET: "start", "Lazy..cctor", "made"      csharpc: "Unused..cctor", ... before "start"
// Found in dotnet/docs docs/csharp/programming-guide/classes-and-structs/snippets/constructors.
using System;

Console.WriteLine("start");
_ = new Lazy();
Console.WriteLine("made");

class Unused
{
    static Unused() => Console.WriteLine("Unused..cctor");
}

class Lazy
{
    static Lazy() => Console.WriteLine("Lazy..cctor");
}
