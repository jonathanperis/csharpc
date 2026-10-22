var c = new C();
Console.WriteLine(((IA)c).Name() + " " + ((IB)c).Name());
interface IA { string Name(); }
interface IB { string Name(); }
class C : IA, IB { string IA.Name() => "A"; string IB.Name() => "B"; }
