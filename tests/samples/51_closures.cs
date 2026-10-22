// Capture semantics: shared vs. per-iteration variables, mutation through closures.
var actions = new Action[3];
for (int i = 0; i < 3; i++)
    actions[i] = () => Console.Write(i + " ");       // `for` variable: one shared variable
foreach (var a in actions) a();
Console.WriteLine();

int[] values = [10, 20, 30];
var printers = new Action[3];
int k = 0;
foreach (var v in values)
    printers[k++] = () => Console.Write(v + " ");    // foreach variable: fresh per iteration
foreach (var p in printers) p();
Console.WriteLine();

var fns = new Func<int>[3];
for (int i = 0; i < 3; i++)
{
    int copy = i * i;                                 // declared in the body: fresh per iteration
    fns[i] = () => copy;
}
foreach (var f in fns) Console.Write(f() + " ");
Console.WriteLine();

int counter = 0;
Action increment = () => counter++;
increment(); increment(); increment();
Console.WriteLine(counter);

string message = "before";
Action change = () => message = "after";
change();
Console.WriteLine(message);

// Two closures sharing one variable.
int shared = 0;
Action inc = () => shared += 2;
Func<int> get = () => shared;
inc(); inc();
Console.WriteLine(get());

// Nested scopes: inner lambda captures from two levels.
int outer = 1;
Func<int, Func<int>> make = x => { int mid = x * 10; return () => outer + mid + x; };
Console.WriteLine(make(2)());
outer = 100;
Console.WriteLine(make(3)());

var b = new Box(5);
Func<int> viaThis = b.Getter();
b.Value = 42;
Console.WriteLine(viaThis());

class Box(int initial)
{
    public int Value = initial;
    public Func<int> Getter() => () => Value;        // captures `this`
}
