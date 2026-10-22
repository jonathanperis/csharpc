Base b = new Derived();
Console.WriteLine(b.Name());
Console.WriteLine(((Derived)b).Name());
class Base { public string Name() => "base"; }
sealed class Derived : Base { public new string Name() => "derived"; }
