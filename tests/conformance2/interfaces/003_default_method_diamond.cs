IC c = new Impl();
Console.WriteLine(c.Name());
IA a = c;
Console.WriteLine(a.Name());
interface IA { string Name() => "A"; }
interface IB : IA { string IA.Name() => "B"; }
interface IC : IA { string IA.Name() => "C"; }
class Impl : IB, IC { public string Name() => "Impl"; string IA.Name() => "Impl.IA"; }
