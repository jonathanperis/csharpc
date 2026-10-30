// Awaiting a dynamic value binds GetAwaiter/IsCompleted/GetResult at run time; csharpc doesn't bind that yet,
// so it must refuse the program (docs/adr/0019-dynamic.md) rather than guess an awaiter.
using System.Threading.Tasks;

dynamic pending = Task.FromResult(42);
var value = await pending;
Console.WriteLine(value);
